using System.Net;

namespace BajkoweCuda.Api.IntegrationTests.Auth;

/// <summary>
/// Behaves like the Angular app in a browser: keeps cookies (over HTTPS, so <c>__Host-</c> cookies work) and copies
/// the <c>XSRF-TOKEN</c> cookie into the <c>X-XSRF-TOKEN</c> header of unsafe requests.
/// </summary>
public sealed class BrowserHandler : DelegatingHandler
{
    public static readonly Uri BaseAddress = new("https://localhost");

    public CookieContainer Cookies { get; } = new();

    /// <summary>Set to false to send unsafe requests without the antiforgery header.</summary>
    public bool SendAntiforgeryHeader { get; set; } = true;

    public string? Cookie(string name) => Cookies.GetCookies(BaseAddress)[name]?.Value;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var cookieHeader = Cookies.GetCookieHeader(uri);
        if (!string.IsNullOrEmpty(cookieHeader))
        {
            request.Headers.Add("Cookie", cookieHeader);
        }

        var safe = request.Method == HttpMethod.Get || request.Method == HttpMethod.Head;
        if (!safe && SendAntiforgeryHeader && Cookie("XSRF-TOKEN") is { } token)
        {
            request.Headers.Add("X-XSRF-TOKEN", token);
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var setCookie in setCookies)
            {
                Cookies.SetCookies(uri, setCookie);
            }
        }

        return response;
    }
}
