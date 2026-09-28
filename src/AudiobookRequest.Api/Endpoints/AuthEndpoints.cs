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
            CancellationToken ct) =>
        {
            var settings = await settingsService.GetAsync(ct);
            if (!settings.SetupCompleted || string.IsNullOrWhiteSpace(settings.JellyfinUrl))
            {
                return Results.Problem("Setup has not been completed yet.", statusCode: 400);
            }

            var authResult = await jellyfinClient.AuthenticateAsync(settings.JellyfinUrl, req.Username, req.Password, ct);
            if (authResult is null)
            {
                return Results.Unauthorized();
            }

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

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, user.Username),
                new(ClaimTypes.Role, user.Role.ToString())
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

            return Results.Ok(new CurrentUserResponse(user.Username, user.Role.ToString()));
        });

        group.MapPost("/logout", async (HttpContext httpContext) =>
        {
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
