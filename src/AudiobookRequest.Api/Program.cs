using AudiobookRequest.Api.BackgroundServices;
using AudiobookRequest.Api.Endpoints;
using AudiobookRequest.Api.Hubs;
using AudiobookRequest.Api.Services;
using AudiobookRequest.Core.Audio;
using AudiobookRequest.Core.Clients;
using AudiobookRequest.Core.Import;
using AudiobookRequest.Core.Library;
using AudiobookRequest.Data;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
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
        // The HTTP client's own per-request logging prints a full stack trace for every failed call to an outside
        // service; the app logs those itself as one short line (ExternalCallLog), so keep only this one's warnings.
        .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
        .WriteTo.Console()
        .WriteTo.File(logFilePath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: retainedFileCount);
});

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Missing ConnectionStrings:Default");

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));

builder.Services.AddScoped<SettingsService>();

builder.Services.AddHttpClient<IJellyfinClient, JellyfinClient>();
builder.Services.AddHttpClient<IJellyfinPlayerClient, JellyfinPlayerClient>();
builder.Services.AddHttpClient<IChaptarrClient, ChaptarrClient>();
builder.Services.AddHttpClient<ICoverArtDownloader, CoverArtDownloader>();
// A page waiting on a description shouldn't hang on a slow Audible/Audnexus: give each call a dozen seconds.
builder.Services.AddHttpClient<IAudiobookMetadataClient, AudiobookMetadataClient>(c => c.Timeout = TimeSpan.FromSeconds(12));
builder.Services.AddHttpClient<IExternalNotifier, ExternalNotifier>(c => c.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddMemoryCache();

var ffmpegPath = ResolveToolPath(builder.Configuration["Ffmpeg:Path"] ?? "ffmpeg", builder.Environment.ContentRootPath);
var ffprobePath = ResolveToolPath(builder.Configuration["Ffmpeg:ProbePath"] ?? "ffprobe", builder.Environment.ContentRootPath);
var logDirectory = Path.GetDirectoryName(ResolveToolPath(builder.Configuration["Serilog:LogFilePath"] ?? "data/logs/log-.txt", AppContext.BaseDirectory))
    ?? AppContext.BaseDirectory;

builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
builder.Services.AddSingleton<IAudioCombiner>(sp => new FfmpegAudioCombiner(
    sp.GetRequiredService<IProcessRunner>(), ffmpegPath, ffprobePath));
builder.Services.AddSingleton<IAudioProbe>(sp => new FfprobeAudioProbe(sp.GetRequiredService<IProcessRunner>(), ffprobePath));
builder.Services.AddSingleton(new AppPaths(ffmpegPath, ffprobePath, logDirectory, Path.Combine(AppContext.BaseDirectory, "data", "backups")));
builder.Services.AddSingleton<BackgroundJobStatus>();
builder.Services.AddSingleton<LogViewerService>();
builder.Services.AddScoped<BookProcessingService>();
builder.Services.AddScoped<ChaptarrOrchestrationService>();
builder.Services.AddScoped<UpcomingRequestService>();
builder.Services.AddScoped<LibraryCatalogService>();
builder.Services.AddScoped<LibrarySeriesService>();
builder.Services.AddScoped<RecommendationService>();
builder.Services.AddScoped<SeriesRequestService>();
builder.Services.AddScoped<RequestCancellationService>();
builder.Services.AddScoped<RequestPolicyService>();
builder.Services.AddScoped<RequestCreationService>();
builder.Services.AddScoped<RequestActionsService>();
builder.Services.AddScoped<MetadataLookupService>();
builder.Services.AddScoped<HealthService>();
builder.Services.AddScoped<CatalogBrowseService>();
builder.Services.AddScoped<ImportFixService>();
builder.Services.AddScoped<FollowService>();
builder.Services.AddScoped<DatabaseBackupService>();
builder.Services.AddScoped<JellyfinLinkService>();
builder.Services.AddScoped<ListenService>();
builder.Services.AddScoped<ListeningStatsService>();
builder.Services.AddScoped<PersonalBookService>();
builder.Services.AddScoped<LibraryCleanupService>();
builder.Services.AddScoped<ImportService>();
builder.Services.AddSingleton<ImportJobService>();

builder.Services.AddSingleton(new LoginThrottle(TimeProvider.System));
builder.Services.AddSignalR();
builder.Services.AddScoped<INotificationSender, NotificationSender>();
builder.Services.AddHostedService<ChaptarrOrchestrationBackgroundService>();
builder.Services.AddHostedService<UpcomingBackgroundService>();
builder.Services.AddHostedService<LibraryProcessingBackgroundService>();
builder.Services.AddHostedService<OriginalCleanupBackgroundService>();
builder.Services.AddHostedService<ListeningHistoryRefreshBackgroundService>();
builder.Services.AddHostedService<LibraryCatalogRefreshBackgroundService>();
builder.Services.AddHostedService<LibrarySeriesBackgroundService>();
builder.Services.AddHostedService<FollowCheckBackgroundService>();
builder.Services.AddHostedService<DatabaseBackupBackgroundService>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "abr_auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
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

    // Create the single settings row up front. Left to first use, several background services asking for
    // it at the same moment on a fresh install each create their own, leaving stray empty rows behind.
    if (!db.Settings.Any())
    {
        db.Settings.Add(new AudiobookRequest.Core.Entities.AppSettings());
        db.SaveChanges();
    }
}

// Behind an HTTPS reverse proxy (Caddy, Cloudflare Tunnel, ...) the proxy talks plain HTTP to this app and
// says so in X-Forwarded-Proto. Honoring it keeps sign-in cookies and generated links correct, which the
// installable-app (PWA) features need. Only the scheme/host/IP are affected, and nothing here trusts the IP.
// A proxy on this machine or anywhere on a private network is believed (see ForwardedHeadersSetup); set
// "ForwardedHeaders:TrustAnyProxy": true in appsettings.Production.json to believe a proxy at any address.
app.UseForwardedHeaders(ForwardedHeadersSetup.Create(app.Configuration.GetValue<bool>("ForwardedHeaders:TrustAnyProxy")));

// Security headers on everything (including the static files below), and a refusal of change-making requests that a
// browser says came from another website. Set "Security:ContentSecurityPolicy" to "" in appsettings.Production.json
// to switch the content policy off if it ever blocks something it shouldn't.
app.UseSecurityHeaders(SecurityOptions.From(app.Configuration));

app.UseDefaultFiles();
var staticFiles = new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = StaticCachePolicy.For(ctx.Context.Request.Path.Value),
};
app.UseStaticFiles(staticFiles);

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapSetupEndpoints();
app.MapBookEndpoints();
app.MapAdminEndpoints();
app.MapBrowseEndpoints();
app.MapFollowEndpoints();
app.MapAccountEndpoints();
app.MapListenEndpoints();
app.MapImportEndpoints();
app.MapAppEndpoints();
app.MapHub<NotificationHub>("/hubs/notifications");

app.MapFallbackToFile("index.html", new StaticFileOptions
{
    // Always the page itself, so every deep link and refresh revalidates it.
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = StaticCachePolicy.Revalidate,
});

app.Run();

// A bare command name (no path separators) is left as-is to resolve via PATH; anything else is
// resolved against the app's own directory rather than the process's current working directory,
// which for a Windows Service is not reliably the install folder.
static string ResolveToolPath(string configuredPath, string contentRoot) =>
    Path.IsPathRooted(configuredPath) || (!configuredPath.Contains('/') && !configuredPath.Contains('\\'))
        ? configuredPath
        : Path.Combine(contentRoot, configuredPath);
