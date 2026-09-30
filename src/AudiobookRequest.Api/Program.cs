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
using Serilog.Events;

// A Windows Service's starting working directory is not reliably its install folder (often
// %SystemRoot%\System32), which silently breaks every relative path in config (SQLite connection
// string, log file path, etc.). Pin it to the app's own directory before anything else runs.
Directory.SetCurrentDirectory(AppContext.BaseDirectory);

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService();

builder.Host.UseSerilog((context, _, loggerConfig) =>
{
    var minLevel = Enum.TryParse<LogEventLevel>(context.Configuration["Serilog:MinimumLevel"], true, out var level)
        ? level
        : LogEventLevel.Information;
    var logFilePath = ResolveToolPath(context.Configuration["Serilog:LogFilePath"] ?? "data/logs/log-.txt", AppContext.BaseDirectory);
    var retainedFileCount = int.TryParse(context.Configuration["Serilog:RetainedFileCountLimit"], out var n) ? n : 14;

    loggerConfig
        .MinimumLevel.Is(minLevel)
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
        .WriteTo.Console()
        .WriteTo.File(logFilePath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: retainedFileCount);
});

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Default");

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));

builder.Services.AddScoped<SettingsService>();

builder.Services.AddHttpClient<IJellyfinClient, JellyfinClient>();
builder.Services.AddHttpClient<IChaptarrClient, ChaptarrClient>();
builder.Services.AddHttpClient<ICoverArtDownloader, CoverArtDownloader>();

builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
builder.Services.AddSingleton<IAudioCombiner>(sp => new FfmpegAudioCombiner(
    sp.GetRequiredService<IProcessRunner>(),
    ResolveToolPath(builder.Configuration["Ffmpeg:Path"] ?? "ffmpeg", builder.Environment.ContentRootPath),
    ResolveToolPath(builder.Configuration["Ffmpeg:ProbePath"] ?? "ffprobe", builder.Environment.ContentRootPath)));
builder.Services.AddScoped<BookProcessingService>();

builder.Services.AddSignalR();
builder.Services.AddScoped<INotificationSender, NotificationSender>();
builder.Services.AddHostedService<ChaptarrOrchestrationBackgroundService>();
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

var dataProtectionKeysPath = ResolveToolPath(builder.Configuration["DataProtectionKeysPath"] ?? "data/keys", builder.Environment.ContentRootPath);
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

// A bare command name (no path separators) is left as-is to resolve via PATH; anything else is
// resolved against the app's own directory rather than the process's current working directory,
// which for a Windows Service is not reliably the install folder.
static string ResolveToolPath(string configuredPath, string contentRoot) =>
    Path.IsPathRooted(configuredPath) || (!configuredPath.Contains('/') && !configuredPath.Contains('\\'))
        ? configuredPath
        : Path.Combine(contentRoot, configuredPath);
