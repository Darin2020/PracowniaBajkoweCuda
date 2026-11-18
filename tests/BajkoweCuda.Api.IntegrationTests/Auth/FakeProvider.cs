using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BajkoweCuda.Api.IntegrationTests.Auth;

/// <summary>
/// Stands in for Google in tests. It behaves like an OAuth handler from the application's side: the challenge sets
/// the correlation cookie and redirects to the provider with a protected <c>state</c>; the callback on
/// <c>/signin-google</c> checks both and stores the person in the external cookie. The test plays the provider by
/// calling the callback with the person's id, email and whether the email is verified.
/// </summary>
public sealed class FakeProviderHandler(
    IOptionsMonitor<FakeProviderOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IDataProtectionProvider dataProtection) : RemoteAuthenticationHandler<FakeProviderOptions>(options, logger, encoder)
{
    public const string SchemeName = "Google";
    public const string AuthorizeUrl = "https://provider.test/authorize";

    private PropertiesDataFormat State => new(dataProtection.CreateProtector(nameof(FakeProviderHandler), Scheme.Name));

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        GenerateCorrelationId(properties);
        Response.Redirect($"{AuthorizeUrl}?state={Uri.EscapeDataString(State.Protect(properties))}");
        return Task.CompletedTask;
    }

    protected override Task<HandleRequestResult> HandleRemoteAuthenticateAsync()
    {
        var properties = State.Unprotect(Request.Query["state"]);
        if (properties is null)
        {
            return Task.FromResult(HandleRequestResult.Fail("Invalid state."));
        }

        if (!ValidateCorrelationId(properties))
        {
            return Task.FromResult(HandleRequestResult.Fail("Correlation failed.", properties));
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Request.Query["sub"].ToString()),
            new(ClaimTypes.Name, "Anna from the provider"),
        };
        if (Request.Query["email"].ToString() is { Length: > 0 } email)
        {
            claims.Add(new(ClaimTypes.Email, email));
        }

        if (Request.Query["verified"] == "true")
        {
            claims.Add(new("bc:email_verified", "true"));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return Task.FromResult(HandleRequestResult.Success(new AuthenticationTicket(principal, properties, Scheme.Name)));
    }
}

public sealed class FakeProviderOptions : RemoteAuthenticationOptions
{
    public FakeProviderOptions()
    {
        CallbackPath = "/signin-google";
        SignInScheme = IdentityConstants.ExternalScheme;
        CorrelationCookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
    }
}

public static class FakeProviderExtensions
{
    public static IServiceCollection AddFakeProvider(this IServiceCollection services)
    {
        services.AddAuthentication().AddRemoteScheme<FakeProviderOptions, FakeProviderHandler>(FakeProviderHandler.SchemeName, "Google", _ => { });
        return services;
    }
}
