using System.Net;
using System.Text;

namespace ShellyHwinfoBridge.Core.Tests.TestSupport;

/// <summary>Returns canned responses keyed by path-and-query; unknown paths return 404.</summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _responses = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Requests { get; } = [];

    public StubHttpHandler Add(string pathAndQuery, string body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _responses[pathAndQuery] = (status, body);
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var key = request.RequestUri!.PathAndQuery;
        Requests.Add(key);
        var response = _responses.TryGetValue(key, out var r)
            ? new HttpResponseMessage(r.Status) { Content = new StringContent(r.Body, Encoding.UTF8, "application/json") }
            : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("Not Found") };
        return Task.FromResult(response);
    }
}
