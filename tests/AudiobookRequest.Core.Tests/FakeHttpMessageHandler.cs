using System.Net;
using System.Text;

namespace AudiobookRequest.Core.Tests;

/// <summary>Records every request and returns canned responses, for testing HTTP clients without a real server.</summary>
public class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body)> _responses;
    private readonly (HttpStatusCode Status, string Body) _default;

    public FakeHttpMessageHandler(HttpStatusCode statusCode, string responseBody)
    {
        _default = (statusCode, responseBody);
        _responses = new Queue<(HttpStatusCode, string)>();
    }

    /// <summary>Returns each response in order, one per request; repeats the last one once exhausted.</summary>
    public FakeHttpMessageHandler(params (HttpStatusCode Status, string Body)[] responses)
    {
        _responses = new Queue<(HttpStatusCode, string)>(responses);
        _default = responses[^1];
    }

    public List<HttpRequestMessage> Requests { get; } = [];
    public HttpRequestMessage? LastRequest => Requests.Count > 0 ? Requests[^1] : null;
    public List<string?> RequestBodies { get; } = [];
    public string? LastRequestBody => RequestBodies.Count > 0 ? RequestBodies[^1] : null;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));

        var (status, body) = _responses.Count > 0 ? _responses.Dequeue() : _default;
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}
