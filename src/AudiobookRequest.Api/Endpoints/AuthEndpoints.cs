using System.Security.Claims;
using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Entities;
using AudiobookRequest.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Endpoints;

public record LoginRequest(string Username, string Password);
public record CurrentUserResponse(string Username, string Role);

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");

        group.MapPost("/login", async (
            LoginRequest req,
            HttpContext httpContext,
            SettingsService settingsService,
            IJellyfinClient jellyfinClient,
            AppDbContext db,
            LoginThrottle throttle,
            ILogger<LoginRequest> logger,
            CancellationToken ct) =>
        {
            var settings = await settingsService.GetAsync(ct);
            if (!settings.SetupCompleted || string.IsNullOrWhiteSpace(settings.JellyfinUrl))
            {
                return Results.Problem("Setup has not been completed yet.", statusCode: 400);
            }

            if (req is null || string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrEmpty(req.Password) || req.Username.Length > 256 || req.Password.Length > 1024)
            {
                return Results.Problem("Enter your username and password.", statusCode: 400);
            }

            // After too many wrong passwords from one place, stop asking Jellyfin for a while.
            var address = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            if (throttle.BlockedFor(address, req.Username) is { } wait)
            {
                logger.LogWarning("Sign-in from {Address} refused: too many failed attempts", address);
                httpContext.Response.Headers.RetryAfter = ((int)Math.Ceiling(wait.TotalSeconds)).ToString();
                return Results.Problem($"Too many failed sign-in attempts. Try again in {LoginThrottle.Describe(wait)}.", statusCode: 429);
            }

            // One Jellyfin "device" per browser (remembered in a cookie), named for what it is, so signing out of one
            // browser ends only its own Jellyfin session and the admin can tell them apart in Jellyfin's dashboard.
            var deviceId = httpContext.Request.Cookies[DeviceNames.CookieName];
            if (!DeviceNames.IsValidId(deviceId))
            {
                deviceId = DeviceNames.NewId();
                httpContext.Response.Cookies.Append(DeviceNames.CookieName, deviceId, new CookieOptions
                {
                    HttpOnly = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = httpContext.Request.IsHttps,
                    MaxAge = TimeSpan.FromDays(365),
                    Path = "/",
                });
            }
            var device = new JellyfinDevice(deviceId!, DeviceNames.FromUserAgent(httpContext.Request.Headers.UserAgent.ToString()));

            var authResult = await jellyfinClient.AuthenticateAsync(settings.JellyfinUrl, req.Username, req.Password, ct, device);
            if (authResult is null)
            {
                throttle.RecordFailure(address, req.Username);
                logger.LogWarning("Failed sign-in from {Address}", address);
                return Results.Unauthorized();
            }
            throttle.RecordSuccess(address, req.Username);

            var user = await db.Users.FirstOrDefaultAsync(u => u.JellyfinUserId == authResult.User.Id, ct);
            var role = authResult.User.Policy?.IsAdministrator == true ? UserRole.Admin : UserRole.User;
            if (user is null)
            {
                user = new AppUser
                {
                    JellyfinUserId = authResult.User.Id,
                    Username = authResult.User.Name,
                    Role = role
                };
                db.Users.Add(user);
            }
            else
            {
                user.Username = authResult.User.Name;
                user.Role = role;
            }

            await db.SaveChangesAsync(ct);

            var identity = new ClaimsIdentity(SessionClaims.Build(user, authResult, device), CookieAuthenticationDefaults.AuthenticationScheme);
            await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            return Results.Ok(new CurrentUserResponse(user.Username, user.Role.ToString()));
        });

        group.MapPost("/logout", async (
            HttpContext httpContext,
            ClaimsPrincipal principal,
            SettingsService settingsService,
            IJellyfinPlayerClient player,
            ILogger<LoginRequest> logger) =>
        {
            // End this browser's Jellyfin session too, so signing in and out doesn't leave a trail of stale devices
            // in Jellyfin's dashboard. Best effort: signing out of the app must never depend on Jellyfin answering.
            var session = SessionClaims.JellyfinSessionOf(principal);
            if (session is not null)
            {
                try
                {
                    var settings = await settingsService.GetAsync(CancellationToken.None);
                    if (!string.IsNullOrWhiteSpace(settings.JellyfinUrl))
                    {
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await player.LogoutAsync(settings.JellyfinUrl.Trim().TrimEnd('/'), session, timeout.Token);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Couldn't end the Jellyfin session on sign-out");
                }
            }

            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });

        group.MapGet("/me", (ClaimsPrincipal user) =>
        {
            if (user.Identity?.IsAuthenticated != true)
            {
                return Results.Unauthorized();
            }

            var username = user.Identity.Name ?? "";
            var role = user.FindFirstValue(ClaimTypes.Role) ?? UserRole.User.ToString();
            return Results.Ok(new CurrentUserResponse(username, role));
        });

        return app;
    }
}
