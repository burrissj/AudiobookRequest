using System.Security.Claims;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;

namespace AudiobookRequest.Api.Services;

/// <summary>
/// What goes in the sign-in cookie. The person's own Jellyfin token, user id and the device it was issued to ride
/// along (the cookie is encrypted) so the in-app player can act as them; sessions from before that was added have
/// none and need one fresh sign-in to listen.
/// </summary>
public static class SessionClaims
{
    public const string JellyfinToken = "abr:jf_token";
    public const string JellyfinUserId = "abr:jf_user";
    public const string JellyfinDeviceId = "abr:jf_device";
    public const string JellyfinDeviceName = "abr:jf_device_name";

    public static List<Claim> Build(AppUser user, JellyfinAuthResult auth, JellyfinDevice device) =>
    [
        new(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new(ClaimTypes.Name, user.Username),
        new(ClaimTypes.Role, user.Role.ToString()),
        new(JellyfinToken, auth.AccessToken),
        new(JellyfinUserId, auth.User.Id),
        new(JellyfinDeviceId, device.Id),
        new(JellyfinDeviceName, device.Name),
    ];

    /// <summary>The signed-in person's Jellyfin session, or null for a session that predates the player.</summary>
    public static JellyfinSession? JellyfinSessionOf(ClaimsPrincipal principal)
    {
        var token = principal.FindFirstValue(JellyfinToken);
        var userId = principal.FindFirstValue(JellyfinUserId);
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        // 0.9.0 sessions have no device claims; they fall back to the original shared defaults.
        var deviceId = principal.FindFirstValue(JellyfinDeviceId);
        var deviceName = principal.FindFirstValue(JellyfinDeviceName);
        return string.IsNullOrWhiteSpace(deviceId)
            ? new JellyfinSession(token, userId)
            : new JellyfinSession(token, userId, deviceId, string.IsNullOrWhiteSpace(deviceName) ? "Web Player" : deviceName);
    }
}
