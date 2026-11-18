using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BajkoweCuda.Api.IntegrationTests.Auth;
using BajkoweCuda.Api.IntegrationTests.Database;
using BajkoweCuda.Application.Files;
using BajkoweCuda.Domain.Chat;
using BajkoweCuda.Infrastructure.Chat;
using BajkoweCuda.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SkiaSharp;

namespace BajkoweCuda.Api.IntegrationTests.Chat;

/// <summary>Chat image uploads and file access (docs/PLAN.md §8.4, §13: upload validation, IDOR on files).</summary>
[Trait("Category", SqlServerFixture.Category)]
public sealed class AttachmentTests(AuthApp app) : IClassFixture<AuthApp>
{
    private static readonly Uri Upload = new("/api/chat/attachments", UriKind.Relative);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_upload_is_re_encoded_upright_without_exif_and_served_privately()
    {
        var (client, _) = await SignedInCustomerAsync();

        using var response = await client.PostAsync(Upload, TestImages.Form(TestImages.JpegWithExif(300, 200)), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        // Orientation 6 means "turn 90°": the stored image is 200 × 300.
        body.GetProperty("width").GetInt32().ShouldBe(200);
        body.GetProperty("height").GetInt32().ShouldBe(300);

        using var file = await client.GetAsync(new Uri(body.GetProperty("url").GetString()!, UriKind.Relative), Ct);
        file.StatusCode.ShouldBe(HttpStatusCode.OK);
        file.Content.Headers.ContentType?.MediaType.ShouldBe("image/webp");
        file.Headers.CacheControl?.Private.ShouldBeTrue();
        file.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
        var bytes = await file.Content.ReadAsByteArrayAsync(Ct);
        TestImages.Describe(bytes).ShouldBe((200, 300, SKEncodedImageFormat.Webp));
        Encoding.ASCII.GetString(bytes).ShouldNotContain(TestImages.Secret);
        Encoding.ASCII.GetString(bytes).ShouldNotContain("Exif");

        using var thumbnail = await client.GetAsync(new Uri(body.GetProperty("thumbnailUrl").GetString()!, UriKind.Relative), Ct);
        thumbnail.StatusCode.ShouldBe(HttpStatusCode.OK);
        TestImages.Describe(await thumbnail.Content.ReadAsByteArrayAsync(Ct)).ShouldBe((200, 300, SKEncodedImageFormat.Webp));
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpeg")]
    public async Task Large_images_are_scaled_to_1600_and_thumbnails_to_480(string format)
    {
        var (client, _) = await SignedInCustomerAsync();
        // JPEG decoders scale in eighths; 2400 px would decode at 5/8 = 1500 px if the scale were rounded down.
        var file = format == "png" ? TestImages.Png(4000, 3000) : TestImages.Jpeg(2400, 1800);

        using var response = await client.PostAsync(Upload, TestImages.Form(file, $"big.{format}", $"image/{format}"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("width").GetInt32().ShouldBe(1600);
        body.GetProperty("height").GetInt32().ShouldBe(1200);
        using var thumbnail = await client.GetAsync(new Uri(body.GetProperty("thumbnailUrl").GetString()!, UriKind.Relative), Ct);
        TestImages.Describe(await thumbnail.Content.ReadAsByteArrayAsync(Ct)).ShouldBe((480, 360, SKEncodedImageFormat.Webp));
    }

    [Fact]
    public async Task Webp_uploads_are_accepted()
    {
        var (client, _) = await SignedInCustomerAsync();

        using var response = await client.PostAsync(Upload, TestImages.Form(TestImages.Webp(), "a.webp", "image/webp"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task The_type_is_decided_by_the_content_not_by_the_name_or_the_content_type()
    {
        var (client, _) = await SignedInCustomerAsync();
        var html = Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>");

        using var response = await client.PostAsync(Upload, TestImages.Form(html, "photo.jpg", "image/jpeg"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuthApp.ProblemCodeAsync(response)).ShouldBe("files.unsupported_type");
    }

    [Fact]
    public async Task A_damaged_image_is_refused()
    {
        var (client, _) = await SignedInCustomerAsync();
        var jpeg = TestImages.Jpeg();

        using var response = await client.PostAsync(Upload, TestImages.Form(jpeg[..(jpeg.Length / 3)]), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuthApp.ProblemCodeAsync(response)).ShouldBe("files.unreadable");
    }

    [Fact]
    public async Task Files_over_10_MB_are_refused()
    {
        var (client, _) = await SignedInCustomerAsync();
        var file = new byte[(10 * 1024 * 1024) + 1];
        TestImages.Jpeg().AsSpan(0, 3).CopyTo(file);

        using var response = await client.PostAsync(Upload, TestImages.Form(file), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuthApp.ProblemCodeAsync(response)).ShouldBe("files.too_large");
    }

    [Fact]
    public async Task Images_over_40_megapixels_are_refused()
    {
        var (client, _) = await SignedInCustomerAsync();

        using var response = await client.PostAsync(Upload, TestImages.Form(TestImages.HugePngHeader(), "huge.png", "image/png"), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuthApp.ProblemCodeAsync(response)).ShouldBe("files.too_many_pixels");
    }

    [Fact]
    public async Task A_request_without_a_file_is_refused()
    {
        var (client, _) = await SignedInCustomerAsync();

        using var form = new MultipartFormDataContent { { new StringContent("hello"), "note" } };

        using var response = await client.PostAsync(Upload, form, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await AuthApp.ProblemCodeAsync(response)).ShouldBe("files.missing");
    }

    [Fact]
    public async Task Uploading_needs_a_confirmed_email_address()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();
        (await AuthApp.RegisterAsync(client, email)).EnsureSuccessStatusCode();
        // Unconfirmed accounts can sign in; the chat is what waits for the confirmation.
        (await AuthApp.LoginAsync(client, email)).EnsureSuccessStatusCode();

        using var response = await client.PostAsync(Upload, TestImages.Form(TestImages.Jpeg()), Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await AuthApp.ProblemCodeAsync(response)).ShouldBe("chat.email_not_confirmed");
    }

    [Fact]
    public async Task Anonymous_visitors_can_neither_upload_nor_read_files()
    {
        var (owner, _) = await SignedInCustomerAsync();
        var id = await UploadAsync(owner);
        var (anonymous, _) = await app.CreateBrowserAsync();

        using var upload = await anonymous.PostAsync(Upload, TestImages.Form(TestImages.Jpeg()), Ct);
        using var file = await anonymous.GetAsync(new Uri($"/api/files/{id}", UriKind.Relative), Ct);

        upload.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        file.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Another_customer_cannot_read_someone_else_s_image()
    {
        var (owner, _) = await SignedInCustomerAsync();
        var id = await UploadAsync(owner);
        var (other, _) = await SignedInCustomerAsync();

        foreach (var path in new[] { $"/api/files/{id}", $"/api/files/{id}/thumbnail" })
        {
            using var response = await other.GetAsync(new Uri(path, UriKind.Relative), Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await AuthApp.ProblemCodeAsync(response)).ShouldBe("files.not_found");
        }

        using var missing = await other.GetAsync(new Uri($"/api/files/{Guid.NewGuid()}", UriKind.Relative), Ct);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Admins_read_images_only_after_the_second_factor()
    {
        var (customer, _) = await SignedInCustomerAsync();
        var id = await UploadAsync(customer);
        var (admin, _) = await app.CreateBrowserAsync();
        var adminEmail = app.NewAdminEmail();
        await app.RegisterConfirmedAsync(admin, adminEmail);
        (await AuthApp.LoginAsync(admin, adminEmail)).EnsureSuccessStatusCode();
        var path = new Uri($"/api/files/{id}", UriKind.Relative);

        using (var before = await admin.GetAsync(path, Ct))
        {
            before.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        await AuthApp.EnableTwoFactorAsync(admin);
        using var after = await admin.GetAsync(path, Ct);
        after.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_customer_reads_the_studio_s_images_in_their_conversation_but_not_in_others()
    {
        var (customer, customerId) = await SignedInCustomerAsync();
        var (stranger, _) = await SignedInCustomerAsync();
        var (admin, adminId) = await SignedInCustomerAsync();
        var imageId = await UploadAsync(admin);

        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var image = await db.Attachments.SingleAsync(row => row.Id == imageId, Ct);
            var conversation = Conversation.Start(customerId, DateTimeOffset.UtcNow);
            db.Conversations.Add(conversation);
            db.Messages.Add(conversation.PostText(ChatSide.Studio, adminId, "Like this?", [image], DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(Ct);
        }

        using var mine = await customer.GetAsync(new Uri($"/api/files/{imageId}", UriKind.Relative), Ct);
        using var notMine = await stranger.GetAsync(new Uri($"/api/files/{imageId}", UriKind.Relative), Ct);

        mine.StatusCode.ShouldBe(HttpStatusCode.OK);
        notMine.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Uploads_that_are_never_sent_are_deleted_after_a_day_with_their_blobs()
    {
        var (client, _) = await SignedInCustomerAsync();
        var id = await UploadAsync(client);

        await using var scope = app.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var files = scope.ServiceProvider.GetRequiredService<IFileStore>();
        var row = await db.Attachments.AsNoTracking().SingleAsync(attachment => attachment.Id == id, Ct);
        var clock = new FakeTimeProvider(row.CreatedAt.AddHours(23));
        var cleanup = new Attachments(db, files, clock, NullLogger<Attachments>.Instance);

        (await cleanup.DeleteAbandonedAsync(1000, Ct)).ShouldBe(0);

        clock.Advance(TimeSpan.FromHours(1));
        (await cleanup.DeleteAbandonedAsync(1000, Ct)).ShouldBeGreaterThanOrEqualTo(1);

        (await db.Attachments.AnyAsync(attachment => attachment.Id == id, Ct)).ShouldBeFalse();
        (await files.OpenReadAsync(row.Container, row.BlobName, Ct)).ShouldBeNull();
        (await files.OpenReadAsync(row.Container, row.ThumbnailBlobName, Ct)).ShouldBeNull();
    }

    [Fact]
    public async Task Uploads_are_rate_limited_per_user()
    {
        await using var limited = app.Factory.WithWebHostBuilder(builder => builder.UseSetting("RateLimiting:UploadsPerHour", "2"));
        var browser = new BrowserHandler();
        using var client = limited.CreateDefaultClient(BrowserHandler.BaseAddress, browser);
        (await client.GetAsync(new Uri("/api/auth/me", UriKind.Relative), Ct)).EnsureSuccessStatusCode();
        var email = AuthApp.NewEmail();
        await app.RegisterConfirmedAsync(client, email);
        (await AuthApp.LoginAsync(client, email)).EnsureSuccessStatusCode();

        for (var i = 0; i < 2; i++)
        {
            using var accepted = await client.PostAsync(Upload, TestImages.Form(TestImages.Jpeg()), Ct);
            accepted.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        using var refused = await client.PostAsync(Upload, TestImages.Form(TestImages.Jpeg()), Ct);
        refused.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        refused.Headers.RetryAfter.ShouldNotBeNull();
    }

    [Fact]
    public async Task The_export_contains_the_conversation_and_deleting_the_account_erases_it_with_the_images()
    {
        var (client, customerId) = await SignedInCustomerAsync();
        var sentId = await UploadAsync(client);
        var unsentId = await UploadAsync(client);
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var image = await db.Attachments.SingleAsync(row => row.Id == sentId, Ct);
            var conversation = Conversation.Start(customerId, DateTimeOffset.UtcNow);
            db.Conversations.Add(conversation);
            db.Messages.Add(conversation.PostText(ChatSide.Customer, customerId, "A blue bunny", [image], DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(Ct);
        }

        using (var export = await client.GetAsync(new Uri("/api/account/export", UriKind.Relative), Ct))
        {
            var message = (await export.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("conversation").GetProperty("messages")[0];
            message.GetProperty("body").GetString().ShouldBe("A blue bunny");
            message.GetProperty("from").GetString().ShouldBe("you");
            message.GetProperty("images").EnumerateArray().Select(item => item.GetString()).ShouldBe([$"/api/files/{sentId}"]);
        }

        List<Attachment> rows;
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            rows = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Attachments.AsNoTracking()
                .Where(row => row.Id == sentId || row.Id == unsentId).ToListAsync(Ct);
        }

        using var deleted = await AuthApp.PostAsync(client, "/api/account/delete", new { password = AuthApp.Password });
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var files = scope.ServiceProvider.GetRequiredService<IFileStore>();
            (await db.Conversations.AnyAsync(row => row.CustomerId == customerId, Ct)).ShouldBeFalse();
            (await db.Attachments.AnyAsync(row => row.Id == sentId || row.Id == unsentId, Ct)).ShouldBeFalse();
            rows.Count.ShouldBe(2);
            foreach (var row in rows)
            {
                (await files.OpenReadAsync(row.Container, row.BlobName, Ct)).ShouldBeNull();
                (await files.OpenReadAsync(row.Container, row.ThumbnailBlobName, Ct)).ShouldBeNull();
            }
        }
    }

    private async Task<(HttpClient Client, Guid UserId)> SignedInCustomerAsync()
    {
        var (client, _) = await app.CreateBrowserAsync();
        var email = AuthApp.NewEmail();
        var userId = await app.RegisterConfirmedAsync(client, email);
        (await AuthApp.LoginAsync(client, email)).EnsureSuccessStatusCode();
        return (client, userId);
    }

    private static async Task<Guid> UploadAsync(HttpClient client)
    {
        using var response = await client.PostAsync(Upload, TestImages.Form(TestImages.Jpeg()), Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }
}
