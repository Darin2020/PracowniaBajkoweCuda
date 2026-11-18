using BajkoweCuda.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace BajkoweCuda.Api.Auth;

/// <summary>
/// Sign-in with Google and Facebook (docs/PLAN.md §3.2). Each provider is registered only when its settings are
/// present (<c>Auth:Google:ClientId/ClientSecret</c>, <c>Auth:Facebook:AppId/AppSecret</c>), so missing credentials
/// never break the app. In Azure the values come from Key Vault (secrets <c>Auth--Google--ClientId</c> ...).
/// </summary>
internal static class ExternalProviders
{
    /// <summary>Set by a provider that says the email address is verified (Google does; Facebook does not say).</summary>
    public const string EmailVerifiedClaim = "bc:email_verified";

    /// <summary>Between the provider's answer and the end of sign-in (or the complete-profile step).</summary>
    public const string ExternalCookieName = "__Host-bc.ext";
    public const string LocalExternalCookieName = "bc.ext";
    public static readonly TimeSpan ExternalCookieLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Items kept in the authentication properties across the round trip to the provider.</summary>
    public const string ModeItem = "bc.mode";
    public const string ReturnUrlItem = "bc.return_url";
    public const string LanguageItem = "bc.lang";

    public static void AddExternalProviders(this IServiceCollection services, IConfiguration configuration, AuthCookies cookies)
    {
        services.Configure<CookieAuthenticationOptions>(IdentityConstants.ExternalScheme, options =>
        {
            options.Cookie.Name = cookies.External;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = cookies.SecurePolicy;
            // Lax: the cookie is set at the end of a redirect chain that started on the provider's site.
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = ExternalCookieLifetime;
        });

        var authentication = services.AddAuthentication();

        var google = configuration.GetSection("Auth:Google");
        if (!string.IsNullOrWhiteSpace(google["ClientId"]) && !string.IsNullOrWhiteSpace(google["ClientSecret"]))
        {
            authentication.AddGoogle(options =>
            {
                options.ClientId = google["ClientId"]!;
                options.ClientSecret = google["ClientSecret"]!;
                // Google's userinfo says whether the address is verified; only then may an account be linked by email.
                options.ClaimActions.MapJsonKey(EmailVerifiedClaim, "email_verified");
                options.ClaimActions.MapJsonKey(EmailVerifiedClaim, "verified_email");
                Configure(options, cookies);
            });
        }

        var facebook = configuration.GetSection("Auth:Facebook");
        if (!string.IsNullOrWhiteSpace(facebook["AppId"]) && !string.IsNullOrWhiteSpace(facebook["AppSecret"]))
        {
            authentication.AddFacebook(options =>
            {
                options.AppId = facebook["AppId"]!;
                options.AppSecret = facebook["AppSecret"]!;
                Configure(options, cookies);
            });
        }
    }

    /// <summary>
    /// The provider's correlation cookie is Lax (Google and Facebook answer with a GET redirect), which also works on
    /// the plain-http local origin, where SameSite=None cookies are rejected. A refused consent or any other provider
    /// error ends on the sign-in page with <c>?error=external_failed</c> instead of an exception page.
    /// </summary>
    public static void Configure(RemoteAuthenticationOptions options, AuthCookies cookies)
    {
        options.SignInScheme = IdentityConstants.ExternalScheme;
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = cookies.SecurePolicy;
        options.Events.OnRemoteFailure = context =>
        {
            var language = context.Properties?.Items.TryGetValue(LanguageItem, out var value) == true ? value : null;
            context.Response.Redirect(FrontendUrl(language, "login", "error=external_failed"));
            context.HandleResponse();
            return Task.CompletedTask;
        };
    }

    /// <summary>A page of the Angular app (docs/PLAN.md §4.4), same origin, so a relative URL.</summary>
    public static string FrontendUrl(string? language, string page, string? query = null) =>
        $"/{Languages.Find(language ?? string.Empty) ?? Languages.Default}/{page}{(query is null ? string.Empty : "?" + query)}";

    /// <summary>
    /// Only local paths are accepted as return URLs (open-redirect protection, §6): "/pl/chat" yes;
    /// "https://evil", "//evil", "/\evil" and anything with a scheme or control characters no.
    /// </summary>
    public static string? SafeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl) || returnUrl.Length > 512 || returnUrl[0] != '/')
        {
            return null;
        }

        if (returnUrl.Length > 1 && (returnUrl[1] == '/' || returnUrl[1] == '\\'))
        {
            return null;
        }

        return returnUrl.Any(character => char.IsControl(character) || character == '\\') ? null : returnUrl;
    }

    /// <summary>The language of a return URL like "/pl/chat", if it starts with a supported one.</summary>
    public static string? LanguageOf(string? returnUrl)
    {
        if (returnUrl is null || returnUrl.Length < 3)
        {
            return null;
        }

        var end = returnUrl.IndexOf('/', 1);
        return Languages.Find(end < 0 ? returnUrl[1..] : returnUrl[1..end]);
    }
}
