using System.Net;
using System.Text;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.AspNetCore.Http;

namespace WooPulse.Api.Tests.Infrastructure;

/// <summary>Records the Hangfire jobs a service enqueues, without running them.</summary>
public sealed class FakeJobClient : IBackgroundJobClient
{
    public List<Job> Jobs { get; } = new();

    public string Create(Job job, IState state)
    {
        Jobs.Add(job);
        return Jobs.Count.ToString();
    }

    public bool ChangeState(string jobId, IState state, string expectedState) => true;
}

/// <summary>
/// Stands in for a WooCommerce shop. Each test decides the response; every request is
/// recorded so tests can check what was sent (auth header, query string).
/// </summary>
public sealed class FakeWooHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    public FakeWooHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    public List<HttpRequestMessage> Requests { get; } = new();

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(_respond(request));
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string json, params (string Name, string Value)[] headers)
    {
        var res = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        foreach (var (name, value) in headers) res.Headers.Add(name, value);
        return res;
    }

    public static HttpResponseMessage Ok(string json = "[]", params (string Name, string Value)[] headers)
        => Json(HttpStatusCode.OK, json, headers);

    public static HttpResponseMessage Unauthorized()
        => Json(HttpStatusCode.Unauthorized, """{"code":"woocommerce_rest_cannot_view","message":"Sorry, you cannot list resources."}""");
}

public static class ResultAssert
{
    /// <summary>The HTTP status an IResult would produce (NotFound → 404, Problem(...) → its status, ...).</summary>
    public static int StatusOf(IResult? result)
    {
        Assert.NotNull(result);
        var withStatus = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        return withStatus.StatusCode ?? 200;
    }

    public static void HasStatus(int expected, IResult? result) => Assert.Equal(expected, StatusOf(result));
}
