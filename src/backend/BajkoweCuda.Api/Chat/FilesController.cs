using BajkoweCuda.Api.Auth;
using BajkoweCuda.Api.Problems;
using BajkoweCuda.Application.Files;
using BajkoweCuda.Infrastructure.Chat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BajkoweCuda.Api.Chat;

/// <summary>
/// <c>GET /api/files/{id}</c> and <c>/thumbnail</c> (docs/PLAN.md §6, §8.4): the only way to the stored images. Every
/// request checks that the signed-in user may see the file; for anyone else it does not exist (404, no IDOR).
/// </summary>
[ApiController]
[Route("api/files")]
public sealed class FilesController(
    Attachments attachments,
    IFileStore files,
    IAuthorizationService authorization,
    UserManager<Infrastructure.Identity.ApplicationUser> users) : ControllerBase
{
    public static string UrlOf(Guid id) => $"/api/files/{id}";

    public static string ThumbnailUrlOf(Guid id) => $"/api/files/{id}/thumbnail";

    [HttpGet("{id:guid}")]
    public Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) => ServeAsync(id, thumbnail: false, cancellationToken);

    [HttpGet("{id:guid}/thumbnail")]
    public Task<IActionResult> Thumbnail(Guid id, CancellationToken cancellationToken) => ServeAsync(id, thumbnail: true, cancellationToken);

    private async Task<IActionResult> ServeAsync(Guid id, bool thumbnail, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(users.GetUserId(User), out var userId))
        {
            return AuthErrors.NotAuthenticated.ToResult();
        }

        // Admins see every conversation's images, in a session that meets the admin policy (2FA, §3.3).
        var isAdmin = (await authorization.AuthorizeAsync(User, AuthSetup.AdminPolicy)).Succeeded;
        var file = await attachments.FindReadableAsync(id, userId, isAdmin, thumbnail, cancellationToken);
        if (file is null)
        {
            return ChatApiErrors.FileNotFound.ToResult();
        }

        var stream = await files.OpenReadAsync(file.Container, file.BlobName, cancellationToken);
        if (stream is null)
        {
            return ChatApiErrors.FileNotFound.ToResult();
        }

        // Private: only this browser may cache it. X-Content-Type-Options: nosniff comes from the security headers.
        Response.Headers.CacheControl = "private, max-age=3600";
        return File(stream, file.ContentType);
    }
}
