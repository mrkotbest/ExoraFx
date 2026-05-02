using System.Net;
using System.Text;

namespace ExoraFx.Api.Tests.Providers;

public sealed class StubHttpMessageHandler(Dictionary<string, string> responsesBySubstring, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
{
    public List<string> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.PathAndQuery;
        Requests.Add(path);

        foreach (var (substring, json) in responsesBySubstring)
        {
            if (path.Contains(substring, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                });
            }
        }

        throw new InvalidOperationException($"No stubbed response for path: {path}");
    }
}
