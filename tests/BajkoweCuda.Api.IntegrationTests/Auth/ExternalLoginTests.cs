using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BajkoweCuda.Api.IntegrationTests.Database;

namespace BajkoweCuda.Api.IntegrationTests.Auth;

/// <summary>External sign-in, account linking and complete-profile (docs/PLAN.md §3.2), against <see cref="FakeProviderHandler"/>.</summary>
[Trait("Category", SqlServerFixture.Category)]
public sealed class ExternalLoginTests(AuthApp app) : IClassFixture<AuthApp>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Configured_providers_are_listed_and_unknown_ones_refused()
    {
        var (client, _) = await app.CreateBrowserAsync();

        (await client.GetFromJsonAsync<string[]>(new Uri("/api/auth/providers", UriKind.Relative), Ct)).ShouldBe(["google"]);
        using var unknown = await client.GetAsync(new Uri("/api/auth/external/facebook", UriKind.Relative), Ct);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AuthApp.ProblemCodeAsync(unknown)).ShouldBe("auth.provider_unavailable");
    }

    [Fact]
    public async Task A_new_person_completes_the_profile_and_later_signs_in_with_the_provider_alone()
    {
        var (client, browser) = await app.CreateBrowserAsync();
        var sub = NewSub();
        var email = AuthApp.NewEmail();

        var landing = await SignInWithProviderAsync(client, sub, email, verified: true, "?returnUrl=%2Fpl%2Fchat");
        landing.ShouldBe("/pl/complete-profile?returnUrl=%2Fpl%2Fchat");

        var pending = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/auth/complete-profile", UriKind.Relative), Ct);
        pending.GetProperty("provider").GetString().ShouldBe("google");
        pending.GetProperty("email").GetString().ShouldBe(email);
        pending.GetProperty("emailVerified").GetBoolean().ShouldBeTrue();

        using (var missingTerms = await AuthApp.PostAsync(client, "/api/auth/complete-profile", new { displayName = "Anna", preferredLanguage = "pl", acceptTerms = false }))
        {
            missingTerms.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using (var completed = await AuthApp.PostAsync(client, "/api/auth/complete-profile", new { displayName = "Anna", preferredLanguage = "pl", acceptTerms = true }))
        {
            completed.StatusCode.ShouldBe(HttpStatusCode.OK);
            var me = await completed.Content.ReadFromJsonAsync<JsonElement>(Ct);
            me.GetProperty("email").GetString().ShouldBe(email);
            me.GetProperty("emailConfirmed").GetBoolean().ShouldBeTrue("the provider verified the address");
        }

        browser.Cookie("__Host-bc.ext").ShouldBeNullOrEmpty();
        (await AuthApp.PostAsync(client, "/api/auth/logout", new { })).Dispose();

        (await SignInWithProviderAsync(client, sub, email, verified: true, "?returnUrl=%2Fpl%2Fchat")).ShouldBe("/pl/chat");
        var again = await MeAsync(client);
        again.GetProperty("email").GetString().ShouldBe(email);
    }

    [Fact]
    public async Task Without_an_email_from_the_provider_the_person_enters_one_and_confirms_it()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();

        (await SignInWithProviderAsync(client, NewSub(), email: null, verified: false)).ShouldBe("/en/complete-profile");

        using (var noEmail = await AuthApp.PostAsync(client, "/api/auth/complete-profile", new { displayName = "Anna", preferredLanguage = "en", acceptTerms = true }))
        {
            noEmail.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        using var completed = await AuthApp.PostAsync(client, "/api/auth/complete-profile", new { email, displayName = "Anna", preferredLanguage = "en", acceptTerms = true });
        completed.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await completed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("emailConfirmed").GetBoolean().ShouldBeFalse();
        await app.Emails.WaitForAsync(email, "Confirm your email address");
    }

    [Fact]
    public async Task A_verified_address_of_a_confirmed_account_links_automatically()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();
        var userId = await app.RegisterConfirmedAsync(client, email);

        (await SignInWithProviderAsync(client, NewSub(), email, verified: true)).ShouldBe("/en/");

        (await MeAsync(client)).GetProperty("id").GetGuid().ShouldBe(userId);
    }

    [Fact]
    public async Task An_unverified_address_of_an_existing_account_is_not_linked()
    {
        var (client, browser) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();
        await app.RegisterConfirmedAsync(client, email);

        (await SignInWithProviderAsync(client, NewSub(), email, verified: false)).ShouldBe("/en/login?error=account_exists");

        browser.Cookie("__Host-bc.session").ShouldBeNullOrEmpty();
        (await MeAsync(client)).GetProperty("authenticated").GetBoolean().ShouldBeFalse();
    }

    [Theory]
    [InlineData("https%3A%2F%2Fevil.test%2F")]
    [InlineData("%2F%2Fevil.test")]
    [InlineData("%2F%5Cevil.test")]
    public async Task Foreign_return_urls_are_ignored(string returnUrl)
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();
        await app.RegisterConfirmedAsync(client, email);

        (await SignInWithProviderAsync(client, NewSub(), email, verified: true, $"?returnUrl={returnUrl}")).ShouldBe("/en/");
    }

    [Fact]
    public async Task A_signed_in_user_links_a_provider_that_no_other_account_uses()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();
        await app.RegisterConfirmedAsync(client, email);
        (await AuthApp.LoginAsync(client, email)).Dispose();
        var sub = NewSub();

        (await SignInWithProviderAsync(client, sub, "someone-else@example.test", verified: false, "?mode=link")).ShouldBe("/en/account?linked=google");

        var logins = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/account/logins", UriKind.Relative), Ct);
        logins.GetProperty("hasPassword").GetBoolean().ShouldBeTrue();
        logins.GetProperty("logins").EnumerateArray().Select(login => login.GetProperty("provider").GetString()).ShouldBe(["google"]);
        logins.GetProperty("available").GetArrayLength().ShouldBe(0);

        var (other, _) = await app.CreateBrowserAsync();
        var otherEmail = AuthApp.NewEmail();
        await app.RegisterConfirmedAsync(other, otherEmail);
        (await AuthApp.LoginAsync(other, otherEmail)).Dispose();
        (await SignInWithProviderAsync(other, sub, null, verified: false, "?mode=link")).ShouldBe("/en/account?error=login_in_use");
    }

    [Fact]
    public async Task The_last_way_to_sign_in_cannot_be_removed_but_others_can()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();
        await app.RegisterConfirmedAsync(client, email);
        (await AuthApp.LoginAsync(client, email)).Dispose();
        _ = await SignInWithProviderAsync(client, NewSub(), null, verified: false, "?mode=link");

        using (var removed = await client.DeleteAsync(new Uri("/api/account/logins/google", UriKind.Relative), Ct))
        {
            removed.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        var (externalOnly, _) = await app.CreateBrowserAsync();
        await CreateExternalOnlyAccountAsync(externalOnly, NewSub(), AuthApp.NewEmail());
        using var last = await externalOnly.DeleteAsync(new Uri("/api/account/logins/google", UriKind.Relative), Ct);
        last.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AuthApp.ProblemCodeAsync(last)).ShouldBe("auth.last_login");
    }

    [Fact]
    public async Task An_account_without_a_password_is_deleted_after_re_authenticating_with_the_provider()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var sub = NewSub();
        var email = AuthApp.NewEmail();
        await CreateExternalOnlyAccountAsync(client, sub, email);

        using (var refused = await AuthApp.PostAsync(client, "/api/account/delete", new { }))
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
            (await AuthApp.ProblemCodeAsync(refused)).ShouldBe("auth.reauthentication_failed");
        }

        (await SignInWithProviderAsync(client, NewSub(), null, verified: false, "?mode=reauth")).ShouldBe("/en/account?error=reauthentication_failed");
        (await SignInWithProviderAsync(client, sub, email, verified: true, "?mode=reauth")).ShouldBe("/en/account?reauthenticated=1");

        using var deleted = await AuthApp.PostAsync(client, "/api/account/delete", new { });
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await MeAsync(client)).GetProperty("authenticated").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task With_2fa_the_provider_sign_in_continues_on_the_2fa_page()
    {
        var (client, browser) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();
        await app.RegisterConfirmedAsync(client, email);
        (await AuthApp.LoginAsync(client, email)).Dispose();
        await AuthApp.EnableTwoFactorAsync(client);
        var sub = NewSub();
        _ = await SignInWithProviderAsync(client, sub, null, verified: false, "?mode=link");
        (await AuthApp.PostAsync(client, "/api/auth/logout", new { })).Dispose();

        (await SignInWithProviderAsync(client, sub, email, verified: true)).ShouldBe("/en/login/2fa");

        browser.Cookie("__Host-bc.session").ShouldBeNullOrEmpty();
        browser.Cookie("__Host-bc.2fa").ShouldNotBeNullOrEmpty();
    }

    /// <summary>Runs the whole round trip as a browser would and returns where the application finally sends it.</summary>
    private static async Task<string> SignInWithProviderAsync(HttpClient client, string sub, string? email, bool verified, string query = "")
    {
        using var challenge = await client.GetAsync(new Uri($"/api/auth/external/google{query}", UriKind.Relative), Ct);
        challenge.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var authorize = challenge.Headers.Location!;
        authorize.GetLeftPart(UriPartial.Path).ShouldBe(FakeProviderHandler.AuthorizeUrl);
        var state = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(authorize.Query)["state"].ToString();

        // The provider sends the browser back to the callback path.
        var callbackUrl = $"/signin-google?state={Uri.EscapeDataString(state)}&sub={sub}&email={Uri.EscapeDataString(email ?? string.Empty)}&verified={(verified ? "true" : "false")}";
        using var callback = await client.GetAsync(new Uri(callbackUrl, UriKind.Relative), Ct);
        callback.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        callback.Headers.Location!.OriginalString.ShouldBe("/api/auth/external/callback");

        using var finish = await client.GetAsync(callback.Headers.Location, Ct);
        finish.StatusCode.ShouldBe(HttpStatusCode.Redirect);

        // Like the Angular app after landing: refresh the session and the XSRF token.
        (await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative), Ct)).Dispose();
        return finish.Headers.Location!.OriginalString;
    }

    private static async Task CreateExternalOnlyAccountAsync(HttpClient client, string sub, string email)
    {
        (await SignInWithProviderAsync(client, sub, email, verified: true)).ShouldBe("/en/complete-profile");
        using var completed = await AuthApp.PostAsync(client, "/api/auth/complete-profile", new { displayName = "Anna", preferredLanguage = "en", acceptTerms = true });
        completed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static async Task<JsonElement> MeAsync(HttpClient client) =>
        await client.GetFromJsonAsync<JsonElement>(new Uri("/api/auth/me", UriKind.Relative), Ct);

    private static string NewSub() => Guid.NewGuid().ToString("N");
}
