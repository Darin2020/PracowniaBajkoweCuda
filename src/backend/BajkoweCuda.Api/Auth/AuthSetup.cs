using Azure.Identity;
using BajkoweCuda.Api.Hosting;
using BajkoweCuda.Api.Problems;
using BajkoweCuda.Infrastructure.Identity;
using BajkoweCuda.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace BajkoweCuda.Api.Auth;

/// <summary>
/// BFF authentication (docs/PLAN.md §2, §8.1): ASP.NET Core Identity signs users in and keeps the session in an
/// HttpOnly cookie; the Angular app never sees a token. Unsafe requests carry an antiforgery header.
/// </summary>
internal static class AuthSetup
{
    public const string SessionCookieName = "__Host-bc.session";
    public const string AntiforgeryCookieName = "__Host-bc.af";

    /// <summary>
    /// Names used when the public origin is plain http, which only happens locally (docker compose, ng serve):
    /// a __Host- cookie must be Secure, and Secure cookies need HTTPS.
    /// </summary>
    public const string LocalSessionCookieName = "bc.session";
    public const string LocalAntiforgeryCookieName = "bc.af";

    /// <summary>Readable by the Angular app, which sends its value back in <see cref="AntiforgeryHeaderName"/>.</summary>
    public const string RequestTokenCookieName = "XSRF-TOKEN";
    public const string AntiforgeryHeaderName = "X-XSRF-TOKEN";

    public static readonly TimeSpan SlidingExpiration = TimeSpan.FromDays(14);
    public static readonly TimeSpan AbsoluteExpiration = TimeSpan.FromDays(30);
    public static readonly TimeSpan SecurityStampValidationInterval = TimeSpan.FromMinutes(30);

    private const string SignedInAtKey = "bc.signed_in_at";

    public static IServiceCollection AddAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var cookies = AuthCookies.For(PublicSiteOptions.FromConfiguration(configuration));
        services.AddSingleton(cookies);

        services
            .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
            {
                options.User.RequireUniqueEmail = true;

                // At least 12 characters, no composition rules (§8.1).
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;

                // Signing in does not need a confirmed email; the chat does (§3.2).
                options.SignIn.RequireConfirmedEmail = false;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();

        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = SecurityStampValidationInterval);

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = cookies.Session;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = cookies.SecurePolicy;
            // Lax, not Strict: after an OAuth redirect chain a Strict cookie is not sent on the first navigation.
            // Cross-site request forgery is covered by the antiforgery header.
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = SlidingExpiration;
            options.SlidingExpiration = true;

            options.Events.OnRedirectToLogin = context =>
                ApiProblems.WriteAsync(context.HttpContext, AuthErrors.NotAuthenticated);
            options.Events.OnRedirectToAccessDenied = context =>
                ApiProblems.WriteAsync(context.HttpContext, AuthErrors.Forbidden);
            options.Events.OnSigningIn = context =>
            {
                context.Properties.Items.TryAdd(SignedInAtKey, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
                return Task.CompletedTask;
            };
            options.Events.OnValidatePrincipal = ValidatePrincipalAsync;
        });

        // Controllers need a signed-in user unless they say [AllowAnonymous]: MapControllers().RequireAuthorization()
        // in Program.cs (CLAUDE.md, Rules). A fallback policy would also turn every 404 into a 401.
        services.AddAuthorization();

        services.AddAntiforgery(options =>
        {
            options.HeaderName = AntiforgeryHeaderName;
            options.Cookie.Name = cookies.Antiforgery;
            options.Cookie.SecurePolicy = cookies.SecurePolicy;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });

        services.AddDataProtectionKeys(configuration);
        services.AddScoped<AccountLinks>();

        return services;
    }

    /// <summary>
    /// Writes the request token cookie for the current user. Called by <c>GET /api/auth/me</c> and whenever the user
    /// changes (sign-in, sign-out), because a token is bound to the user it was issued for.
    /// </summary>
    public static void IssueRequestToken(this HttpContext context)
    {
        var tokens = context.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
        var cookies = context.RequestServices.GetRequiredService<AuthCookies>();
        context.Response.Cookies.Append(RequestTokenCookieName, tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false,
            Secure = cookies.SecurePolicy == CookieSecurePolicy.Always,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        });
    }

    /// <summary>
    /// The session slides for 14 days but ends 30 days after sign-in at the latest; the security stamp is checked
    /// every 30 minutes, so a password reset signs out the other sessions (§8.1).
    /// </summary>
    private static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        if (context.Properties.Items.TryGetValue(SignedInAtKey, out var value)
            && long.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
            && DateTimeOffset.FromUnixTimeSeconds(seconds) + AbsoluteExpiration < DateTimeOffset.UtcNow)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return;
        }

        await SecurityStampValidator.ValidatePrincipalAsync(context);
    }

    /// <summary>
    /// Session cookies, antiforgery tokens and email links are protected with Data Protection keys. In Azure the key
    /// ring lives in Blob Storage, wrapped by a Key Vault key, so sessions survive restarts and new revisions.
    /// Locally (no settings) the default key store is used.
    /// </summary>
    private static void AddDataProtectionKeys(this IServiceCollection services, IConfiguration configuration)
    {
        var builder = services.AddDataProtection().SetApplicationName("bajkowecuda");

        var blobUri = configuration["DataProtection:BlobUri"];
        var keyUri = configuration["DataProtection:KeyUri"];
        if (string.IsNullOrWhiteSpace(blobUri) || string.IsNullOrWhiteSpace(keyUri))
        {
            return;
        }

        // DefaultAzureCredential picks the managed identity selected by AZURE_CLIENT_ID.
        var credential = new DefaultAzureCredential();
        builder
            .PersistKeysToAzureBlobStorage(new Uri(blobUri), credential)
            .ProtectKeysWithAzureKeyVault(new Uri(keyUri), credential);
    }
}

/// <summary>
/// Cookie names and the Secure policy of this environment. Every deployed environment has an https origin and gets
/// __Host- cookies that are always Secure; a plain-http origin (local development only) gets unprefixed cookies
/// that follow the request scheme.
/// </summary>
internal sealed record AuthCookies(string Session, string Antiforgery, CookieSecurePolicy SecurePolicy)
{
    public static AuthCookies For(PublicSiteOptions site) =>
        site.Origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? new(AuthSetup.SessionCookieName, AuthSetup.AntiforgeryCookieName, CookieSecurePolicy.Always)
            : new(AuthSetup.LocalSessionCookieName, AuthSetup.LocalAntiforgeryCookieName, CookieSecurePolicy.SameAsRequest);
}
