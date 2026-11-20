using System.Text;
using BajkoweCuda.Api.Hosting;
using BajkoweCuda.Application.Chat;
using BajkoweCuda.Domain;
using Microsoft.AspNetCore.WebUtilities;

namespace BajkoweCuda.Api.Auth;

/// <summary>
/// Links in account emails. They open pages of the Angular app in the recipient's language
/// (docs/PLAN.md §4.4), which then call the API with the user id and the one-time code.
/// </summary>
public sealed class AccountLinks(PublicSiteOptions site) : IChatLinks
{
    public string ConfirmEmail(Guid userId, string token, string language) =>
        WithCode("confirm-email", userId, token, language);

    public string ResetPassword(Guid userId, string token, string language) =>
        WithCode("reset-password", userId, token, language);

    public string ForgotPassword(string language) => $"{site.Origin}/{Language(language)}/forgot-password";

    public string CustomerChat(string language) => $"{site.Origin}/{Language(language)}/chat";

    public string AdminConversation(Guid conversationId, string language) =>
        $"{site.Origin}/{Language(language)}/admin/conversations/{conversationId}";

    public string CustomerOrder(Guid orderId, string language) => $"{site.Origin}/{Language(language)}/orders/{orderId}";

    /// <summary>Identity tokens contain characters that are not URL-safe; they travel Base64Url-encoded.</summary>
    public static string EncodeToken(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    /// <summary>Returns <c>null</c> for a code that is not valid Base64Url.</summary>
    public static string? DecodeToken(string code)
    {
        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private string WithCode(string page, Guid userId, string token, string language) =>
        QueryHelpers.AddQueryString(
            $"{site.Origin}/{Language(language)}/{page}",
            new Dictionary<string, string?> { ["userId"] = userId.ToString(), ["code"] = EncodeToken(token) });

    private static string Language(string language) => Languages.Find(language) ?? Languages.Default;
}
