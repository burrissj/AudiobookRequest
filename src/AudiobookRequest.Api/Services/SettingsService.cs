using AudiobookRequest.Core.Entities;
using AudiobookRequest.Data;
using Microsoft.EntityFrameworkCore;

namespace AudiobookRequest.Api.Services;

public class SettingsService(AppDbContext db)
{
    public async Task<AppSettings> GetAsync(CancellationToken ct = default)
    {
        var settings = await db.Settings.OrderBy(s => s.Id).FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = new AppSettings();
            db.Settings.Add(settings);
            await db.SaveChangesAsync(ct);
        }

        return settings;
    }

    public async Task<AppSettings> UpdateAsync(Action<AppSettings> apply, CancellationToken ct = default)
    {
        var settings = await GetAsync(ct);
        apply(settings);
        await db.SaveChangesAsync(ct);
        return settings;
    }
}
