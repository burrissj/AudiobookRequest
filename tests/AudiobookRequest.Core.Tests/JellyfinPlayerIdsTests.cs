using System.Net;
using AudiobookRequest.Core.Clients;

namespace AudiobookRequest.Core.Tests;

public class JellyfinPlayerIdsTests
{
    [Fact]
    public async Task GetItems_WithIds_AsksForExactlyThoseBooks()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"Items":[],"TotalRecordCount":0}""");
        var client = new JellyfinPlayerClient(new HttpClient(handler));

        await client.GetItemsAsync("http://jf", new JellyfinSession("t", "u"), new JellyfinItemQuery(Ids: ["abc", "d e"], Limit: 2));

        var url = handler.LastRequest!.RequestUri!.AbsoluteUri;
        Assert.Contains("&Ids=abc,d%20e", url);
        Assert.Contains("Limit=2", url);
    }

    [Fact]
    public async Task GetItems_WithoutIds_SendsNone()
    {
        var handler = new FakeHttpMessageHandler(HttpStatusCode.OK, """{"Items":[],"TotalRecordCount":0}""");
        var client = new JellyfinPlayerClient(new HttpClient(handler));

        await client.GetItemsAsync("http://jf", new JellyfinSession("t", "u"), new JellyfinItemQuery());

        Assert.DoesNotContain("Ids=", handler.LastRequest!.RequestUri!.AbsoluteUri);
    }
}
