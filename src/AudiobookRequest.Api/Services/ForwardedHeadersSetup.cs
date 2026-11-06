using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace AudiobookRequest.Api.Services;

/// <summary>
/// Decides which reverse proxies are believed when they say "the browser actually used HTTPS". A proxy such as
/// Nginx Proxy Manager terminates TLS and talks plain HTTP to this app, so without this the app thinks every
/// request is insecure and issues its sign-in cookie without the Secure flag.
///
/// Proxies on private networks (10.x, 172.16-31.x, 192.168.x, loopback, IPv6 unique-local) are trusted by
/// default: on a home network that is where a proxy lives, and a request that reaches the app from a public
/// address -- someone port-forwarded straight at it -- is never believed. What a believed proxy can change is
/// only the scheme/host/IP of that request, and the app uses none of them for decisions beyond the cookie flag.
/// </summary>
public static class ForwardedHeadersSetup
{
    private static readonly string[] PrivateNetworks = ["10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7"];

    public static ForwardedHeadersOptions Create(bool trustAnyProxy)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
        };

        if (trustAnyProxy)
        {
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            return options;
        }

        // Loopback is already trusted by the options' own defaults; add the private ranges alongside it.
        foreach (var network in PrivateNetworks)
        {
            options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        }
        return options;
    }
}
