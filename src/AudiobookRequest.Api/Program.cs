using AudiobookRequest.Api.BackgroundServices;
using AudiobookRequest.Api.Endpoints;
using AudiobookRequest.Api.Hubs;
using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Audio;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Library;
using AudiobookRequest.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService();

builder.Host.UseSerilog((context, services, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Default");

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));

builder.Services.AddScoped<SettingsService>();

builder.Services.AddHttpClient<IJellyfinClient, JellyfinClient>();
builder.Services.AddHttpClient<IReadarrClient, ReadarrClient>();
builder.Services.AddHttpClient<ICoverArtDownloader, CoverArtDownloader>();

builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
builder.Services.AddSingleton<IAudioCombiner>(sp => new FfmpegAudioCombiner(
    sp.GetRequiredService<IProcessRunner>(),
    builder.Configuration["Ffmpeg:Path"] ?? "ffmpeg",
    builder.Configuration["Ffmpeg:ProbePath"] ?? "ffprobe"));
builder.Services.AddScoped<BookProcessingService>();

builder.Services.AddSignalR();
builder.Services.AddScoped<INotificationSender, NotificationSender>();
builder.Services.AddHostedService<ReadarrOrchestrationBackgroundService>();
builder.Services.AddHostedService<LibraryProcessingBackgroundService>();
builder.Services.AddHostedService<OriginalCleanupBackgroundService>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "abr_auth";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        // API-style auth: return 401/403 instead of redirecting to an HTML login page.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin"));
});

var dataProtectionKeysPath = Path.Combine(builder.Environment.ContentRootPath,
    builder.Configuration["DataProtectionKeysPath"] ?? "data/keys");
Directory.CreateDirectory(dataProtectionKeysPath);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));

var app = builder.Build();

Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "data"));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapSetupEndpoints();
app.MapBookEndpoints();
app.MapHub<NotificationHub>("/hubs/notifications");

app.MapFallbackToFile("index.html");

app.Run();
