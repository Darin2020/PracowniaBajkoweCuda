using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using BajkoweCuda.Api.IntegrationTests.Auth;
using BajkoweCuda.Api.IntegrationTests.Database;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace BajkoweCuda.Api.IntegrationTests.Chat;

/// <summary>The chat API and its real-time events (docs/PLAN.md §3.4, §6, §13: IDOR on messages, rate limits).</summary>
[Trait("Category", SqlServerFixture.Category)]
public sealed class ChatTests(AuthApp app) : IClassFixture<AuthApp>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_first_message_starts_the_conversation()
    {
        var customer = await CustomerAsync();
        (await GetJsonAsync(customer.Client, "/api/chat")).GetProperty("conversationId").ValueKind.ShouldBe(JsonValueKind.Null);
        var imageId = await UploadAsync(customer.Client);

        var message = await PostAsync(customer.Client, "/api/chat/messages", new { body = "  A blue bunny, please  ", attachmentIds = new[] { imageId } });

        message.GetProperty("body").GetString().ShouldBe("A blue bunny, please");
        message.GetProperty("from").GetString().ShouldBe("customer");
        message.GetProperty("images")[0].GetProperty("id").GetGuid().ShouldBe(imageId);
        var chat = await GetJsonAsync(customer.Client, "/api/chat");
        chat.GetProperty("conversationId").GetGuid().ShouldBe(message.GetProperty("conversationId").GetGuid());
        chat.GetProperty("emailPromptAnswered").GetBoolean().ShouldBeFalse();
        chat.GetProperty("unreadCount").GetInt32().ShouldBe(0);
        var page = await GetJsonAsync(customer.Client, "/api/chat/messages");
        page.GetProperty("items").GetArrayLength().ShouldBe(1);
        page.GetProperty("items")[0].GetProperty("images")[0].GetProperty("thumbnailUrl").GetString().ShouldBe($"/api/files/{imageId}/thumbnail");
    }

    [Theory]
    [InlineData("", "chat.empty_message")]
    [InlineData("   ", "chat.empty_message")]
    public async Task Broken_rules_answer_400_with_their_code(string body, string code)
    {
        var customer = await CustomerAsync();

        using var response = await AuthApp.PostAsync(customer.Client, "/api/chat/messages", new { body });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuthApp.ProblemCodeAsync(response)).ShouldBe(code);
    }

    [Fact]
    public async Task A_message_has_at_most_2000_characters()
    {
        var customer = await CustomerAsync();

        using var response = await AuthApp.PostAsync(customer.Client, "/api/chat/messages", new { body = new string('a', 2001) });

        (await AuthApp.ProblemCodeAsync(response)).ShouldBe("chat.message_too_long");
    }

    [Fact]
    public async Task Images_of_someone_else_or_already_sent_cannot_be_sent()
    {
        var customer = await CustomerAsync();
        var other = await CustomerAsync();
        var othersImage = await UploadAsync(other.Client);
        var mine = await UploadAsync(customer.Client);
        await PostAsync(customer.Client, "/api/chat/messages", new { body = "First", attachmentIds = new[] { mine } });

        foreach (var id in new[] { othersImage, mine, Guid.NewGuid() })
        {
            using var response = await AuthApp.PostAsync(customer.Client, "/api/chat/messages", new { body = "Again", attachmentIds = new[] { id } });
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await AuthApp.ProblemCodeAsync(response)).ShouldBe("chat.attachment_unavailable");
        }
    }

    [Fact]
    public async Task Chatting_needs_a_confirmed_email_address()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();
        (await AuthApp.RegisterAsync(client, email)).EnsureSuccessStatusCode();
        (await AuthApp.LoginAsync(client, email)).EnsureSuccessStatusCode();

        using var get = await client.GetAsync(new Uri("/api/chat", UriKind.Relative), Ct);
        using var post = await AuthApp.PostAsync(client, "/api/chat/messages", new { body = "Hello" });

        get.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AuthApp.ProblemCodeAsync(post)).ShouldBe("chat.email_not_confirmed");
    }

    [Fact]
    public async Task A_studio_reply_is_unread_until_the_customer_reads_it()
    {
        var customer = await CustomerAsync();
        var admin = await AdminAsync();
        var conversationId = (await PostAsync(customer.Client, "/api/chat/messages", new { body = "Hi" })).GetProperty("conversationId").GetGuid();

        var reply = await PostAsync(admin.Client, $"/api/admin/conversations/{conversationId}/messages", new { body = "Hello! What size?" });

        reply.GetProperty("from").GetString().ShouldBe("studio");
        (await GetJsonAsync(customer.Client, "/api/chat")).GetProperty("unreadCount").GetInt32().ShouldBe(1);
        (await GetJsonAsync(customer.Client, "/api/auth/me")).GetProperty("unreadCount").GetInt32().ShouldBe(1);

        using (var read = await AuthApp.PostAsync(customer.Client, "/api/chat/read", new { upToMessageId = reply.GetProperty("id").GetGuid() }))
        {
            read.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        (await GetJsonAsync(customer.Client, "/api/chat")).GetProperty("unreadCount").GetInt32().ShouldBe(0);
        var seenByAdmin = await GetJsonAsync(admin.Client, $"/api/admin/conversations/{conversationId}/messages");
        seenByAdmin.GetProperty("items")[0].GetProperty("readByCustomerAt").ValueKind.ShouldBe(JsonValueKind.String);
    }

    [Fact]
    public async Task A_customer_cannot_mark_messages_of_another_conversation()
    {
        var customer = await CustomerAsync();
        var other = await CustomerAsync();
        await PostAsync(customer.Client, "/api/chat/messages", new { body = "Mine" });
        var othersMessage = await PostAsync(other.Client, "/api/chat/messages", new { body = "Theirs" });

        using var response = await AuthApp.PostAsync(customer.Client, "/api/chat/read", new { upToMessageId = othersMessage.GetProperty("id").GetGuid() });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AuthApp.ProblemCodeAsync(response)).ShouldBe("chat.message_not_found");
        var page = await GetJsonAsync(customer.Client, "/api/chat/messages");
        page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("body").GetString()).ShouldBe(["Mine"]);
    }

    [Fact]
    public async Task History_pages_go_back_in_time_without_gaps()
    {
        var customer = await CustomerAsync();
        for (var i = 1; i <= 5; i++)
        {
            await PostAsync(customer.Client, "/api/chat/messages", new { body = $"Message {i}" });
        }

        var bodies = new List<string>();
        string? before = null;
        do
        {
            var page = await GetJsonAsync(customer.Client, $"/api/chat/messages?take=2{(before is null ? string.Empty : $"&before={before}")}");
            bodies.AddRange(page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("body").GetString()!));
            before = page.GetProperty("nextCursor").ValueKind == JsonValueKind.Null ? null : page.GetProperty("nextCursor").GetString();
        }
        while (before is not null);

        bodies.ShouldBe(["Message 5", "Message 4", "Message 3", "Message 2", "Message 1"]);
    }

    [Fact]
    public async Task The_email_setting_needs_a_started_conversation_and_answers_the_question()
    {
        var customer = await CustomerAsync();

        using (var early = await customer.Client.PutAsJsonAsync(new Uri("/api/chat/email-notifications", UriKind.Relative), new { enabled = true }, Ct))
        {
            early.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await AuthApp.ProblemCodeAsync(early)).ShouldBe("chat.not_started");
        }

        await PostAsync(customer.Client, "/api/chat/messages", new { body = "Hi" });
        using var response = await customer.Client.PutAsJsonAsync(new Uri("/api/chat/email-notifications", UriKind.Relative), new { enabled = true }, Ct);

        var chat = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        chat.GetProperty("emailNotificationsEnabled").GetBoolean().ShouldBeTrue();
        chat.GetProperty("emailPromptAnswered").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task The_admin_list_needs_2fa_and_shows_unread_conversations_first()
    {
        var quiet = await CustomerAsync();
        var waiting = await CustomerAsync();
        var admin = await AdminAsync();
        var quietConversation = (await PostAsync(quiet.Client, "/api/chat/messages", new { body = "Older question" })).GetProperty("conversationId").GetGuid();
        var quietMessage = (await GetJsonAsync(admin.Client, $"/api/admin/conversations/{quietConversation}/messages")).GetProperty("items")[0].GetProperty("id").GetGuid();
        (await AuthApp.PostAsync(admin.Client, $"/api/admin/conversations/{quietConversation}/read", new { upToMessageId = quietMessage })).EnsureSuccessStatusCode();
        await PostAsync(waiting.Client, "/api/chat/messages", new { body = "Still waiting" });
        // A newer, already read conversation must not push the unread one down.
        await PostAsync(admin.Client, $"/api/admin/conversations/{quietConversation}/messages", new { body = "Answered" });

        var list = await GetJsonAsync(admin.Client, $"/api/admin/conversations?search={Uri.EscapeDataString(waiting.Email)}");
        var row = list.GetProperty("items").EnumerateArray().Single();
        row.GetProperty("customer").GetProperty("email").GetString().ShouldBe(waiting.Email);
        row.GetProperty("unreadCount").GetInt32().ShouldBe(1);
        row.GetProperty("lastMessagePreview").GetString().ShouldBe("Still waiting");

        var all = (await GetJsonAsync(admin.Client, "/api/admin/conversations?take=100")).GetProperty("items").EnumerateArray().ToList();
        var waitingIndex = all.FindIndex(item => item.GetProperty("customer").GetProperty("email").GetString() == waiting.Email);
        var quietIndex = all.FindIndex(item => item.GetProperty("customer").GetProperty("email").GetString() == quiet.Email);
        waitingIndex.ShouldBeLessThan(quietIndex);
    }

    [Fact]
    public async Task Admin_chat_endpoints_refuse_customers_and_admins_without_2fa()
    {
        var customer = await CustomerAsync();
        var (admin, _) = await app.CreateBrowserAsync();
        var adminEmail = app.NewAdminEmail();
        await app.RegisterConfirmedAsync(admin, adminEmail);
        (await AuthApp.LoginAsync(admin, adminEmail)).EnsureSuccessStatusCode();
        var path = new Uri("/api/admin/conversations", UriKind.Relative);

        using var asCustomer = await customer.Client.GetAsync(path, Ct);
        using var withoutSecondFactor = await admin.GetAsync(path, Ct);

        asCustomer.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AuthApp.ProblemCodeAsync(withoutSecondFactor)).ShouldBe("auth.mfa_required");
    }

    [Fact]
    public async Task Unknown_conversations_answer_404()
    {
        var admin = await AdminAsync();

        using var get = await admin.Client.GetAsync(new Uri($"/api/admin/conversations/{Guid.NewGuid()}", UriKind.Relative), Ct);
        using var post = await AuthApp.PostAsync(admin.Client, $"/api/admin/conversations/{Guid.NewGuid()}/messages", new { body = "Hello?" });

        (await AuthApp.ProblemCodeAsync(get)).ShouldBe("chat.conversation_not_found");
        (await AuthApp.ProblemCodeAsync(post)).ShouldBe("chat.conversation_not_found");
    }

    [Fact]
    public async Task Messages_are_rate_limited_per_user()
    {
        await using var limited = app.Factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:ChatMessagesPerMinute", "2"));
        var browser = new BrowserHandler();
        using var client = limited.CreateDefaultClient(BrowserHandler.BaseAddress, browser);
        (await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative), Ct)).EnsureSuccessStatusCode();
        var email = AuthApp.NewEmail();
        await app.RegisterConfirmedAsync(client, email);
        (await AuthApp.LoginAsync(client, email)).EnsureSuccessStatusCode();

        for (var i = 0; i < 2; i++)
        {
            (await AuthApp.PostAsync(client, "/api/chat/messages", new { body = $"Hi {i}" })).StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        using var refused = await AuthApp.PostAsync(client, "/api/chat/messages", new { body = "One too many" });
        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Messages_arrive_in_real_time_on_the_other_side_only()
    {
        var customer = await CustomerAsync();
        var bystander = await CustomerAsync();
        var admin = await AdminAsync();
        await using var customerHub = await ConnectAsync(customer.Browser);
        await using var bystanderHub = await ConnectAsync(bystander.Browser);
        await using var adminHub = await ConnectAsync(admin.Browser);

        var sent = await PostAsync(customer.Client, "/api/chat/messages", new { body = "Is anyone there?" });
        var arrivedAtStudio = await adminHub.NextMessageAsync(message => message.GetProperty("id").GetGuid() == sent.GetProperty("id").GetGuid());
        arrivedAtStudio.GetProperty("body").GetString().ShouldBe("Is anyone there?");

        var conversationId = sent.GetProperty("conversationId").GetGuid();
        await PostAsync(admin.Client, $"/api/admin/conversations/{conversationId}/messages", new { body = "Yes, hello!" });

        var reply = await customerHub.NextMessageAsync(message => message.GetProperty("from").GetString() == "studio");
        reply.GetProperty("body").GetString().ShouldBe("Yes, hello!");
        (await customerHub.NextUnreadCountAsync()).ShouldBe(1);
        bystanderHub.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_hub_needs_a_session()
    {
        var (anonymous, _) = await app.CreateBrowserAsync();

        using var response = await anonymous.PostAsync(new Uri("/hubs/chat/negotiate?negotiateVersion=1", UriKind.Relative), null, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private sealed record Person(HttpClient Client, BrowserHandler Browser, string Email);

    private async Task<Person> CustomerAsync()
    {
        var (client, browser) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();
        await app.RegisterConfirmedAsync(client, email);
        (await AuthApp.LoginAsync(client, email)).EnsureSuccessStatusCode();
        return new Person(client, browser, email);
    }

    private async Task<Person> AdminAsync()
    {
        var (client, browser) = await app.CreateBrowserAsync();
        var email = app.NewAdminEmail();
        await app.RegisterConfirmedAsync(client, email);
        (await AuthApp.LoginAsync(client, email)).EnsureSuccessStatusCode();
        await AuthApp.EnableTwoFactorAsync(client);
        return new Person(client, browser, email);
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
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

    private async Task<HubListener> ConnectAsync(BrowserHandler browser)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(BrowserHandler.BaseAddress, "/hubs/chat"), options =>
            {
                // The test server does not do WebSockets through this handler; long polling uses plain requests.
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => new BrowserHandler(browser.Cookies) { InnerHandler = app.Factory.Server.CreateHandler() };
            })
            .Build();
        var listener = new HubListener(connection);
        await connection.StartAsync(Ct);
        return listener;
    }

    /// <summary>Collects the events a connection receives.</summary>
    private sealed class HubListener : IAsyncDisposable
    {
        private readonly HubConnection _connection;
        private readonly Channel<JsonElement> _messages = Channel.CreateUnbounded<JsonElement>();
        private readonly Channel<int> _unread = Channel.CreateUnbounded<int>();

        public HubListener(HubConnection connection)
        {
            _connection = connection;
            connection.On<JsonElement>("MessageReceived", message =>
            {
                Messages.Add(message);
                _messages.Writer.TryWrite(message);
            });
            connection.On<int>("UnreadCountChanged", count => _unread.Writer.TryWrite(count));
        }

        public List<JsonElement> Messages { get; } = [];

        public async Task<JsonElement> NextMessageAsync(Func<JsonElement, bool> match)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            while (true)
            {
                var message = await _messages.Reader.ReadAsync(timeout.Token);
                if (match(message))
                {
                    return message;
                }
            }
        }

        public async Task<int> NextUnreadCountAsync()
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            return await _unread.Reader.ReadAsync(timeout.Token);
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();
    }
}
