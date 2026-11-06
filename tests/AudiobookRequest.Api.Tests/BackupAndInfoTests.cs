using AudiobookRequest.Api.Services;
using Microsoft.Data.Sqlite;

namespace AudiobookRequest.Api.Tests;

public sealed class BackupAndInfoTests : IDisposable
{
    private readonly TestDb t = new();
    private readonly string dir = Path.Combine(Path.GetTempPath(), "abr-backup-" + Guid.NewGuid().ToString("N"));

    private DatabaseBackupService Service() =>
        new(t.Db, t.Settings, new AppPaths("ffmpeg", "ffprobe", dir, dir), Nulls.Logger<DatabaseBackupService>());

    [Fact]
    public async Task Create_WritesAReadableSnapshotContainingTheData()
    {
        t.AddRequest(t.Victoria, AudiobookRequest.Core.Entities.BookRequestStatus.Requested, "Backed Up Book");

        var backup = await Service().CreateAsync(default);

        var path = Path.Combine(dir, backup.FileName);
        Assert.True(File.Exists(path));
        Assert.True(backup.SizeBytes > 0);
        using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Title FROM BookRequests";
        Assert.Equal("Backed Up Book", command.ExecuteScalar());
    }

    [Fact]
    public async Task Create_KeepsOnlyTheNewestFewBackups()
    {
        t.CurrentSettings.BackupRetentionCount = 2;
        t.Db.SaveChanges();
        Directory.CreateDirectory(dir);
        foreach (var old in new[] { "app-20260101-010101.db", "app-20260102-010101.db", "app-20260103-010101.db" })
        {
            File.WriteAllText(Path.Combine(dir, old), "old");
        }
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "not a backup, must survive");

        await Service().CreateAsync(default);

        var names = Service().List().Select(b => b.FileName).ToList();
        Assert.Equal(2, names.Count);
        Assert.DoesNotContain("app-20260101-010101.db", names);
        Assert.DoesNotContain("app-20260102-010101.db", names);
        Assert.True(File.Exists(Path.Combine(dir, "notes.txt")));
    }

    [Fact]
    public void PathFor_OnlyHandsOutRealBackupsByTheirOwnNames()
    {
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "app-20260101-010101.db"), "x");
        File.WriteAllText(Path.Combine(dir, "secret.txt"), "x");
        var service = Service();

        Assert.NotNull(service.PathFor("app-20260101-010101.db"));
        Assert.Null(service.PathFor("app-20260909-090909.db"));    // right shape, doesn't exist
        Assert.Null(service.PathFor("secret.txt"));                // exists, wrong shape
        Assert.Null(service.PathFor("../app-20260101-010101.db")); // traversal
        Assert.Null(service.PathFor(@"..\..\Windows\win.ini"));
    }

    [Fact]
    public async Task IsDue_FollowsTheSettingAndTheAgeOfTheNewestBackup()
    {
        var service = Service();
        Assert.True(await service.IsDueAsync(TimeSpan.FromHours(20), default)); // none yet

        await service.CreateAsync(default);
        Assert.False(await service.IsDueAsync(TimeSpan.FromHours(20), default)); // just made one

        t.CurrentSettings.BackupRetentionCount = 0;
        t.Db.SaveChanges();
        Assert.False(await service.IsDueAsync(TimeSpan.Zero, default)); // backups switched off
    }

    [Fact]
    public void Changelog_IsNewestFirstWithUniqueVersionsAndRealContent()
    {
        var versions = AppInfo.Changelog.Select(c => c.Version).ToList();

        Assert.Equal(versions.Count, versions.Distinct().Count());
        Assert.All(AppInfo.Changelog, c => Assert.NotEmpty(c.Items));
        Assert.Equal("0.21.0", AppInfo.Changelog[0].Version);
    }

    [Fact]
    public void Version_IsReported() => Assert.False(string.IsNullOrWhiteSpace(AppInfo.Version));

    public void Dispose()
    {
        t.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(dir, recursive: true); } catch { /* temp cleanup */ }
    }
}
