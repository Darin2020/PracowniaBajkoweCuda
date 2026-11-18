namespace BajkoweCuda.Domain.Chat;

/// <summary>Error codes of the chat rules. Keep in step with the frontend's translations.</summary>
public static class ChatErrors
{
    public const string EmptyMessage = "chat.empty_message";
    public const string MessageTooLong = "chat.message_too_long";
    public const string TooManyAttachments = "chat.too_many_attachments";

    /// <summary>
    /// The attachment does not exist for this user, or is already part of a message. One code for both, so nobody
    /// can probe other people's attachment ids.
    /// </summary>
    public const string AttachmentUnavailable = "chat.attachment_unavailable";
}
