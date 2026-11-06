using Microsoft.AspNetCore.Mvc;

namespace AudiobookRequest.Api.Services;

/// <param name="ContentSecurityPolicy">The policy to send, or null to send none.</param>
/// <param name="Hsts">Tell browsers to insist on HTTPS for this site (sent only on requests that arrived over HTTPS).</param>
/// <param name="CheckOrigin">Refuse state-changing requests that a browser says came from some other website.</param>
public sealed record SecurityOptions(string? ContentSecurityPolicy, bool Hsts, bool CheckOrigin)
{
    // Everything the app needs and nothing more: its own scripts and styles; cover art from anywhere (book covers come
    // from the catalog's own image hosts); audio from itself or saved on the device; its live-update connection; and no
    // framing, plugins or form posts elsewhere. Style attributes are used by the app, so inline styles are allowed.
    public const string DefaultPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: https: http:; " +
        "media-src 'self' blob:; font-src 'self' data:; connect-src 'self' ws: wss:; manifest-src 'self'; worker-src 'self'; " +
        "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'";

    /// <summary>
    /// Read from configuration (appsettings.Production.json). "Security:ContentSecurityPolicy" replaces the default
    /// policy, and an empty value turns it off -- the way back if a policy ever blocks something it shouldn't.
    /// </summary>
    public static SecurityOptions From(IConfiguration configuration)
    {
        var policy = configuration["Security:ContentSecurityPolicy"];
        return new SecurityOptions(
            policy is null ? DefaultPolicy : string.IsNullOrWhiteSpace(policy) ? null : policy,
            configuration.GetValue("Security:Hsts", true),
            configuration.GetValue("Security:CheckOrigin", true));
    }
}

/// <summary>Headers that make browsers treat the site more carefully, and a check against other websites posting to it.</summary>
public static class SecurityHeaders
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app, SecurityOptions options) =>
        app.Use(async (context, next) =>
        {
            Apply(context, options);
            if (options.CheckOrigin && !OriginIsAcceptable(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "That request came from another website, so it was refused.",
                });
                return;
            }
            await next();
        });

    internal static void Apply(HttpContext context, SecurityOptions options)
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "same-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
        if (options.ContentSecurityPolicy is not null)
        {
            headers["Content-Security-Policy"] = options.ContentSecurityPolicy;
        }
        // Six months, this exact host only: long enough to matter, short enough to recover if HTTPS were ever dropped.
        if (options.Hsts && context.Request.IsHttps)
        {
            headers["Strict-Transport-Security"] = "max-age=15552000";
        }
        // Sign-in state must never be kept by a browser cache or a shared one in between.
        if (context.Request.Path.StartsWithSegments("/api/auth"))
        {
            headers.CacheControl = "no-store";
        }
    }

    private static readonly string[] SafeMethods = ["GET", "HEAD", "OPTIONS", "TRACE"];

    /// <summary>
    /// A browser says where a request came from (Origin). A change-making request from another website's page is
    /// refused even if the visitor happens to be signed in here. Requests with no Origin (this app's own page loading,
    /// a command-line tool) are left alone, unless the browser says outright it is cross-site.
    /// </summary>
    internal static bool OriginIsAcceptable(HttpRequest request)
    {
        if (SafeMethods.Contains(request.Method, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        var origin = request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
        {
            return !string.Equals(request.Headers["Sec-Fetch-Site"].ToString(), "cross-site", StringComparison.OrdinalIgnoreCase);
        }
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var from))
        {
            return false;   // including the literal "null" some browsers send from sandboxed pages
        }

        var host = request.Host;
        if (!string.Equals(from.Host, host.Host, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        // Behind a proxy the port the browser used need not be the one this app sees, so ports are only compared
        // when both sides name one outright.
        return host.Port is null || from.IsDefaultPort || host.Port == from.Port;
    }
}
