using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BajkoweCuda.Api.IntegrationTests.Auth;
using BajkoweCuda.Api.IntegrationTests.Chat;
using BajkoweCuda.Api.IntegrationTests.Database;

namespace BajkoweCuda.Api.IntegrationTests.Orders;

/// <summary>
/// The order endpoints (docs/PLAN.md §3.5, §6, §13): drafts invisible to customers, cross-customer access denied,
/// invalid transitions answer 409 with the error code, and the full flow.
/// </summary>
[Trait("Category", SqlServerFixture.Category)]
public sealed class OrderApiTests(AuthApp app) : IClassFixture<AuthApp>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_full_flow_is_visible_to_the_customer_from_the_confirmation_on()
    {
        var admin = await AdminAsync();
        var customer = await CustomerWithChatAsync();

        var draft = await CreateAsync(admin, customer, price: 120, referenceAttachmentId: customer.ImageId);
        draft.GetProperty("status").GetString().ShouldBe("Draft");
        draft.GetProperty("referenceImage").GetProperty("id").GetGuid().ShouldBe(customer.ImageId);
        var id = draft.GetProperty("id").GetGuid();
        var number = draft.GetProperty("number").GetString()!;
        number.ShouldStartWith("BC-");

        // A draft does not exist for the customer.
        (await GetJsonAsync(customer.Client, "/api/orders")).GetArrayLength().ShouldBe(0);
        using (var hidden = await customer.Client.GetAsync(new Uri($"/api/orders/{id}", UriKind.Relative), Ct))
        {
            hidden.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        (await GetJsonAsync(customer.Client, "/api/auth/me")).GetProperty("hasOrders").GetBoolean().ShouldBeFalse();

        await StepAsync(admin, id, "confirm");

        var order = await GetJsonAsync(customer.Client, $"/api/orders/{id}");
        order.GetProperty("status").GetString().ShouldBe("AwaitingPayment");
        order.GetProperty("paymentInstructions").GetString()!.ShouldContain(number);
        order.GetProperty("paymentInstructions").GetString()!.ShouldContain("120.00 EUR");
        order.TryGetProperty("adminNote", out _).ShouldBeFalse();
        (await GetJsonAsync(customer.Client, "/api/auth/me")).GetProperty("hasOrders").GetBoolean().ShouldBeTrue();

        // The confirmation reaches the customer in the chat and by email (always, also with notifications off).
        var system = (await GetJsonAsync(customer.Client, "/api/chat/messages")).GetProperty("items")[0];
        system.GetProperty("kind").GetString().ShouldBe("system");
        system.GetProperty("systemEventType").GetString().ShouldBe("order.confirmed");
        system.GetProperty("systemEventPayload").GetProperty("number").GetString().ShouldBe(number);
        var email = await app.Emails.WaitForAsync(customer.Email, $"Order {number}: payment details");
        email.TextBody.ShouldContain("120.00 EUR");
        email.TextBody.ShouldContain($"/en/orders/{id}");

        await StepAsync(admin, id, "mark-paid");
        var photo = await AddPhotoAsync(admin, id);
        await StepAsync(admin, id, "mark-ready");
        await StepAsync(admin, id, "mark-shipped");
        await StepAsync(admin, id, "complete");

        order = await GetJsonAsync(customer.Client, $"/api/orders/{id}");
        order.GetProperty("status").GetString().ShouldBe("Completed");
        order.GetProperty("paymentInstructions").ValueKind.ShouldBe(JsonValueKind.Null);
        order.GetProperty("timeline").EnumerateArray().Select(step => step.GetProperty("status").GetString())
            .ShouldBe(["AwaitingPayment", "InProgress", "Ready", "Shipped", "Completed"]);
        var photoUrl = order.GetProperty("photos")[0].GetProperty("url").GetString()!;
        photoUrl.ShouldBe(photo.GetProperty("image").GetProperty("url").GetString());
        using var file = await customer.Client.GetAsync(new Uri(photoUrl, UriKind.Relative), Ct);
        file.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Another_customer_sees_neither_the_order_nor_its_photos()
    {
        var admin = await AdminAsync();
        var customer = await CustomerWithChatAsync();
        var stranger = await CustomerWithChatAsync();
        var id = (await CreateAsync(admin, customer)).GetProperty("id").GetGuid();
        await StepAsync(admin, id, "confirm");
        await StepAsync(admin, id, "mark-paid");
        var photoUrl = (await AddPhotoAsync(admin, id)).GetProperty("image").GetProperty("url").GetString()!;

        using var order = await stranger.Client.GetAsync(new Uri($"/api/orders/{id}", UriKind.Relative), Ct);
        using var photo = await stranger.Client.GetAsync(new Uri(photoUrl, UriKind.Relative), Ct);

        order.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AuthApp.ProblemCodeAsync(order)).ShouldBe("orders.not_found");
        photo.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await GetJsonAsync(stranger.Client, "/api/orders")).GetArrayLength().ShouldBe(0);
    }

    [Theory]
    [InlineData("mark-paid")]
    [InlineData("mark-ready")]
    [InlineData("mark-shipped")]
    [InlineData("complete")]
    public async Task Steps_from_the_wrong_status_answer_409(string step)
    {
        var admin = await AdminAsync();
        var customer = await CustomerWithChatAsync();
        var id = (await CreateAsync(admin, customer)).GetProperty("id").GetGuid();

        using var response = await AuthApp.PostAsync(admin, $"/api/admin/orders/{id}/{step}", new { });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AuthApp.ProblemCodeAsync(response)).ShouldBe("orders.invalid_transition");
    }

    [Fact]
    public async Task Ready_needs_a_photo_and_cancelling_needs_a_reason()
    {
        var admin = await AdminAsync();
        var customer = await CustomerWithChatAsync();
        var id = (await CreateAsync(admin, customer)).GetProperty("id").GetGuid();
        await StepAsync(admin, id, "confirm");
        await StepAsync(admin, id, "mark-paid");

        using var ready = await AuthApp.PostAsync(admin, $"/api/admin/orders/{id}/mark-ready", new { });
        using var cancel = await AuthApp.PostAsync(admin, $"/api/admin/orders/{id}/cancel", new { reason = "  " });

        ready.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AuthApp.ProblemCodeAsync(ready)).ShouldBe("orders.photo_required");
        cancel.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuthApp.ProblemCodeAsync(cancel)).ShouldBe("orders.reason_required");
    }

    [Fact]
    public async Task A_cancelled_order_shows_the_reason_to_the_customer()
    {
        var admin = await AdminAsync();
        var customer = await CustomerWithChatAsync();
        var id = (await CreateAsync(admin, customer)).GetProperty("id").GetGuid();
        await StepAsync(admin, id, "confirm");

        using (var cancel = await AuthApp.PostAsync(admin, $"/api/admin/orders/{id}/cancel", new { reason = "The fabric is sold out" }))
        {
            cancel.StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        var order = await GetJsonAsync(customer.Client, $"/api/orders/{id}");
        order.GetProperty("status").GetString().ShouldBe("Cancelled");
        order.GetProperty("cancellationReason").GetString().ShouldBe("The fabric is sold out");
    }

    [Fact]
    public async Task The_price_cannot_change_after_payment_and_stale_edits_conflict()
    {
        var admin = await AdminAsync();
        var customer = await CustomerWithChatAsync();
        var draft = await CreateAsync(admin, customer);
        var id = draft.GetProperty("id").GetGuid();
        var seen = draft.GetProperty("rowVersion").GetString();

        var edited = await PutAsync(admin, id, new { price = 135.5m, adminNote = "bigger", rowVersion = seen });
        edited.StatusCode.ShouldBe(HttpStatusCode.OK);
        var current = (await edited.Content.ReadFromJsonAsync<JsonElement>(Ct));
        current.GetProperty("price").GetProperty("amount").GetDecimal().ShouldBe(135.5m);
        current.GetProperty("adminNote").GetString().ShouldBe("bigger");

        // Another admin still holds the old version.
        using (var stale = await PutAsync(admin, id, new { title = "Old view", rowVersion = seen }))
        {
            stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await AuthApp.ProblemCodeAsync(stale)).ShouldBe("orders.conflict");
        }

        await StepAsync(admin, id, "confirm");
        await StepAsync(admin, id, "mark-paid");
        var latest = (await GetJsonAsync(admin, $"/api/admin/orders/{id}")).GetProperty("rowVersion").GetString();
        using var late = await PutAsync(admin, id, new { price = 99, rowVersion = latest });
        late.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await AuthApp.ProblemCodeAsync(late)).ShouldBe("orders.not_editable");
    }

    [Fact]
    public async Task The_reference_image_must_come_from_the_conversation()
    {
        var admin = await AdminAsync();
        var customer = await CustomerWithChatAsync();
        var other = await CustomerWithChatAsync();

        using var foreign = await AuthApp.PostAsync(admin, "/api/admin/orders", NewOrder(customer.ConversationId, other.ImageId));
        using var unknown = await AuthApp.PostAsync(admin, "/api/admin/orders", NewOrder(Guid.NewGuid(), null));

        foreign.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuthApp.ProblemCodeAsync(foreign)).ShouldBe("orders.image_unavailable");
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AuthApp.ProblemCodeAsync(unknown)).ShouldBe("orders.conversation_not_found");
    }

    [Fact]
    public async Task The_admin_list_filters_by_status_and_conversation()
    {
        var admin = await AdminAsync();
        var customer = await CustomerWithChatAsync();
        var draftId = (await CreateAsync(admin, customer)).GetProperty("id").GetGuid();
        var confirmedId = (await CreateAsync(admin, customer)).GetProperty("id").GetGuid();
        await StepAsync(admin, confirmedId, "confirm");

        var byConversation = await GetJsonAsync(admin, $"/api/admin/orders?conversationId={customer.ConversationId}");
        var awaiting = await GetJsonAsync(admin, $"/api/admin/orders?status=AwaitingPayment&conversationId={customer.ConversationId}");

        byConversation.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ShouldBe([confirmedId, draftId]);
        awaiting.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ShouldBe([confirmedId]);
        byConversation.GetProperty("items")[0].GetProperty("customerName").GetString().ShouldBe("Anna");
    }

    [Fact]
    public async Task Admin_order_endpoints_refuse_customers_and_admins_without_2fa()
    {
        var customer = await CustomerWithChatAsync();
        var (admin, _) = await app.CreateBrowserAsync();
        var adminEmail = app.NewAdminEmail();
        await app.RegisterConfirmedAsync(admin, adminEmail);
        (await AuthApp.LoginAsync(admin, adminEmail)).EnsureSuccessStatusCode();

        using var asCustomer = await AuthApp.PostAsync(customer.Client, "/api/admin/orders", NewOrder(customer.ConversationId, null));
        using var withoutSecondFactor = await admin.GetAsync(new Uri("/api/admin/orders", UriKind.Relative), Ct);

        asCustomer.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AuthApp.ProblemCodeAsync(withoutSecondFactor)).ShouldBe("auth.mfa_required");
    }

    [Fact]
    public async Task Deleting_the_customer_account_keeps_the_orders_anonymized()
    {
        var admin = await AdminAsync();
        var customer = await CustomerWithChatAsync();
        var id = (await CreateAsync(admin, customer, referenceAttachmentId: customer.ImageId)).GetProperty("id").GetGuid();
        await StepAsync(admin, id, "confirm");
        await StepAsync(admin, id, "mark-paid");
        var photoId = (await AddPhotoAsync(admin, id)).GetProperty("id").GetGuid();

        using (var deleted = await AuthApp.PostAsync(customer.Client, "/api/account/delete", new { password = AuthApp.Password }))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent, await deleted.Content.ReadAsStringAsync(Ct));
        }

        // The order stays (§8.6): no conversation, no reference image from the erased chat, the studio's photos kept.
        var order = await GetJsonAsync(admin, $"/api/admin/orders/{id}");
        order.GetProperty("status").GetString().ShouldBe("InProgress");
        order.GetProperty("customerName").GetString()!.ShouldNotContain("Anna");
        order.GetProperty("conversationId").ValueKind.ShouldBe(JsonValueKind.Null);
        order.GetProperty("referenceImage").ValueKind.ShouldBe(JsonValueKind.Null);
        order.GetProperty("photos").EnumerateArray().Select(photo => photo.GetProperty("id").GetGuid()).ShouldBe([photoId]);

        var list = await GetJsonAsync(admin, "/api/admin/orders");
        list.GetProperty("items").EnumerateArray().ShouldContain(item => item.GetProperty("id").GetGuid() == id);
    }

    private sealed record Customer(HttpClient Client, string Email, Guid ConversationId, Guid ImageId);

    private async Task<HttpClient> AdminAsync()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = app.NewAdminEmail();
        await app.RegisterConfirmedAsync(client, email);
        (await AuthApp.LoginAsync(client, email)).EnsureSuccessStatusCode();
        await AuthApp.EnableTwoFactorAsync(client);
        return client;
    }

    /// <summary>A confirmed customer whose conversation has one message with a photo.</summary>
    private async Task<Customer> CustomerWithChatAsync()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();
        await app.RegisterConfirmedAsync(client, email);
        (await AuthApp.LoginAsync(client, email)).EnsureSuccessStatusCode();

        using var upload = await client.PostAsync(new Uri("/api/chat/attachments", UriKind.Relative), TestImages.Form(TestImages.Jpeg()), Ct);
        upload.StatusCode.ShouldBe(HttpStatusCode.Created);
        var imageId = (await upload.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        using var message = await AuthApp.PostAsync(client, "/api/chat/messages", new { body = "A fox like this", attachmentIds = new[] { imageId } });
        message.StatusCode.ShouldBe(HttpStatusCode.Created);
        var conversationId = (await message.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("conversationId").GetGuid();
        return new Customer(client, email, conversationId, imageId);
    }

    private static object NewOrder(Guid conversationId, Guid? referenceAttachmentId, decimal price = 120) => new
    {
        conversationId,
        title = "Fox with a scarf",
        description = "Orange, 30 cm",
        price,
        referenceAttachmentId,
        adminNote = "green felt",
    };

    private static async Task<JsonElement> CreateAsync(HttpClient admin, Customer customer, decimal price = 120, Guid? referenceAttachmentId = null)
    {
        using var response = await AuthApp.PostAsync(admin, "/api/admin/orders", NewOrder(customer.ConversationId, referenceAttachmentId, price));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task StepAsync(HttpClient admin, Guid id, string step)
    {
        using var response = await AuthApp.PostAsync(admin, $"/api/admin/orders/{id}/{step}", new { });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<JsonElement> AddPhotoAsync(HttpClient admin, Guid id)
    {
        using var response = await admin.PostAsync(new Uri($"/api/admin/orders/{id}/photos", UriKind.Relative), TestImages.Form(TestImages.Jpeg()), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Ct));
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("photos").EnumerateArray().Last();
    }

    private static Task<HttpResponseMessage> PutAsync(HttpClient admin, Guid id, object body) =>
        admin.PutAsJsonAsync(new Uri($"/api/admin/orders/{id}", UriKind.Relative), body, Ct);

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }
}
