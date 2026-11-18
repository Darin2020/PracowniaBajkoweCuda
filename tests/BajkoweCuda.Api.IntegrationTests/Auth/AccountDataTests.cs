using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BajkoweCuda.Api.IntegrationTests.Database;
using BajkoweCuda.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BajkoweCuda.Api.IntegrationTests.Auth;

/// <summary>Data export, password change and account deletion (docs/PLAN.md §3.2, §8.1, §8.6).</summary>
[Trait("Category", SqlServerFixture.Category)]
public sealed class AccountDataTests(AuthApp app) : IClassFixture<AuthApp>
{
    private const string NewPassword = "a much longer new passphrase";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Export_downloads_the_users_data_as_json()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();
        var userId = await app.RegisterConfirmedAsync(client, email);
        (await AuthApp.LoginAsync(client, email)).Dispose();

        using var response = await client.GetAsync(new Uri("/api/account/export", UriKind.Relative), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
        response.Content.Headers.ContentDisposition?.DispositionType.ShouldBe("attachment");
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();
        var export = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var profile = export.GetProperty("profile");
        profile.GetProperty("id").GetGuid().ShouldBe(userId);
        profile.GetProperty("email").GetString().ShouldBe(email);
        profile.GetProperty("displayName").GetString().ShouldBe("Anna");
        profile.GetProperty("termsVersion").GetString().ShouldBe("test-terms");
        export.GetProperty("emails").EnumerateArray().ShouldContain(item => item.GetProperty("template").GetString() == "ConfirmEmail");
        export.GetProperty("securityEvents").EnumerateArray().Select(item => item.GetProperty("action").GetString())
            .ShouldContain("auth.login_succeeded");
    }

    [Fact]
    public async Task Changing_the_password_needs_the_current_one_keeps_this_session_and_sends_a_notice()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();
        await app.RegisterConfirmedAsync(client, email);
        (await AuthApp.LoginAsync(client, email)).Dispose();

        using (var wrong = await AuthApp.PostAsync(client, "/api/account/password", new { currentPassword = "not the password", newPassword = NewPassword }))
        {
            wrong.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await AuthApp.ProblemCodeAsync(wrong)).ShouldBe("auth.reauthentication_failed");
        }

        using (var weak = await AuthApp.PostAsync(client, "/api/account/password", new { currentPassword = AuthApp.Password, newPassword = "short" }))
        {
            weak.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await AuthApp.ProblemCodeAsync(weak)).ShouldBe("auth.password_rejected");
        }

        using (var changed = await AuthApp.PostAsync(client, "/api/account/password", new { currentPassword = AuthApp.Password, newPassword = NewPassword }))
        {
            changed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var account = await client.GetAsync(new Uri("/api/account", UriKind.Relative), Ct))
        {
            account.StatusCode.ShouldBe(HttpStatusCode.OK, "the session that changed the password stays signed in");
        }

        await app.Emails.WaitForAsync(email, "Your password was changed");
        var (other, _) = await app.CreateBrowserAsync();
        using (var oldLogin = await AuthApp.LoginAsync(other, email))
        {
            oldLogin.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using var newLogin = await AuthApp.LoginAsync(other, email, NewPassword);
        newLogin.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Deleting_needs_the_password()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();
        await app.RegisterConfirmedAsync(client, email);
        (await AuthApp.LoginAsync(client, email)).Dispose();

        using var refused = await AuthApp.PostAsync(client, "/api/account/delete", new { password = "not the password" });

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AuthApp.ProblemCodeAsync(refused)).ShouldBe("auth.reauthentication_failed");
        using var account = await client.GetAsync(new Uri("/api/account", UriKind.Relative), Ct);
        account.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Deleting_erases_the_personal_data_signs_out_and_confirms_by_email()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();
        var userId = await app.RegisterConfirmedAsync(client, email);
        (await AuthApp.LoginAsync(client, email)).Dispose();

        using (var deleted = await AuthApp.PostAsync(client, "/api/account/delete", new { password = AuthApp.Password }))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var account = await client.GetAsync(new Uri("/api/account", UriKind.Relative), Ct))
        {
            account.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using (var login = await AuthApp.LoginAsync(client, email))
        {
            login.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        await app.Emails.WaitForAsync(email, "Your account was deleted");

        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.AsNoTracking().SingleAsync(row => row.Id == userId, Ct);
            user.IsDeleted.ShouldBeTrue();
            user.DeletedAt.ShouldNotBeNull();
            user.Email.ShouldBeNull();
            user.NormalizedEmail.ShouldBeNull();
            user.UserName.ShouldBe($"deleted-{userId:N}");
            user.DisplayName.ShouldBeEmpty();
            user.PasswordHash.ShouldBeNull();
            (await db.EmailOutbox.AnyAsync(row => row.ToUserId == userId, Ct)).ShouldBeFalse();
            (await db.AuditLog.AnyAsync(row => row.ActorId == userId && row.IpAddress != null, Ct)).ShouldBeFalse();
            (await db.AuditLog.AnyAsync(row => row.ActorId == userId && row.Action == "account.deleted", Ct)).ShouldBeTrue();
        }

        // The address is free again: registering sends a new confirmation for a new account.
        using (var again = await AuthApp.RegisterAsync(client, email))
        {
            again.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
        while (app.Emails.Count(email, "Confirm your email address") < 2 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(50, Ct);
        }

        var (newId, _) = AuthApp.ParseLink(await app.Emails.WaitForAsync(email, "Confirm your email address"));
        newId.ShouldNotBe(userId);
    }
}
