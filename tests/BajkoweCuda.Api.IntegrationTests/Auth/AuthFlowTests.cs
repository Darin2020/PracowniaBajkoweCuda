using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BajkoweCuda.Api.IntegrationTests.Database;

namespace BajkoweCuda.Api.IntegrationTests.Auth;

[Trait("Category", SqlServerFixture.Category)]
public sealed class AuthFlowTests(AuthApp app) : IClassFixture<AuthApp>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Register_confirm_sign_in_and_sign_out()
    {
        var (client, browser) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();

        using (var registered = await AuthApp.RegisterAsync(client, email))
        {
            registered.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        var confirmation = await app.Emails.WaitForAsync(email, "Confirm your email address");
        confirmation.HtmlBody.ShouldContain("https://toys.test/en/confirm-email?userId=");
        var (userId, code) = AuthApp.ParseLink(confirmation);

        using (var confirmed = await AuthApp.PostAsync(client, "/api/auth/confirm-email", new { userId, code }))
        {
            confirmed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using (var login = await AuthApp.LoginAsync(client, email))
        {
            login.StatusCode.ShouldBe(HttpStatusCode.OK);
            var me = await login.Content.ReadFromJsonAsync<JsonElement>(Ct);
            me.GetProperty("authenticated").GetBoolean().ShouldBeTrue();
            me.GetProperty("id").GetGuid().ShouldBe(userId);
            me.GetProperty("emailConfirmed").GetBoolean().ShouldBeTrue();
            me.GetProperty("displayName").GetString().ShouldBe("Anna");
        }

        browser.Cookie("__Host-bc.session").ShouldNotBeNull();
        using (var account = await client.GetAsync(new Uri("/api/account", UriKind.Relative), Ct))
        {
            account.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await account.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("email").GetString().ShouldBe(email);
        }

        using (var logout = await AuthApp.PostAsync(client, "/api/auth/logout", new { }))
        {
            logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        using var afterLogout = await client.GetAsync(new Uri("/api/account", UriKind.Relative), Ct);
        afterLogout.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await AuthApp.ProblemCodeAsync(afterLogout)).ShouldBe("auth.not_authenticated");
    }

    [Fact]
    public async Task The_session_cookie_is_host_only_secure_http_only_and_lax()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();
        await app.RegisterConfirmedAsync(client, email);

        using var login = await AuthApp.LoginAsync(client, email);

        var session = login.Headers.GetValues("Set-Cookie").Single(cookie => cookie.StartsWith("__Host-bc.session=", StringComparison.Ordinal));
        session.ShouldContain("secure", Case.Insensitive);
        session.ShouldContain("httponly", Case.Insensitive);
        session.ShouldContain("samesite=lax", Case.Insensitive);
        session.ShouldContain("path=/", Case.Insensitive);
        session.ShouldNotContain("domain=", Case.Insensitive);
    }

    [Fact]
    public async Task Registering_an_existing_address_answers_the_same_and_emails_the_owner()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();
        await app.RegisterConfirmedAsync(client, email);

        using var again = await AuthApp.RegisterAsync(client, email, "another long password");

        again.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var notice = await app.Emails.WaitForAsync(email, "You already have an account");
        notice.TextBody.ShouldContain("https://toys.test/en/forgot-password");
        using var oldPasswordStillWorks = await AuthApp.LoginAsync(client, email);
        oldPasswordStillWorks.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_short_password_is_rejected_with_identity_codes_for_new_and_existing_addresses()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var existing = AuthApp.NewEmail();
        await app.RegisterConfirmedAsync(client, existing);

        foreach (var email in new[] { AuthApp.NewEmail(), existing })
        {
            using var response = await AuthApp.RegisterAsync(client, email, "short");

            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
            problem.GetProperty("code").GetString().ShouldBe("auth.password_rejected");
            problem.GetProperty("errors").GetProperty("password")[0].GetString().ShouldBe("PasswordTooShort");
        }
    }

    [Fact]
    public async Task Wrong_password_and_unknown_address_get_the_same_answer()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();
        await app.RegisterConfirmedAsync(client, email);

        using var wrongPassword = await AuthApp.LoginAsync(client, email, "not the right password");
        using var unknown = await AuthApp.LoginAsync(client, AuthApp.NewEmail());

        foreach (var response in new[] { wrongPassword, unknown })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await AuthApp.ProblemCodeAsync(response)).ShouldBe("auth.invalid_credentials");
        }
    }

    [Fact]
    public async Task Five_failed_sign_ins_lock_the_account()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();
        await app.RegisterConfirmedAsync(client, email);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failed = await AuthApp.LoginAsync(client, email, "not the right password");
        }

        using var locked = await AuthApp.LoginAsync(client, email);
        locked.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await AuthApp.ProblemCodeAsync(locked)).ShouldBe("auth.invalid_credentials");
    }

    [Fact]
    public async Task Forgot_and_reset_password()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();
        await app.RegisterConfirmedAsync(client, email);

        using (var forgot = await AuthApp.PostAsync(client, "/api/auth/forgot-password", new { email }))
        {
            forgot.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        }

        var (userId, code) = AuthApp.ParseLink(await app.Emails.WaitForAsync(email, "Reset your password"));
        const string newPassword = "a brand new long password";
        using (var reset = await AuthApp.PostAsync(client, "/api/auth/reset-password", new { userId, code, password = newPassword }))
        {
            reset.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await app.Emails.WaitForAsync(email, "Your password was changed");
        using (var reused = await AuthApp.PostAsync(client, "/api/auth/reset-password", new { userId, code, password = "yet another long password" }))
        {
            reused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await AuthApp.ProblemCodeAsync(reused)).ShouldBe("auth.invalid_token");
        }

        using var oldPassword = await AuthApp.LoginAsync(client, email);
        oldPassword.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using var newOne = await AuthApp.LoginAsync(client, email, newPassword);
        newOne.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Forgot_password_for_an_unknown_address_answers_the_same_and_sends_nothing()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();

        using var forgot = await AuthApp.PostAsync(client, "/api/auth/forgot-password", new { email });

        forgot.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        await Task.Delay(500, Ct);
        app.Emails.Sent.ShouldNotContain(sent => sent.To == email);
    }

    [Fact]
    public async Task A_wrong_confirmation_code_is_rejected()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();
        using (await AuthApp.RegisterAsync(client, email))
        {
        }

        var (userId, _) = AuthApp.ParseLink(await app.Emails.WaitForAsync(email, "Confirm your email address"));

        using var response = await AuthApp.PostAsync(client, "/api/auth/confirm-email", new { userId, code = "bm90LWEtdG9rZW4" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuthApp.ProblemCodeAsync(response)).ShouldBe("auth.invalid_token");
    }

    [Fact]
    public async Task The_confirmation_email_is_in_the_chosen_language()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();

        using (await AuthApp.RegisterAsync(client, email, language: "pl"))
        {
        }

        var confirmation = await app.Emails.WaitForAsync(email, "Potwierdź swój adres e-mail");
        confirmation.HtmlBody.ShouldContain("lang=\"pl\"");
        confirmation.HtmlBody.ShouldContain("https://toys.test/pl/confirm-email?userId=");
    }

    [Fact]
    public async Task Unsafe_requests_without_the_antiforgery_header_are_rejected()
    {
        var (client, browser) = await app.CreateBrowserAsync();
        browser.SendAntiforgeryHeader = false;

        using var response = await AuthApp.LoginAsync(client, AuthApp.NewEmail());

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuthApp.ProblemCodeAsync(response)).ShouldBe("antiforgery.invalid");
    }

    [Fact]
    public async Task Account_settings_can_be_changed_and_are_validated()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();
        await app.RegisterConfirmedAsync(client, email);
        using (await AuthApp.LoginAsync(client, email))
        {
        }

        using (var updated = await client.PatchAsJsonAsync(new Uri("/api/account", UriKind.Relative), new { displayName = "  Anna N.  ", preferredLanguage = "de" }, Ct))
        {
            updated.StatusCode.ShouldBe(HttpStatusCode.OK);
            var account = await updated.Content.ReadFromJsonAsync<JsonElement>(Ct);
            account.GetProperty("displayName").GetString().ShouldBe("Anna N.");
            account.GetProperty("preferredLanguage").GetString().ShouldBe("de");
        }

        using var invalid = await client.PatchAsJsonAsync(new Uri("/api/account", UriKind.Relative), new { preferredLanguage = "ru" }, Ct);
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuthApp.ProblemCodeAsync(invalid)).ShouldBe("validation_failed");
    }

    [Fact]
    public async Task Registration_requires_accepting_the_terms()
    {
        var (client, _) = await app.CreateBrowserAsync();

        using var response = await AuthApp.PostAsync(client, "/api/auth/register", new
        {
            email = AuthApp.NewEmail(),
            password = AuthApp.Password,
            displayName = "Anna",
            preferredLanguage = "en",
            acceptTerms = false,
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuthApp.ProblemCodeAsync(response)).ShouldBe("validation_failed");
    }
}
