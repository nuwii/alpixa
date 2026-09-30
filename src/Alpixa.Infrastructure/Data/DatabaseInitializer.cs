using Alpixa.Infrastructure.Templates;
using Microsoft.EntityFrameworkCore;

namespace Alpixa.Infrastructure.Data;

public sealed class DatabaseInitializer(IDbContextFactory<AlpixaDbContext> dbFactory)
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);

        if (!await db.Templates.AnyAsync(t => t.IsBuiltIn, ct))
        {
            db.Templates.AddRange(BuiltInTemplates.Create());
            await db.SaveChangesAsync(ct);
            return;
        }

        var legacy = await db.Templates.Where(t => t.IsBuiltIn).OrderBy(t => t.Id).ToListAsync(ct);
        var fresh = BuiltInTemplates.Create().ToList();
        var changed = false;
        for (var i = 0; i < Math.Min(legacy.Count, fresh.Count); i++)
        {
            if (legacy[i].UpdatedUtc > legacy[i].CreatedUtc.AddSeconds(1) || !IsAsciiOnly(legacy[i].HtmlBody)) continue;
            legacy[i].Name = fresh[i].Name;
            legacy[i].Subject = fresh[i].Subject;
            legacy[i].HtmlBody = fresh[i].HtmlBody;
            changed = true;
        }
        if (changed) await db.SaveChangesAsync(ct);
    }

    private static bool IsAsciiOnly(string text) => text.All(c => c < 128);
}
