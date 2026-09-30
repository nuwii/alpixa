using System.Text.Json;
using Alpixa.Core.Models;
using Alpixa.Core.Settings;
using Alpixa.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Alpixa.Infrastructure.Settings;

public sealed class SettingsStore(IDbContextFactory<AlpixaDbContext> dbFactory)
{
    private const string SendingKey = "sending";
    private const string GeneralKey = "general";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public Task<SendingSettings> GetSendingAsync(CancellationToken ct = default) => GetAsync<SendingSettings>(SendingKey, ct);
    public Task SaveSendingAsync(SendingSettings value, CancellationToken ct = default) => SetAsync(SendingKey, value, ct);
    public Task<GeneralSettings> GetGeneralAsync(CancellationToken ct = default) => GetAsync<GeneralSettings>(GeneralKey, ct);
    public Task SaveGeneralAsync(GeneralSettings value, CancellationToken ct = default) => SetAsync(GeneralKey, value, ct);

    public async Task<T> GetAsync<T>(string key, CancellationToken ct = default) where T : new()
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null) return new T();
        try
        {
            return JsonSerializer.Deserialize<T>(row.Value, Json) ?? new T();
        }
        catch (JsonException)
        {
            return new T();
        }
    }

    public async Task SetAsync<T>(string key, T value, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var json = JsonSerializer.Serialize(value, Json);
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null) db.Settings.Add(new AppSetting { Key = key, Value = json });
        else row.Value = json;
        await db.SaveChangesAsync(ct);
    }
}
