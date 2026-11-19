using Microsoft.AspNetCore.DataProtection;
using WooPulse.Api.Integrations.WooCommerce;
using WooPulse.Api.Services;

namespace WooPulse.Api.Setup;

/// <summary>
/// What: everything the WooCommerce integration needs: encryption for API keys, the HTTP
///       client that calls shops, and the connection service.
/// </summary>
public static class WooCommerceSetup
{
    public static IServiceCollection AddWooPulseWooCommerce(this IServiceCollection services)
    {
        // DATA PROTECTION (encrypts the stored API keys).
        // SetApplicationName: keys are tied to "WooPulse", so moving the project folder
        // doesn't make existing secrets unreadable.
        // Where the keys live (Windows default, running as your user):
        //   %LOCALAPPDATA%\ASP.NET\DataProtection-Keys
        // and they are themselves encrypted with Windows DPAPI (tied to your Windows account).
        // If that folder is deleted, saved API keys can't be decrypted; the owner just
        // re-enters them (ConnectionService returns a clear "Saved keys unreadable" error).
        services.AddDataProtection().SetApplicationName("WooPulse");

        // TYPED HTTP CLIENT: IHttpClientFactory creates WooCommerceClient with a managed HttpClient.
        services.AddHttpClient<WooCommerceClient>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                // Some hosts/security plugins block requests with no User-Agent.
                client.DefaultRequestHeaders.UserAgent.ParseAdd("WooPulse/0.1");
            })
            // SECURITY: HttpClientFactory logs every request URL at "Information" level by
            // default. In query-string auth mode the URL contains consumer_secret, so it would
            // end up in the console/logs. RemoveAllLoggers() turns that logging off for this client.
            .RemoveAllLoggers();

        services.AddScoped<ConnectionService>();
        return services;
    }
}
