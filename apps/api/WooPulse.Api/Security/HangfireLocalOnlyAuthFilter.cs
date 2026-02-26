using Hangfire.Dashboard;

namespace WooPulse.Api.Security;

public class HangfireLocalOnlyAuthFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var http = context.GetHttpContext();

        // allow only local machine (loopback)
        return http.Connection.RemoteIpAddress is not null
               && (System.Net.IPAddress.IsLoopback(http.Connection.RemoteIpAddress));
    }
}