using System.Net;
using BajkoweCuda.Api.IntegrationTests.Auth;
using BajkoweCuda.Api.IntegrationTests.Database;
using BajkoweCuda.Application.Files;
using BajkoweCuda.Domain.Chat;
using BajkoweCuda.Domain.Orders;
using BajkoweCuda.Infrastructure.Chat;
using BajkoweCuda.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace BajkoweCuda.Api.IntegrationTests.Orders;

/// <summary>How orders are stored (docs/PLAN.md §3.5, §5): numbers, prices, photos, timeline, concurrency, deletion.</summary>
[Trait("Category", SqlServerFixture.Category)]
public sealed class OrderPersistenceTests(AuthApp app) : IClassFixture<AuthApp>
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_order_is_stored_with_a_number_from_the_sequence_its_price_and_timeline()
    {
        var (customerId, conversationId, adminId) = await PeopleAsync();

        Guid firstId;
        Guid secondId;
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var first = Order.CreateDraft(customerId, conversationId, "Fox", "Orange", Money.Euros(129.90m), null, "green felt", adminId, Now);
            var second = Order.CreateDraft(customerId, conversationId, "Bear", null, Money.Euros(80), null, null, adminId, Now);
            db.Orders.AddRange(first, second);
            await db.SaveChangesAsync(Ct);
            (firstId, secondId) = (first.Id, second.Id);
        }

        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var first = await db.Orders.Include(order => order.StatusChanges).SingleAsync(order => order.Id == firstId, Ct);
            var second = await db.Orders.SingleAsync(order => order.Id == secondId, Ct);

            first.Number.ShouldBeGreaterThan(0);
            // From one database sequence: unique, but other tests take numbers in parallel.
            second.Number.ShouldBeGreaterThan(0);
            second.Number.ShouldNotBe(first.Number);
            first.DisplayNumber.ShouldBe(Order.FormatNumber(first.Number));
            first.Price.ShouldBe(Money.Euros(129.90m));
            first.AdminNote.ShouldBe("green felt");
            first.StatusChanges.ShouldHaveSingleItem().ToStatus.ShouldBe(OrderStatus.Draft);
            first.RowVersion.ShouldNotBeEmpty();
        }
    }

    [Fact]
    public async Task Two_admins_changing_the_same_order_conflict()
    {
        var (customerId, conversationId, adminId) = await PeopleAsync();
        var orderId = await SaveDraftAsync(customerId, conversationId, adminId);

        await using var firstScope = app.Factory.Services.CreateAsyncScope();
        await using var secondScope = app.Factory.Services.CreateAsyncScope();
        var firstDb = firstScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var secondDb = secondScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var first = await firstDb.Orders.SingleAsync(order => order.Id == orderId, Ct);
        var second = await secondDb.Orders.SingleAsync(order => order.Id == orderId, Ct);

        first.ChangePrice(Money.Euros(140));
        await firstDb.SaveChangesAsync(Ct);
        second.ChangePrice(Money.Euros(150));

        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => secondDb.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task Photos_of_an_order_survive_the_cleanup_of_abandoned_uploads()
    {
        var (customerId, conversationId, adminId) = await PeopleAsync();
        var orderId = await SaveDraftAsync(customerId, conversationId, adminId);

        await using var scope = app.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var photo = Attachment.Uploaded(Guid.NewGuid(), adminId, FileContainers.Attachments, "photo.webp", "photo-thumb.webp", "image/webp", 10, 10, 10, Now.AddDays(-3));
        var stray = Attachment.Uploaded(Guid.NewGuid(), adminId, FileContainers.Attachments, "stray.webp", "stray-thumb.webp", "image/webp", 10, 10, 10, Now.AddDays(-3));
        db.Attachments.AddRange(photo, stray);
        var order = await db.Orders.SingleAsync(item => item.Id == orderId, Ct);
        order.Confirm(adminId, Now);
        order.MarkPaid(adminId, Now);
        order.AddPhoto(photo.Id, Now);
        await db.SaveChangesAsync(Ct);

        var cleanup = new Attachments(db, scope.ServiceProvider.GetRequiredService<IFileStore>(), new FakeTimeProvider(Now), NullLogger<Attachments>.Instance);
        await cleanup.DeleteAbandonedAsync(1000, Ct);

        (await db.Attachments.AnyAsync(item => item.Id == photo.Id, Ct)).ShouldBeTrue();
        (await db.Attachments.AnyAsync(item => item.Id == stray.Id, Ct)).ShouldBeFalse();
        (await db.OrderPhotos.CountAsync(item => item.OrderId == orderId, Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task Deleting_the_customer_keeps_the_order_without_the_conversation()
    {
        var (client, customerId) = await CustomerAsync();
        var adminId = (await PeopleAsync()).AdminId;
        Guid conversationId;
        Guid imageId;
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var conversation = Conversation.Start(customerId, Now);
            var image = Attachment.Uploaded(Guid.NewGuid(), customerId, FileContainers.Attachments, "ref.webp", "ref-thumb.webp", "image/webp", 10, 10, 10, Now);
            db.Conversations.Add(conversation);
            db.Attachments.Add(image);
            db.Messages.Add(conversation.PostText(ChatSide.Customer, customerId, "Like this", [image], Now));
            await db.SaveChangesAsync(Ct);
            (conversationId, imageId) = (conversation.Id, image.Id);
        }

        var orderId = await SaveDraftAsync(customerId, conversationId, adminId, imageId);

        using (var deleted = await AuthApp.PostAsync(client, "/api/account/delete", new { password = AuthApp.Password }))
        {
            deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var order = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders.AsNoTracking().SingleAsync(item => item.Id == orderId, Ct);
            order.CustomerId.ShouldBe(customerId);
            order.ConversationId.ShouldBeNull();
            order.ReferenceImageId.ShouldBeNull();
        }
    }

    private async Task<Guid> SaveDraftAsync(Guid customerId, Guid conversationId, Guid adminId, Guid? referenceImageId = null)
    {
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = Order.CreateDraft(customerId, conversationId, "Fox", null, Money.Euros(120), referenceImageId, null, adminId, Now);
        db.Orders.Add(order);
        await db.SaveChangesAsync(Ct);
        return order.Id;
    }

    /// <summary>A customer with a conversation, and an admin (users only; no sign-in needed here).</summary>
    private async Task<(Guid CustomerId, Guid ConversationId, Guid AdminId)> PeopleAsync()
    {
        var (_, customerId) = await CustomerAsync();
        var (_, adminId) = await CustomerAsync();
        await using var scope = app.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var conversation = Conversation.Start(customerId, Now);
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync(Ct);
        return (customerId, conversation.Id, adminId);
    }

    private async Task<(HttpClient Client, Guid UserId)> CustomerAsync()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();
        var userId = await app.RegisterConfirmedAsync(client, email);
        (await AuthApp.LoginAsync(client, email)).EnsureSuccessStatusCode();
        return (client, userId);
    }
}
