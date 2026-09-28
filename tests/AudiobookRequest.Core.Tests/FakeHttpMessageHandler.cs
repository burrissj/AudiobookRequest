using System.Net;
using System.Text;

namespace AudiobookRequest.Core.Tests;

/// <summary>Records the last request and returns a canned response, for testing HTTP clients without a real server.</summary>
public class FakeHttpMessageHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        };
        return response;
    }
}
