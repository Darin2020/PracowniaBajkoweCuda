using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace BajkoweCuda.Api.IntegrationTests.PublicSite;

/// <summary>
/// The image is built once and promoted through every environment, so absolute URLs are written at build time
/// with a placeholder origin and replaced when the app serves the files.
/// </summary>
public sealed class PublicOriginTests(SiteFactory factory) : IClassFixture<SiteFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<HttpResponseMessage> GetAsync(HttpClient client, string path) =>
        await client.GetAsync(new Uri(path, UriKind.Relative), Ct);

    [Theory]
    [InlineData("/en/")]
    [InlineData("/pl/privacy")]
    [InlineData("/robots.txt")]
    [InlineData("/sitemap.xml")]
    public async Task Served_text_files_carry_the_configured_origin_instead_of_the_placeholder(string path)
    {
        using var client = factory.CreateSiteClient();
        using var response = await GetAsync(client, path);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(Ct);
        body.ShouldContain(SiteFactory.PublicOrigin);
        body.ShouldNotContain(SiteFactory.OriginPlaceholder);
    }

    [Fact]
    public async Task Rewritten_page_is_complete_valid_utf8_with_a_matching_content_length()
    {
        using var client = factory.CreateSiteClient();
        using var response = await GetAsync(client, "/pl/");

        var bytes = await response.Content.ReadAsByteArrayAsync(Ct);
        var expected = SiteFactory.Landing("pl");

        Encoding.UTF8.GetString(bytes).ShouldBe(expected);
        response.Content.Headers.ContentLength.ShouldBe(Encoding.UTF8.GetByteCount(expected));
        // No byte-order mark in front of <!doctype html>.
        bytes[0].ShouldBe((byte)'<');
    }

    [Fact]
    public async Task Rewritten_page_supports_conditional_requests()
    {
        using var client = factory.CreateSiteClient();
        using var first = await GetAsync(client, "/en/");
        var etag = first.Headers.ETag.ShouldNotBeNull();

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/en/", UriKind.Relative));
        request.Headers.IfNoneMatch.Add(etag);
        using var second = await client.SendAsync(request, Ct);

        second.StatusCode.ShouldBe(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task Scripts_and_other_assets_are_not_rewritten()
    {
        using var client = factory.CreateSiteClient();
        using var response = await GetAsync(client, "/en/main-KRBIKYHT.js");

        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain(SiteFactory.OriginPlaceholder);
    }

    // ---- Seo:NoIndex (staging) -------------------------------------------------------------

    [Fact]
    public async Task Indexable_environment_sends_no_robots_header_and_serves_the_built_robots_file()
    {
        using var client = factory.CreateSiteClient();
        using var page = await GetAsync(client, "/en/");
        using var robots = await GetAsync(client, "/robots.txt");

        page.Headers.Contains("X-Robots-Tag").ShouldBeFalse();
        (await robots.Content.ReadAsStringAsync(Ct)).ShouldBe(SiteFactory.Robots());
    }

    [Theory]
    [InlineData("/en/")]
    [InlineData("/en/chat")]
    [InlineData("/en/main-KRBIKYHT.js")]
    [InlineData("/")]
    [InlineData("/api/version")]
    public async Task NoIndex_environment_marks_every_response(string path)
    {
        using var client = factory.CreateSiteClient(("Seo:NoIndex", "true"));
        using var response = await GetAsync(client, path);

        response.Headers.GetValues("X-Robots-Tag").ShouldHaveSingleItem().ShouldBe("noindex, nofollow");
    }

    [Fact]
    public async Task NoIndex_environment_disallows_everything_in_robots_txt()
    {
        using var client = factory.CreateSiteClient(("Seo:NoIndex", "true"));
        using var response = await GetAsync(client, "/robots.txt");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/plain");
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe("User-agent: *\nDisallow: /\n");
    }

    // ---- configuration validation ----------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("example.com")]
    [InlineData("/relative")]
    [InlineData("ftp://example.com")]
    [InlineData("https://example.com/shop")]
    [InlineData("https://example.com/?a=1")]
    [InlineData("https://user:secret@example.com")]
    public void App_refuses_to_start_with_an_invalid_public_origin(string value)
    {
        using var broken = factory.WithWebHostBuilder(builder => builder.UseSetting("PublicBaseUrl", value));

        var exception = Should.Throw<InvalidOperationException>(() => broken.CreateClient());

        exception.Message.ShouldContain("PublicBaseUrl");
    }

    [Theory]
    [InlineData("https://example.com/", "https://example.com/en/")]
    [InlineData("HTTPS://Example.COM", "https://example.com/en/")]
    [InlineData("http://localhost:8080", "http://localhost:8080/en/")]
    public async Task Public_origin_is_normalized(string configured, string expectedCanonical)
    {
        using var client = factory.CreateSiteClient(("PublicBaseUrl", configured));
        using var response = await GetAsync(client, "/en/");

        (await response.Content.ReadAsStringAsync(Ct)).ShouldContain($"rel=\"canonical\" href=\"{expectedCanonical}\"");
    }

    // ---- /api/version ----------------------------------------------------------------------

    [Fact]
    public async Task Version_endpoint_reports_the_build_and_is_never_cached()
    {
        using var client = factory.CreateSiteClient();
        using var response = await GetAsync(client, "/api/version");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();

        var body = await response.Content.ReadFromJsonAsync<VersionBody>(Ct);
        body.ShouldNotBeNull();
        body.Version.ShouldBe(SiteFactory.AppVersion);
        body.Environment.ShouldNotBeNullOrWhiteSpace();
    }

    private sealed record VersionBody(string Version, string Environment);
}
