namespace AudiobookRequest.Core.Clients;

/// <summary>The client-identification "Authorization" header Jellyfin expects, built safely from user-influenced values.</summary>
public static class JellyfinHeaders
{
    public static string ForLogin(JellyfinDevice device) =>
        $"MediaBrowser Client=\"AudiobookRequest\", Device=\"{Clean(device.Name, 60, "Web Player")}\", DeviceId=\"{Clean(device.Id, 80, "audiobookrequest-web")}\", Version=\"1.0.0\"";

    public static string ForSession(JellyfinSession session) =>
        $"{ForLogin(new JellyfinDevice(session.DeviceId, session.DeviceName))}, Token=\"{session.Token}\"";

    // Values go inside quoted header fields: quotes, commas and control characters would break (or inject into) the header.
    internal static string Clean(string? value, int maxLength, string fallback)
    {
        var chars = (value ?? "").Where(c => !char.IsControl(c) && c is not ('"' or ',' or '\\')).ToArray();
        var cleaned = new string(chars).Trim();
        if (cleaned.Length > maxLength)
        {
            cleaned = cleaned[..maxLength].Trim();
        }
        return cleaned.Length == 0 ? fallback : cleaned;
    }
}
