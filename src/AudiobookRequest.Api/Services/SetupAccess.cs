using System.Net;

namespace AudiobookRequest.Api.Services;

/// <summary>
/// Who may use the setup endpoints. Before setup is finished there is nothing to protect yet and no admin to sign in as,
/// so the page is open -- but only to someone on the home network (or the VPN into it), so a stranger on the internet
/// can't claim a fresh install by pointing it at their own Jellyfin. Once setup is done, only a signed-in admin may
/// change it or use its "test this address" buttons, which make the server connect to whatever address they are given.
/// </summary>
public static class SetupAccess
{
    // Private and loopback ranges, plus 100.64.0.0/10 (what Tailscale hands out) and link-local.
    private static readonly IPNetwork[] HomeNetworks =
    [
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.168.0.0/16"),
        IPNetwork.Parse("169.254.0.0/16"),
        IPNetwork.Parse("100.64.0.0/10"),
        IPNetwork.Parse("fc00::/7"),
        IPNetwork.Parse("fe80::/10"),
    ];

    public static bool IsHomeNetwork(IPAddress? address)
    {
        if (address is null)
        {
            return false;
        }
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        return IPAddress.IsLoopback(address) || HomeNetworks.Any(n => n.Contains(address));
    }

    /// <returns>null when the request may go ahead, otherwise what to tell the person.</returns>
    public static string? Denial(bool setupCompleted, bool isAdmin, IPAddress? address)
    {
        if (setupCompleted)
        {
            return isAdmin ? null : "Setup has already been completed. Sign in as an admin to change it.";
        }
        return IsHomeNetwork(address)
            ? null
            : "First-time setup can only be done from your home network, or over your VPN.";
    }
}
