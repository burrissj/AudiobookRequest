using Microsoft.AspNetCore.Http;

namespace AudiobookRequest.Api.Services;

/// <summary>Describes how the admin's own request reached the app: HTTPS or not, and whether a reverse proxy's "this was HTTPS" was believed.</summary>
public static class ConnectionCheck
{
    public static HealthCheck Describe(HttpRequest request)
    {
        const string name = "This connection";
        var claimedByProxy = string.Equals(request.Headers["X-Forwarded-Proto"].ToString(), "https", StringComparison.OrdinalIgnoreCase);

        if (request.IsHttps)
        {
            return new HealthCheck(name, "ok", $"Loaded over HTTPS as {request.Host.Value}. Sign-in cookies are marked Secure, and the app can be installed on a phone.");
        }

        if (claimedByProxy)
        {
            // A proxy said HTTPS but the app didn't believe it, which means the proxy isn't on a private address.
            return new HealthCheck(name, "warn",
                "A reverse proxy says this page was loaded over HTTPS, but its address isn't one the app trusts, so cookies aren't marked Secure. " +
                "Set \"ForwardedHeaders\": { \"TrustAnyProxy\": true } in appsettings.Production.json if the proxy really is yours.");
        }

        return new HealthCheck(name, "info",
            $"Loaded over plain HTTP as {request.Host.Value}. That's fine on your home network. To use the app as an installed phone app, open it through your HTTPS address.");
    }
}
