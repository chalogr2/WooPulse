using System.Net;
using WooPulse.Api.Data;
using WooPulse.Api.Integrations.WooCommerce;
using WooPulse.Api.Tests.Infrastructure;

namespace WooPulse.Api.Tests;

/// <summary>The WooCommerce REST client: auth fallback, error messages, order parsing.</summary>
public sealed class WooCommerceClientTests
{
    private const string Shop = "https://shop.example.test";
    private const string Key = "ck_1234567890abcdef";
    private const string Secret = "cs_s3cr3t&with=special";

    private static (WooCommerceClient Client, FakeWooHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new FakeWooHandler(respond);
        return (new WooCommerceClient(new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task Test_SendsKeysInTheAuthorizationHeader_NotTheUrl()
    {
        var (client, handler) = Create(_ => FakeWooHandler.Ok("[]", ("X-WP-Total", "5988")));

        var result = await client.TestAsync(Shop, Key, Secret);

        Assert.True(result.Ok);
        Assert.Equal(WooAuthMode.Header, result.AuthMode);
        Assert.Equal(5988, result.OrderCount);
        Assert.Contains("5,988 orders found", result.Message);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("Basic", request.Headers.Authorization!.Scheme);
        Assert.DoesNotContain("consumer_secret", request.RequestUri!.Query);
        Assert.StartsWith($"{Shop}/wp-json/wc/v3/orders?", request.RequestUri.AbsoluteUri);
    }

    [Fact]
    public async Task Test_FallsBackToQueryStringAuth_WhenTheServerStripsTheHeader()
    {
        // Typical Apache setup: the Authorization header never reaches PHP, so WooCommerce says 401.
        var (client, handler) = Create(req => req.Headers.Authorization is null
            ? FakeWooHandler.Ok("[]", ("X-WP-Total", "3"))
            : FakeWooHandler.Unauthorized());

        var result = await client.TestAsync(Shop, Key, Secret);

        Assert.True(result.Ok);
        Assert.Equal(WooAuthMode.QueryString, result.AuthMode);
        Assert.Equal(2, handler.Requests.Count);

        // The secret contains & and =; it must be escaped so it can't break the query string.
        var query = handler.Requests[1].RequestUri!.Query;
        Assert.Contains("consumer_secret=cs_s3cr3t%26with%3Dspecial", query);
    }

    [Fact]
    public async Task Test_WrongKeys_FailsWithAReadableMessage_ThatNeverContainsTheSecret()
    {
        var (client, handler) = Create(_ => FakeWooHandler.Unauthorized());

        var result = await client.TestAsync(Shop, Key, Secret);

        Assert.False(result.Ok);
        Assert.Null(result.AuthMode);
        Assert.Equal(2, handler.Requests.Count);   // header, then query string
        Assert.Contains("rejected the API key", result.Message);
        Assert.Contains("HTTP 401", result.Message);
        Assert.DoesNotContain(Secret, result.Message);
        Assert.DoesNotContain(Key, result.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "\"Read\" permission")]
    [InlineData(HttpStatusCode.NotFound, "REST API not found")]
    [InlineData(HttpStatusCode.BadGateway, "server error")]
    public async Task Test_OtherFailures_ExplainWhatToFix(HttpStatusCode status, string hint)
    {
        var (client, handler) = Create(_ => FakeWooHandler.Json(status, "<html>not json</html>"));

        var result = await client.TestAsync(Shop, Key, Secret);

        Assert.False(result.Ok);
        Assert.Contains(hint, result.Message);
        Assert.Single(handler.Requests);   // only a 401 triggers the query-string retry
    }

    [Fact]
    public async Task Test_UnreachableShop_IsReported_NotThrown()
    {
        var (client, _) = Create(_ => throw new HttpRequestException("No such host is known."));

        var result = await client.TestAsync(Shop, Key, Secret);

        Assert.False(result.Ok);
        Assert.StartsWith("Couldn't reach the shop", result.Message);
    }

    [Fact]
    public async Task GetOrdersPage_ParsesSnakeCaseJson_AndMoneySentAsStrings()
    {
        const string json = """
        [{
          "id": 1001, "number": "1001", "status": "completed", "currency": "USD",
          "total": "123.45", "total_tax": "13.45", "shipping_total": "5.00", "discount_total": "0.00",
          "date_created": "2026-11-01T22:15:00", "date_created_gmt": "2026-11-02T04:15:00",
          "line_items": [{ "id": 1, "name": "Tom &amp; Jerry", "product_id": 77, "variation_id": 0, "quantity": 2, "total": "105.00" }]
        }]
        """;
        var (client, handler) = Create(_ => FakeWooHandler.Ok(json, ("X-WP-TotalPages", "7")));
        var creds = new WooCredentials(Shop, Key, Secret, WooAuthMode.Header);

        var (orders, pages) = await client.GetOrdersPageAsync(creds, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), 2, default);

        Assert.Equal(7, pages);
        var order = Assert.Single(orders);
        Assert.Equal((1001L, "completed", 123.45m, 13.45m, 5.00m), (order.Id, order.Status, order.Total, order.TotalTax, order.ShippingTotal));
        Assert.Equal(new DateTime(2026, 11, 1, 22, 15, 0), order.DateCreated);
        var item = Assert.Single(order.LineItems);
        Assert.Equal((77L, 2, 105.00m), (item.ProductId, item.Quantity, item.Total));

        var query = handler.Requests.Single().RequestUri!.Query;
        Assert.Contains("per_page=100", query);
        Assert.Contains("page=2", query);
        Assert.Contains("order=asc", query);
        Assert.Contains("after=2026-10-01T00%3A00%3A00", query);
    }

    [Fact]
    public async Task GetOrdersPage_ErrorResponse_ThrowsWooApiExceptionWithTheShopsMessage()
    {
        var (client, _) = Create(_ => FakeWooHandler.Unauthorized());
        var creds = new WooCredentials(Shop, Key, Secret, WooAuthMode.Header);

        var ex = await Assert.ThrowsAsync<WooApiException>(() => client.GetOrdersPageAsync(creds, DateTimeOffset.UtcNow, 1, default));

        Assert.Contains("Sorry, you cannot list resources.", ex.Message);
    }
}
