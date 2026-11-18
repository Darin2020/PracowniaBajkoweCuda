using System.Net;
using BajkoweCuda.Api.IntegrationTests.Database;
using BajkoweCuda.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BajkoweCuda.Api.IntegrationTests.Auth;

/// <summary>Rate limits of docs/PLAN.md §8.3. Each test runs its own host, so the counters start at zero.</summary>
[Trait("Category", SqlServerFixture.Category)]
public sealed class RateLimitTests(AuthApp app) : IClassFixture<AuthApp>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sign_in_attempts_over_the_limit_get_429_with_retry_after()
    {
        await using var limited = Limited(authPerMinute: 3, emailsPerHour: 100);
        using var client = limited.CreateDefaultClient(BrowserHandler.BaseAddress, new BrowserHandler());
        (await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative), Ct)).Dispose();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var allowed = await AuthApp.LoginAsync(client, AuthApp.NewEmail());
            allowed.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var rejected = await AuthApp.LoginAsync(client, AuthApp.NewEmail());

        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.ShouldNotBeNull();
        (await AuthApp.ProblemCodeAsync(rejected)).ShouldBe("rate_limited");
    }

    [Fact]
    public async Task Requests_that_send_email_are_limited_per_client_per_hour()
    {
        await using var limited = Limited(authPerMinute: 100, emailsPerHour: 2);
        using var client = limited.CreateDefaultClient(BrowserHandler.BaseAddress, new BrowserHandler());
        (await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative), Ct)).Dispose();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var allowed = await AuthApp.PostAsync(client, "/api/auth/forgot-password", new { email = AuthApp.NewEmail() });
            allowed.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        using var rejected = await AuthApp.PostAsync(client, "/api/auth/forgot-password", new { email = AuthApp.NewEmail() });
        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task One_address_gets_at_most_the_hourly_number_of_emails_and_the_answer_does_not_change()
    {
        await using var limited = Limited(authPerMinute: 100, emailsPerHour: 2);
        using var client = limited.CreateDefaultClient(BrowserHandler.BaseAddress, new BrowserHandler());
        (await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative), Ct)).Dispose();
        var email = AuthApp.NewEmail();

        for (var attempt = 0; attempt < 4; attempt++)
        {
            // The first registers; the others hit an existing address, which would email its owner each time.
            using var registered = await AuthApp.RegisterAsync(client, email);
            registered.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        await using var scope = limited.Services.CreateAsyncScope();
        var queued = await scope.ServiceProvider.GetRequiredService<AppDbContext>().EmailOutbox.CountAsync(message => message.ToEmail == email, Ct);
        queued.ShouldBe(2);
    }

    private WebApplicationFactory<Program> Limited(int authPerMinute, int emailsPerHour) =>
        app.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:AuthPerMinute", authPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.UseSetting("RateLimiting:EmailsPerHour", emailsPerHour.ToString(System.Globalization.CultureInfo.InvariantCulture));
        });
}
