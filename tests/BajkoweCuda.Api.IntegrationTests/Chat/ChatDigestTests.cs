using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BajkoweCuda.Api.IntegrationTests.Auth;
using BajkoweCuda.Api.IntegrationTests.Database;
using BajkoweCuda.Infrastructure.Emails;
using BajkoweCuda.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace BajkoweCuda.Api.IntegrationTests.Chat;

/// <summary>Batched chat email notifications (docs/PLAN.md §3.6, §13: notification batching).</summary>
[Trait("Category", SqlServerFixture.Category)]
public sealed class ChatDigestTests : IClassFixture<AuthApp>, IAsyncDisposable
{
    private static readonly TimeSpan Delay = TimeSpan.FromMinutes(5);

    private readonly AuthApp _app;
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly WebApplicationFactory<Program> _factory;

    public ChatDigestTests(AuthApp app)
    {
        _app = app;
        // The application's clock, which the dispatcher waits on: the test moves it past the delay.
        _factory = app.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(_time)));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Customer_messages_reach_the_admins_in_one_email_after_the_delay()
    {
        var admin = await SignInAsync(_app.NewAdminEmail());
        var customer = await SignInAsync(AuthApp.NewEmail());

        var first = await PostAsync(customer.Client, "/api/chat/messages", new { body = "I would like a fox." });
        await PostAsync(customer.Client, "/api/chat/messages", new { body = "Orange, with a scarf.", attachmentIds = new[] { await UploadAsync(customer.Client) } });
        var conversationId = first.GetProperty("conversationId").GetGuid();

        var pending = await DigestsAsync(conversationId, admin.UserId);
        pending.Count.ShouldBe(1, "the second message joins the pending notification");
        pending[0].SendAfter.ShouldBe(_time.GetUtcNow() + Delay, TimeSpan.FromSeconds(5));
        _app.Emails.Count(admin.Email, "New message from Anna").ShouldBe(0);

        _time.Advance(Delay + TimeSpan.FromSeconds(1));

        var email = await _app.Emails.WaitForAsync(admin.Email, "New message from Anna");
        email.TextBody.ShouldContain("I would like a fox.");
        email.TextBody.ShouldContain("Orange, with a scarf. · Photos: 1");
        // The photo itself travels inside the email as a small JPEG (the file needs a session, a mail client has none).
        var photo = email.Images.ShouldHaveSingleItem();
        photo.ContentType.ShouldBe("image/jpeg");
        TestImages.Describe(photo.Content).Format.ShouldBe(SkiaSharp.SKEncodedImageFormat.Jpeg);
        email.HtmlBody.ShouldContain($"cid:{photo.ContentId}");
        email.TextBody.ShouldContain($"/en/admin/conversations/{conversationId}");
        (await DigestsAsync(conversationId, admin.UserId)).Single().Status.ShouldBe(EmailStatus.Sent);
    }

    [Fact]
    public async Task Nothing_is_sent_when_the_messages_were_read_in_time()
    {
        var admin = await SignInAsync(_app.NewAdminEmail());
        await AuthApp.EnableTwoFactorAsync(admin.Client);
        var customer = await SignInAsync(AuthApp.NewEmail());
        var message = await PostAsync(customer.Client, "/api/chat/messages", new { body = "Quick question" });
        var conversationId = message.GetProperty("conversationId").GetGuid();

        (await AuthApp.PostAsync(admin.Client, $"/api/admin/conversations/{conversationId}/read", new { upToMessageId = message.GetProperty("id").GetGuid() }))
            .EnsureSuccessStatusCode();
        _time.Advance(Delay + TimeSpan.FromSeconds(1));

        (await WaitForStatusAsync(conversationId, admin.UserId)).ShouldBe(EmailStatus.Skipped);
        _app.Emails.Count(admin.Email, "New message from Anna").ShouldBe(0);
    }

    [Fact]
    public async Task The_customer_is_emailed_only_with_notifications_on()
    {
        var admin = await SignInAsync(_app.NewAdminEmail());
        await AuthApp.EnableTwoFactorAsync(admin.Client);
        var customer = await SignInAsync(AuthApp.NewEmail());
        var conversationId = (await PostAsync(customer.Client, "/api/chat/messages", new { body = "Hello" })).GetProperty("conversationId").GetGuid();

        await PostAsync(admin.Client, $"/api/admin/conversations/{conversationId}/messages", new { body = "Not emailed" });
        (await DigestsAsync(conversationId, customer.UserId)).ShouldBeEmpty();

        using (var on = await customer.Client.PutAsJsonAsync(new Uri("/api/chat/email-notifications", UriKind.Relative), new { enabled = true }, Ct))
        {
            on.EnsureSuccessStatusCode();
        }

        await PostAsync(admin.Client, $"/api/admin/conversations/{conversationId}/messages", new { body = "We can make it in two weeks." });
        _time.Advance(Delay + TimeSpan.FromSeconds(1));

        var email = await _app.Emails.WaitForAsync(customer.Email, "New message from Pracownia Bajkowe Cuda");
        // Every unread studio message is listed, also the one from before notifications were turned on.
        email.TextBody.ShouldContain("Not emailed");
        email.TextBody.ShouldContain("We can make it in two weeks.");
        email.TextBody.ShouldContain("/en/chat");
        email.TextBody.ShouldContain("Send replies to my email");
    }

    [Fact]
    public async Task Turning_notifications_off_cancels_a_pending_email()
    {
        var admin = await SignInAsync(_app.NewAdminEmail());
        await AuthApp.EnableTwoFactorAsync(admin.Client);
        var customer = await SignInAsync(AuthApp.NewEmail());
        var conversationId = (await PostAsync(customer.Client, "/api/chat/messages", new { body = "Hello" })).GetProperty("conversationId").GetGuid();
        (await customer.Client.PutAsJsonAsync(new Uri("/api/chat/email-notifications", UriKind.Relative), new { enabled = true }, Ct)).EnsureSuccessStatusCode();
        await PostAsync(admin.Client, $"/api/admin/conversations/{conversationId}/messages", new { body = "Reply" });

        (await customer.Client.PutAsJsonAsync(new Uri("/api/chat/email-notifications", UriKind.Relative), new { enabled = false }, Ct)).EnsureSuccessStatusCode();
        _time.Advance(Delay + TimeSpan.FromSeconds(1));

        (await WaitForStatusAsync(conversationId, customer.UserId)).ShouldBe(EmailStatus.Skipped);
    }

    private sealed record Person(HttpClient Client, string Email, Guid UserId);

    private async Task<Person> SignInAsync(string email)
    {
        var browser = new BrowserHandler();
        var client = _factory.CreateDefaultClient(BrowserHandler.BaseAddress, browser);
        (await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative), Ct)).EnsureSuccessStatusCode();
        var userId = await _app.RegisterConfirmedAsync(client, email);
        (await AuthApp.LoginAsync(client, email)).EnsureSuccessStatusCode();
        return new Person(client, email, userId);
    }

    private async Task<List<EmailOutboxMessage>> DigestsAsync(Guid conversationId, Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().EmailOutbox.AsNoTracking()
            .Where(message => message.Kind == EmailKind.ChatDigest && message.ConversationId == conversationId && message.ToUserId == userId)
            .ToListAsync(Ct);
    }

    private async Task<EmailStatus> WaitForStatusAsync(Guid conversationId, Guid userId)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
        while (true)
        {
            var status = (await DigestsAsync(conversationId, userId)).Single().Status;
            if (status != EmailStatus.Pending || DateTimeOffset.UtcNow > deadline)
            {
                return status;
            }

            await Task.Delay(100, Ct);
        }
    }

    private static async Task<JsonElement> PostAsync(HttpClient client, string path, object body)
    {
        using var response = await AuthApp.PostAsync(client, path, body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<Guid> UploadAsync(HttpClient client)
    {
        using var response = await client.PostAsync(new Uri("/api/chat/attachments", UriKind.Relative), TestImages.Form(TestImages.Jpeg()), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }
}
