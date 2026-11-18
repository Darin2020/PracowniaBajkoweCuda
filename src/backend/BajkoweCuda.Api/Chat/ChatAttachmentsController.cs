using BajkoweCuda.Api.Auth;
using BajkoweCuda.Api.Problems;
using BajkoweCuda.Api.Security;
using BajkoweCuda.Application.Common;
using BajkoweCuda.Infrastructure.Chat;
using BajkoweCuda.Infrastructure.Identity;
using BajkoweCuda.Infrastructure.Imaging;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BajkoweCuda.Api.Chat;

/// <summary>
/// <c>POST /api/chat/attachments</c> (docs/PLAN.md §6): one image per request, as multipart field <c>file</c>. The
/// answer is the attachment, which is then sent with a message (by its id) within a day.
/// </summary>
[ApiController]
[Route("api/chat/attachments")]
public sealed class ChatAttachmentsController(UserManager<ApplicationUser> users, Attachments attachments) : ControllerBase
{
    /// <summary>A little above the 10 MB file limit, for the multipart envelope; larger requests are cut off by the server (413).</summary>
    private const long RequestLimit = ImageProcessor.MaxBytes + (512 * 1024);

    [HttpPost]
    [RequestSizeLimit(RequestLimit)]
    [RequestFormLimits(MultipartBodyLengthLimit = RequestLimit)]
    [EnableRateLimiting(RateLimiting.Uploads)]
    public async Task<IActionResult> Upload(CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is null || user.IsDeleted)
        {
            return AuthErrors.NotAuthenticated.ToResult();
        }

        // Chatting needs a confirmed address (§3.2); so does sending images to the chat.
        if (!user.EmailConfirmed)
        {
            return ChatApiErrors.EmailNotConfirmed.ToResult();
        }

        // Read by hand rather than bound, so a missing file gets this endpoint's own error code.
        var file = Request.HasFormContentType ? (await Request.ReadFormAsync(cancellationToken)).Files.GetFile("file") : null;
        if (file is null || file.Length == 0)
        {
            return ChatApiErrors.FileMissing.ToResult();
        }

        if (file.Length > ImageProcessor.MaxBytes)
        {
            return new AppError(ImageErrors.TooLarge, ErrorKind.Validation).ToResult();
        }

        var content = new byte[file.Length];
        await using (var stream = file.OpenReadStream())
        {
            await stream.ReadExactlyAsync(content, cancellationToken);
        }

        var (attachment, error) = await attachments.UploadAsync(user.Id, content, cancellationToken);
        if (attachment is null)
        {
            return new AppError(error!, ErrorKind.Validation).ToResult();
        }

        var response = AttachmentResponse.From(attachment);
        return Created(response.Url, response);
    }
}

/// <summary>An image of the chat; both addresses check access on every request.</summary>
public sealed record AttachmentResponse(Guid Id, string Url, string ThumbnailUrl, int Width, int Height)
{
    public static AttachmentResponse From(Domain.Chat.Attachment attachment) => new(
        attachment.Id,
        FilesController.UrlOf(attachment.Id),
        FilesController.ThumbnailUrlOf(attachment.Id),
        attachment.Width,
        attachment.Height);
}

/// <summary>Error codes of the chat endpoints. Keep in step with the frontend's translations.</summary>
internal static class ChatApiErrors
{
    /// <summary>The chat opens once the email address is confirmed (§3.2).</summary>
    public static readonly AppError EmailNotConfirmed = new("chat.email_not_confirmed", ErrorKind.Forbidden);

    public static readonly AppError FileMissing = new("files.missing", ErrorKind.Validation);

    public static readonly AppError FileNotFound = new("files.not_found", ErrorKind.NotFound);

    /// <summary>The message is not in this conversation (or does not exist).</summary>
    public static readonly AppError MessageNotFound = new("chat.message_not_found", ErrorKind.NotFound);

    public static readonly AppError ConversationNotFound = new("chat.conversation_not_found", ErrorKind.NotFound);

    /// <summary>The setting belongs to the conversation, which the first message starts.</summary>
    public static readonly AppError NotStarted = new("chat.not_started", ErrorKind.Conflict);
}
