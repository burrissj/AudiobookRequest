using System.Text.RegularExpressions;

namespace AudiobookRequest.Api.Services;

/// <summary>
/// Gives each browser a stable id and a readable name for Jellyfin's Devices list ("Safari on iPhone"), so the
/// admin can tell the app's sign-ins apart. Purely cosmetic: a wrong guess changes a label, nothing else.
/// </summary>
public static partial class DeviceNames
{
    public const string CookieName = "abr_device";

    [GeneratedRegex("^abr-[0-9a-f]{24}$")]
    private static partial Regex IdPattern();

    public static bool IsValidId(string? id) => id is not null && IdPattern().IsMatch(id);

    public static string NewId() => "abr-" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();

    public static string FromUserAgent(string? userAgent)
    {
        var ua = userAgent ?? "";
        var os = ua.Contains("iPhone") ? "iPhone"
            : ua.Contains("iPad") ? "iPad"
            : ua.Contains("Android") ? "Android"
            : ua.Contains("Windows") ? "Windows"
            : ua.Contains("Macintosh") || ua.Contains("Mac OS X") ? "Mac"
            : ua.Contains("CrOS") ? "ChromeOS"
            : ua.Contains("Linux") ? "Linux"
            : null;

        // Order matters: Edge and Chrome both say "Safari"; Chrome on iOS says "CriOS".
        var browser = ua.Contains("Edg/") || ua.Contains("EdgA/") || ua.Contains("EdgiOS/") ? "Edge"
            : ua.Contains("CriOS/") || ua.Contains("Chrome/") ? "Chrome"
            : ua.Contains("FxiOS/") || ua.Contains("Firefox/") ? "Firefox"
            : ua.Contains("Safari/") ? "Safari"
            : null;

        return (browser, os) switch
        {
            (not null, not null) => $"{browser} on {os}",
            (not null, null) => browser,
            (null, not null) => os,
            _ => "Web Player",
        };
    }
}
