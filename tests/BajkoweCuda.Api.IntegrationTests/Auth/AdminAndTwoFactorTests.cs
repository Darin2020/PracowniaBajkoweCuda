using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BajkoweCuda.Api.IntegrationTests.Database;
using BajkoweCuda.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace BajkoweCuda.Api.IntegrationTests.Auth;

/// <summary>Admin:Emails, mandatory 2FA for admins and the admin endpoint (docs/PLAN.md §3.3, §8.1).</summary>
[Trait("Category", SqlServerFixture.Category)]
public sealed class AdminAndTwoFactorTests(AuthApp app) : IClassFixture<AuthApp>
{
    private static readonly Uri AdminAudit = new("/api/admin/audit", UriKind.Relative);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_listed_confirmed_address_becomes_admin_but_cannot_use_admin_endpoints_before_setting_up_2fa()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = app.NewAdminEmail();
        await app.RegisterConfirmedAsync(client, email);

        var me = await LoginAsync(client, email);

        me.GetProperty("roles").EnumerateArray().Select(role => role.GetString()).ShouldBe([Roles.Admin]);
        me.GetProperty("twoFactorEnabled").GetBoolean().ShouldBeFalse();
        using var audit = await client.GetAsync(AdminAudit, Ct);
        audit.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AuthApp.ProblemCodeAsync(audit)).ShouldBe("auth.mfa_required");
    }

    [Fact]
    public async Task Enabling_2fa_upgrades_the_session_and_opens_the_admin_endpoints()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = app.NewAdminEmail();
        var userId = await app.RegisterConfirmedAsync(client, email);
        await LoginAsync(client, email);

        var (_, recoveryCodes) = await AuthApp.EnableTwoFactorAsync(client);

        recoveryCodes.Count.ShouldBe(10);
        recoveryCodes.ShouldAllBe(code => code.Length >= 8);
        using var audit = await client.GetAsync(AdminAudit, Ct);
        audit.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await audit.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var actions = page.GetProperty("items").EnumerateArray()
            .Where(item => item.GetProperty("actorId").ValueKind == JsonValueKind.String && item.GetProperty("actorId").GetGuid() == userId)
            .Select(item => item.GetProperty("action").GetString())
            .ToList();
        actions.ShouldContain("auth.2fa_enabled");
        actions.ShouldContain("auth.login_succeeded");
        actions.ShouldContain("auth.registered");
    }

    [Fact]
    public async Task With_2fa_the_password_alone_does_not_sign_in_and_the_authenticator_code_does()
    {
        var (client, browser) = await app.CreateBrowserAsync();
        var email = app.NewAdminEmail();
        await app.RegisterConfirmedAsync(client, email);
        await LoginAsync(client, email);
        var (sharedKey, _) = await AuthApp.EnableTwoFactorAsync(client);
        (await AuthApp.PostAsync(client, "/api/auth/logout", new { })).Dispose();

        using (var login = await AuthApp.LoginAsync(client, email))
        {
            login.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await login.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("requiresTwoFactor").GetBoolean().ShouldBeTrue();
            login.Headers.GetValues("Set-Cookie").ShouldContain(cookie => cookie.StartsWith("__Host-bc.2fa=", StringComparison.Ordinal));
        }

        browser.Cookie("__Host-bc.session").ShouldBeNullOrEmpty();
        using (var account = await client.GetAsync(new Uri("/api/account", UriKind.Relative), Ct))
        {
            account.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        using (var wrong = await AuthApp.PostAsync(client, "/api/auth/2fa/login", new { code = "000000" }))
        {
            wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await AuthApp.ProblemCodeAsync(wrong)).ShouldBe("auth.invalid_2fa_code");
        }

        using (var second = await AuthApp.PostAsync(client, "/api/auth/2fa/login", new { code = Totp.Code(sharedKey, stepOffset: 1) }))
        {
            second.StatusCode.ShouldBe(HttpStatusCode.OK);
            var me = await second.Content.ReadFromJsonAsync<JsonElement>(Ct);
            me.GetProperty("authenticated").GetBoolean().ShouldBeTrue();
            me.GetProperty("twoFactorEnabled").GetBoolean().ShouldBeTrue();
        }

        using var audit = await client.GetAsync(AdminAudit, Ct);
        audit.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_recovery_code_signs_in_once()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();
        await app.RegisterConfirmedAsync(client, email);
        await LoginAsync(client, email);
        var (_, recoveryCodes) = await AuthApp.EnableTwoFactorAsync(client);
        var code = recoveryCodes[0];

        for (var attempt = 0; attempt < 2; attempt++)
        {
            (await AuthApp.PostAsync(client, "/api/auth/logout", new { })).Dispose();
            (await AuthApp.LoginAsync(client, email)).Dispose();
            using var login = await AuthApp.PostAsync(client, "/api/auth/2fa/recovery-login", new { code });
            login.StatusCode.ShouldBe(attempt == 0 ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task Recovery_codes_can_be_replaced_only_in_a_session_that_passed_the_second_factor()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();
        await app.RegisterConfirmedAsync(client, email);
        await LoginAsync(client, email);

        using (var notEnabled = await AuthApp.PostAsync(client, "/api/auth/2fa/recovery-codes", new { }))
        {
            notEnabled.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await AuthApp.ProblemCodeAsync(notEnabled)).ShouldBe("auth.2fa_not_enabled");
        }

        var (_, oldCodes) = await AuthApp.EnableTwoFactorAsync(client);
        using var replaced = await AuthApp.PostAsync(client, "/api/auth/2fa/recovery-codes", new { });

        replaced.StatusCode.ShouldBe(HttpStatusCode.OK);
        var newCodes = (await replaced.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("recoveryCodes")
            .EnumerateArray().Select(code => code.GetString()!).ToList();
        newCodes.Count.ShouldBe(10);
        newCodes.ShouldNotContain(oldCodes[0]);
    }

    [Fact]
    public async Task Setting_up_2fa_twice_is_refused_once_it_is_enabled()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();
        await app.RegisterConfirmedAsync(client, email);
        await LoginAsync(client, email);
        await AuthApp.EnableTwoFactorAsync(client);

        using var setup = await client.GetAsync(new Uri("/api/auth/2fa/setup", UriKind.Relative), Ct);

        setup.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AuthApp.ProblemCodeAsync(setup)).ShouldBe("auth.2fa_already_enabled");
    }

    [Fact]
    public async Task Customers_get_403_and_visitors_401_from_admin_endpoints()
    {
        var (client, _) = await app.CreateBrowserAsync();
        using (var anonymous = await client.GetAsync(AdminAudit, Ct))
        {
            anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var email = AuthApp.NewEmail();
        await app.RegisterConfirmedAsync(client, email);
        var me = await LoginAsync(client, email);
        me.GetProperty("roles").GetArrayLength().ShouldBe(0);
        await AuthApp.EnableTwoFactorAsync(client);

        using var customer = await client.GetAsync(AdminAudit, Ct);
        customer.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AuthApp.ProblemCodeAsync(customer)).ShouldBe("auth.forbidden");
    }

    [Fact]
    public async Task A_listed_address_that_is_not_confirmed_is_not_an_admin()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = app.NewAdminEmail();
        (await AuthApp.RegisterAsync(client, email)).Dispose();

        var me = await LoginAsync(client, email);

        me.GetProperty("emailConfirmed").GetBoolean().ShouldBeFalse();
        me.GetProperty("roles").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task The_deployment_sync_grants_listed_accounts_and_revokes_everybody_else()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var listedEmail = app.NewAdminEmail();
        var listedId = await app.RegisterConfirmedAsync(client, listedEmail);
        var formerEmail = AuthApp.NewEmail();
        var formerId = await app.RegisterConfirmedAsync(client, formerEmail);

        await using var scope = app.Factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var former = (await users.FindByIdAsync(formerId.ToString()))!;
        // An admin whose address was removed from Admin:Emails (the role exists after earlier tests or is created here).
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roles.RoleExistsAsync(Roles.Admin))
        {
            (await roles.CreateAsync(new IdentityRole<Guid>(Roles.Admin))).Succeeded.ShouldBeTrue();
        }

        (await users.AddToRoleAsync(former, Roles.Admin)).Succeeded.ShouldBeTrue();
        var stampBefore = former.SecurityStamp;

        await scope.ServiceProvider.GetRequiredService<AdminRoleSync>().SyncAllAsync(Ct);

        var listed = (await users.FindByIdAsync(listedId.ToString()))!;
        (await users.IsInRoleAsync(listed, Roles.Admin)).ShouldBeTrue();
        former = (await users.FindByIdAsync(formerId.ToString()))!;
        (await users.IsInRoleAsync(former, Roles.Admin)).ShouldBeFalse();
        former.SecurityStamp.ShouldNotBe(stampBefore, "open sessions must re-read their roles");
    }

    private static async Task<JsonElement> LoginAsync(HttpClient client, string email)
    {
        using var login = await AuthApp.LoginAsync(client, email);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        return await login.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }
}
