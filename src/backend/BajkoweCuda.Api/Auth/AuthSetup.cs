using Azure.Identity;
using BajkoweCuda.Application.Audit;
using BajkoweCuda.Api.Hosting;
using BajkoweCuda.Api.Problems;
using BajkoweCuda.Infrastructure.Identity;
using BajkoweCuda.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    /// <summary>Between the password and the second factor: who is signing in (Identity's two-factor cookie, 5 minutes).</summary>
    public const string TwoFactorCookieName = "__Host-bc.2fa";
    public const string LocalTwoFactorCookieName = "bc.2fa";

    /// <summary>Readable by the Angular app, which sends its value back in <see cref="AntiforgeryHeaderName"/>.</summary>
    public const string RequestTokenCookieName = "XSRF-TOKEN";
    public const string AntiforgeryHeaderName = "X-XSRF-TOKEN";

    public static readonly TimeSpan SlidingExpiration = TimeSpan.FromDays(14);
    public static readonly TimeSpan AbsoluteExpiration = TimeSpan.FromDays(30);
    public static readonly TimeSpan SecurityStampValidationInterval = TimeSpan.FromMinutes(30);

    /// <summary>The policy of every /api/admin endpoint (§6): the Admin role and, outside Development, a second factor.</summary>
    public const string AdminPolicy = "Admin";

    /// <summary>Identity adds <c>amr=mfa</c> to the session when the user signed in with a second factor.</summary>
    public const string AuthenticationMethodClaim = "amr";
    public const string MultiFactorMethod = "mfa";

    private const string SignedInAtKey = "bc.signed_in_at";

    /// <summary>When the session last proved the user again through an external provider (re-authentication, §8.6).</summary>
    private const string ReauthenticatedAtKey = "bc.reauthenticated_at";

    /// <summary>How long an external re-authentication allows a sensitive action such as deleting the account.</summary>
    public static readonly TimeSpan ReauthenticationLifetime = TimeSpan.FromMinutes(10);

    public static IServiceCollection AddAuth(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var cookies = AuthCookies.For(PublicSiteOptions.FromConfiguration(configuration));
        services.AddSingleton(cookies);

        var requireAdminMfa = configuration.GetValue("Features:RequireAdminMfa", defaultValue: true);
        if (!requireAdminMfa && !environment.IsDevelopment())
        {
            throw new InvalidOperationException("Features:RequireAdminMfa can be turned off in Development only (docs/PLAN.md §14).");
        }

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
                ApiProblems.WriteAsync(context.HttpContext, IsAdminWithoutMfa(context.HttpContext.User) ? AuthErrors.MfaRequired : AuthErrors.Forbidden);
            options.Events.OnSigningIn = context =>
            {
                context.Properties.Items.TryAdd(SignedInAtKey, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
                return Task.CompletedTask;
            };
            options.Events.OnValidatePrincipal = ValidatePrincipalAsync;
        });

        services.Configure<CookieAuthenticationOptions>(IdentityConstants.TwoFactorUserIdScheme, options =>
        {
            options.Cookie.Name = cookies.TwoFactor;
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = cookies.SecurePolicy;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });

        // Controllers need a signed-in user unless they say [AllowAnonymous]: MapControllers().RequireAuthorization()
        // in Program.cs (CLAUDE.md, Rules). A fallback policy would also turn every 404 into a 401.
        services.AddAuthorizationBuilder()
            .AddPolicy(AdminPolicy, policy =>
            {
                policy.RequireAuthenticatedUser().RequireRole(Roles.Admin);
                if (requireAdminMfa)
                {
                    policy.RequireClaim(AuthenticationMethodClaim, MultiFactorMethod);
                }
            });

        services.AddAntiforgery(options =>
        {
            options.HeaderName = AntiforgeryHeaderName;
            options.Cookie.Name = cookies.Antiforgery;
            options.Cookie.SecurePolicy = cookies.SecurePolicy;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });

        services.AddExternalProviders(configuration, cookies);
        services.AddDataProtectionKeys(configuration);
        services.AddScoped<AccountLinks>();
        services.AddHttpContextAccessor();
        services.Replace(ServiceDescriptor.Singleton<IClientInfo, HttpClientInfo>());

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

    /// <summary>Re-issues the session cookie, keeping its sign-in time and second factor, and records a re-authentication.</summary>
    public static async Task MarkReauthenticatedAsync(this HttpContext context, SignInManager<ApplicationUser> signIn, ApplicationUser user, DateTimeOffset now)
    {
        var current = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var properties = current.Properties ?? new AuthenticationProperties { IsPersistent = true };
        properties.Items[ReauthenticatedAtKey] = now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var methods = current.Principal?.FindAll(AuthenticationMethodClaim).ToList() ?? [];
        await signIn.SignInWithClaimsAsync(user, properties, methods);
    }

    /// <summary>Whether this session re-authenticated through an external provider within <see cref="ReauthenticationLifetime"/>.</summary>
    public static async Task<bool> IsFreshlyReauthenticatedAsync(this HttpContext context, DateTimeOffset now)
    {
        var current = await context.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        return current.Properties?.Items.TryGetValue(ReauthenticatedAtKey, out var value) == true
            && long.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
            && now - DateTimeOffset.FromUnixTimeSeconds(seconds) <= ReauthenticationLifetime;
    }

    public static bool HasMultiFactor(this System.Security.Claims.ClaimsPrincipal user) =>
        user.HasClaim(AuthenticationMethodClaim, MultiFactorMethod);

    private static bool IsAdminWithoutMfa(System.Security.Claims.ClaimsPrincipal user) =>
        user.IsInRole(Roles.Admin) && !user.HasMultiFactor();

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
internal sealed record AuthCookies(string Session, string Antiforgery, string TwoFactor, string External, CookieSecurePolicy SecurePolicy)
{
    public static AuthCookies For(PublicSiteOptions site) =>
        site.Origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? new(AuthSetup.SessionCookieName, AuthSetup.AntiforgeryCookieName, AuthSetup.TwoFactorCookieName, ExternalProviders.ExternalCookieName, CookieSecurePolicy.Always)
            : new(AuthSetup.LocalSessionCookieName, AuthSetup.LocalAntiforgeryCookieName, AuthSetup.LocalTwoFactorCookieName, ExternalProviders.LocalExternalCookieName, CookieSecurePolicy.SameAsRequest);
}

/// <summary>The client address for audit entries (behind the ingress it comes from X-Forwarded-For).</summary>
internal sealed class HttpClientInfo(IHttpContextAccessor accessor) : IClientInfo
{
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
}
