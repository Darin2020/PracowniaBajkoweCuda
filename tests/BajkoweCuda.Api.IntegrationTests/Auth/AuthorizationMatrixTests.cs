using BajkoweCuda.Api.IntegrationTests.PublicSite;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BajkoweCuda.Api.IntegrationTests.Auth;

/// <summary>
/// The skeleton of the authorization matrix (docs/PLAN.md §13; CLAUDE.md, Rules): every API endpoint is authorized
/// explicitly, anonymous access is a short reviewed list, and everything under /api/admin uses the Admin policy.
/// A new endpoint that does not fit fails here until it is added on purpose.
/// </summary>
public sealed class AuthorizationMatrixTests(SiteFactory factory) : IClassFixture<SiteFactory>
{
    /// <summary>Endpoints a visitor may call, as "METHOD route".</summary>
    private static readonly string[] Anonymous =
    [
        "GET api/auth/me",
        "POST api/auth/register",
        "POST api/auth/login",
        "POST api/auth/confirm-email",
        "POST api/auth/resend-confirmation",
        "POST api/auth/forgot-password",
        "POST api/auth/reset-password",
        "POST api/auth/2fa/login",
        "POST api/auth/2fa/recovery-login",
    ];

    [Fact]
    public void Every_controller_endpoint_requires_authorization()
    {
        Endpoints().ShouldAllBe(endpoint => endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0);
    }

    [Fact]
    public void Only_the_reviewed_endpoints_allow_anonymous_access()
    {
        var anonymous = Endpoints()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(Describe)
            .Order(StringComparer.Ordinal)
            .ToList();

        anonymous.ShouldBe(Anonymous.Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void Admin_endpoints_use_the_admin_policy_and_never_allow_anonymous_access()
    {
        var admin = Endpoints().Where(endpoint => endpoint.RoutePattern.RawText!.StartsWith("api/admin", StringComparison.OrdinalIgnoreCase)).ToList();

        admin.ShouldNotBeEmpty();
        admin.ShouldAllBe(endpoint =>
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(data => data.Policy == "Admin")
            && endpoint.Metadata.GetMetadata<IAllowAnonymous>() == null);
    }

    private List<RouteEndpoint> Endpoints() =>
    [
        .. factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>() is not null),
    ];

    private static string Describe(RouteEndpoint endpoint) =>
        $"{string.Join(",", endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [])} {endpoint.RoutePattern.RawText}";
}
