using System.Net;
using BajkoweCuda.Api.IntegrationTests.Database;

namespace BajkoweCuda.Api.IntegrationTests.Auth;

/// <summary>Oversized requests are refused as the client's error, not as a server failure (docs/PLAN.md §8.2).</summary>
[Trait("Category", SqlServerFixture.Category)]
public sealed class RequestLimitTests(AuthApp app) : IClassFixture<AuthApp>
{
    [Fact]
    public async Task A_json_body_over_one_megabyte_gets_413_with_a_code()
    {
        var (client, _) = await app.CreateBrowserAsync();
        using (client)
        {
            // A browser sends Content-Length; JsonContent would stream without it.
            using var body = new StringContent($"{{\"email\":\"a@example.test\",\"password\":\"{new string('x', 2_000_000)}\"}}", System.Text.Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(new Uri("/api/auth/login", UriKind.Relative), body, TestContext.Current.CancellationToken);

            response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
            (await AuthApp.ProblemCodeAsync(response)).ShouldBe("request_too_large");
        }
    }

    [Fact]
    public async Task Uploads_keep_their_own_larger_limit()
    {
        var (client, _) = await app.CreateBrowserAsync();
        using (client)
        {
            using var twoMegabytes = await UploadAsync(client, 2_000_000);
            using var overTheUploadLimit = await UploadAsync(client, 12_000_000);

            // Not refused for its size: the upload goes on to the sign-in check.
            twoMegabytes.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            overTheUploadLimit.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        }
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, int bytes)
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(new byte[bytes]), "file", "photo.jpg" } };
        // Computes Content-Length now, as a browser sends it.
        form.Headers.ContentLength.ShouldNotBeNull();
        return client.PostAsync(new Uri("/api/chat/attachments", UriKind.Relative), form, TestContext.Current.CancellationToken);
    }
}
