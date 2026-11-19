using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WooPulse.Api.Data;

namespace WooPulse.Api.Integrations.WooCommerce;

/// <summary>Plain (decrypted) credentials, only kept in memory while a request runs.</summary>
public record WooCredentials(string ShopUrl, string ConsumerKey, string ConsumerSecret, WooAuthMode AuthMode);

/// <summary>Result of a connection test. Message is always safe to show to the user.</summary>
public record WooTestResult(bool Ok, WooAuthMode? AuthMode, int? OrderCount, string Message);

/// <summary>
/// What: talks to a WooCommerce shop's REST API (wc/v3, read-only).
/// Why its own folder (Integrations/): external systems are a separate concern from our
///      domain. If WooCommerce's API changes, only this folder changes.
///
/// HttpClient: injected by IHttpClientFactory ("typed client", registered in
/// Setup/WooCommerceSetup.cs). The factory reuses network connections between requests;
/// creating "new HttpClient()" per call can exhaust sockets under load.
/// </summary>
public class WooCommerceClient
{
    private readonly HttpClient _http;

    public WooCommerceClient(HttpClient http) => _http = http;

    /// <summary>
    /// Tries the credentials by asking for 1 order. WooCommerce returns the total order count
    /// in the "X-WP-Total" response header, so we can show "1,234 orders found".
    /// Tries the Authorization header first, then falls back to query-string auth.
    /// </summary>
    public async Task<WooTestResult> TestAsync(string shopUrl, string key, string secret, CancellationToken ct = default)
    {
        // "new[] { ... }" = implicitly typed array. We loop over the two auth modes in order.
        foreach (var mode in new[] { WooAuthMode.Header, WooAuthMode.QueryString })
        {
            var creds = new WooCredentials(shopUrl, key, secret, mode);

            HttpResponseMessage res;
            try
            {
                res = await SendAsync(creds, "orders", new() { ["per_page"] = "1" }, ct);
            }
            catch (HttpRequestException ex)
            {
                // DNS failure, refused connection, bad TLS certificate...
                return new(false, null, null, $"Couldn't reach the shop: {ex.Message}");
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                // "when (...)" = exception FILTER: only catch it if WE didn't cancel, i.e. HttpClient's timeout fired.
                return new(false, null, null, "The shop took too long to respond (timeout).");
            }

            // "using" disposes the response (frees the connection) when this block ends.
            using (res)
            {
                if (res.IsSuccessStatusCode)
                {
                    int? total = res.Headers.TryGetValues("X-WP-Total", out var values)
                                 && int.TryParse(values.FirstOrDefault(), out var n) ? n : null;

                    var how = mode == WooAuthMode.Header
                        ? "Connected."
                        : "Connected (the server strips the Authorization header, so keys are sent as URL parameters over HTTPS).";
                    var count = total is null ? "" : $" {total:N0} orders found.";
                    return new(true, mode, total, how + count);
                }

                // 401 with the header can mean "server stripped the header". Try the fallback.
                if (res.StatusCode == HttpStatusCode.Unauthorized && mode == WooAuthMode.Header)
                    continue;

                return new(false, null, null, await DescribeErrorAsync(res, shopUrl, ct));
            }
        }

        return new(false, null, null, "Authentication failed."); // the loop always returns; the compiler needs this
    }

    /// <summary>
    /// Builds and sends one GET request to {shop}/wp-json/wc/v3/{path}?{query}.
    /// Used by TestAsync now, and by the order sync in Step 2B.
    /// </summary>
    internal async Task<HttpResponseMessage> SendAsync(
        WooCredentials creds, string path, Dictionary<string, string> query, CancellationToken ct)
    {
        // Copy the query so we don't modify the caller's dictionary.
        var q = new Dictionary<string, string>(query);
        if (creds.AuthMode == WooAuthMode.QueryString)
        {
            q["consumer_key"] = creds.ConsumerKey;
            q["consumer_secret"] = creds.ConsumerSecret;
        }

        // Uri.EscapeDataString encodes special characters so values can't break the URL.
        var qs = string.Join("&", q.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        var url = $"{creds.ShopUrl}/wp-json/wc/v3/{path}" + (qs.Length > 0 ? "?" + qs : "");

        // A NEW request message per call: headers set here don't leak to other requests
        // (unlike _http.DefaultRequestHeaders, which is shared).
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (creds.AuthMode == WooAuthMode.Header)
        {
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{creds.ConsumerKey}:{creds.ConsumerSecret}"));
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        }

        return await _http.SendAsync(req, ct);
    }

    /// <summary>Turns a failed response into a message a person can act on.</summary>
    private static async Task<string> DescribeErrorAsync(HttpResponseMessage res, string shopUrl, CancellationToken ct)
    {
        // WooCommerce errors look like { "code": "woocommerce_rest_cannot_view", "message": "..." }
        string? wooMessage = null;
        try
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var m)) wooMessage = m.GetString();
        }
        catch (JsonException) { /* not JSON, e.g. an HTML error page from a security plugin */ }

        // switch expression with "when" guards: the first matching arm wins.
        var hint = (int)res.StatusCode switch
        {
            401 => "The shop rejected the API key. Check the key and secret.",
            403 => "The API key isn't allowed to read orders. Give it \"Read\" permission.",
            404 => $"WooCommerce REST API not found at {shopUrl}/wp-json/wc/v3. Is WooCommerce active and are WordPress permalinks enabled?",
            var s when s >= 500 => "The shop had a server error.",
            _ => "The shop returned an unexpected response."
        };

        return wooMessage is null
            ? $"{hint} (HTTP {(int)res.StatusCode})"
            : $"{hint} (HTTP {(int)res.StatusCode}: {wooMessage})";
    }
}
