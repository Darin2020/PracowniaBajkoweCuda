using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using BajkoweCuda.Api.IntegrationTests.Database;
using BajkoweCuda.Api.IntegrationTests.Emails;
using BajkoweCuda.Api.IntegrationTests.PublicSite;
using BajkoweCuda.Infrastructure.Emails;
using BajkoweCuda.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace BajkoweCuda.Api.IntegrationTests.Auth;

/// <summary>
/// The application with its own migrated database and a capturing email sender, shared by the tests of a class.
/// Tests use a fresh email address each, so they do not interfere with each other.
/// </summary>
public sealed partial class AuthApp(SqlServerFixture sqlServer) : IAsyncLifetime
{
    public const string Password = "correct horse battery";

    private readonly SiteFactory _site = new();

    /// <summary>Addresses listed in Admin:Emails; each admin test takes its own (<see cref="NewAdminEmail"/>).</summary>
    private readonly string[] _adminEmails = [.. Enumerable.Range(0, 16).Select(_ => $"admin-{Guid.NewGuid():N}@example.test")];
    private int _nextAdmin = -1;

    public CapturingEmailSender Emails { get; } = new();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var connectionString = await sqlServer.CreateDatabaseAsync();
        Factory = _site.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Sql", connectionString);
            builder.UseSetting("Legal:TermsVersion", "test-terms");
            // The tests sign in far more often than a person would; RateLimitTests checks the real limits.
            builder.UseSetting("RateLimiting:AuthPerMinute", "100000");
            builder.UseSetting("RateLimiting:EmailsPerHour", "100000");
            // One entry with a list, as the environment variable Admin__Emails__0 is set in Azure.
            builder.UseSetting("Admin:Emails:0", string.Join(", ", _adminEmails));
            builder.ConfigureTestServices(services => services.AddSingleton<IEmailSender>(Emails).AddFakeProvider());
        });

        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseMigrator>().RunAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await _site.DisposeAsync();
    }

    public static string NewEmail() => $"user-{Guid.NewGuid():N}@example.test";

    /// <summary>An address from Admin:Emails that no other test uses.</summary>
    public string NewAdminEmail() => _adminEmails[Interlocked.Increment(ref _nextAdmin)];

    /// <summary>Signs in a confirmed admin (or any user) and turns 2FA on; returns the authenticator key and the recovery codes.</summary>
    public static async Task<(string SharedKey, IReadOnlyList<string> RecoveryCodes)> EnableTwoFactorAsync(HttpClient client)
    {
        using var setup = await client.GetAsync(new Uri("/api/auth/2fa/setup", UriKind.Relative));
        setup.EnsureSuccessStatusCode();
        var sharedKey = (await setup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sharedKey").GetString()!;

        using var enabled = await PostAsync(client, "/api/auth/2fa/enable", new { code = Totp.Code(sharedKey) });
        enabled.EnsureSuccessStatusCode();
        var codes = (await enabled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recoveryCodes")
            .EnumerateArray().Select(code => code.GetString()!).ToList();
        return (sharedKey, codes);
    }

    /// <summary>A browser-like client that already has its XSRF cookie (from <c>GET /api/auth/me</c>).</summary>
    public async Task<(HttpClient Client, BrowserHandler Browser)> CreateBrowserAsync()
    {
        var browser = new BrowserHandler();
        var client = Factory.CreateDefaultClient(BrowserHandler.BaseAddress, browser);
        using var me = await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative));
        me.EnsureSuccessStatusCode();
        return (client, browser);
    }

    public static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body) =>
        client.PostAsJsonAsync(new Uri(path, UriKind.Relative), body);

    public static Task<HttpResponseMessage> RegisterAsync(HttpClient client, string email, string password = Password, string language = "en") =>
        PostAsync(client, "/api/auth/register", new
        {
            email,
            password,
            displayName = "Anna",
            preferredLanguage = language,
            acceptTerms = true,
        });

    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password = Password) =>
        PostAsync(client, "/api/auth/login", new { email, password });

    /// <summary>Registers, confirms the address through the emailed link and returns the user id.</summary>
    public async Task<Guid> RegisterConfirmedAsync(HttpClient client, string email, string password = Password)
    {
        using var registered = await RegisterAsync(client, email, password);
        registered.EnsureSuccessStatusCode();
        var (userId, code) = ParseLink(await Emails.WaitForAsync(email, "Confirm your email address"));
        using var confirmed = await PostAsync(client, "/api/auth/confirm-email", new { userId, code });
        confirmed.EnsureSuccessStatusCode();
        return userId;
    }

    /// <summary>The <c>userId</c> and <c>code</c> query values of the link in an email.</summary>
    public static (Guid UserId, string Code) ParseLink(RenderedEmail email)
    {
        var match = LinkQuery().Match(email.TextBody);
        match.Success.ShouldBeTrue($"no link with userId and code in: {email.TextBody}");
        return (Guid.Parse(match.Groups["user"].Value), Uri.UnescapeDataString(match.Groups["code"].Value));
    }

    public static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    [GeneratedRegex(@"userId=(?<user>[0-9a-fA-F-]{36})&code=(?<code>[A-Za-z0-9_\-%]+)")]
    private static partial Regex LinkQuery();
}
