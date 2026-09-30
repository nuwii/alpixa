using Alpixa.Core.Localization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Alpixa.Core.Abstractions;
using Alpixa.Infrastructure.Data;
using Alpixa.Infrastructure.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Alpixa.Infrastructure.Maintenance;

public sealed class MaintenanceService(IAppPaths paths, IDbContextFactory<AlpixaDbContext> dbFactory, SettingsStore settings)
{
    public const string PendingRestoreFileName = "restore-pending.db";

    public async Task<string> CreateBackupAsync(string targetZipPath, CancellationToken ct = default)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"alpixa-backup-{Guid.NewGuid():N}.db");
        try
        {
            await using (var db = await dbFactory.CreateDbContextAsync(ct))
            {
                await db.Database.ExecuteSqlRawAsync("VACUUM INTO {0}", [temp], ct);
            }

            if (File.Exists(targetZipPath)) File.Delete(targetZipPath);
            using var zip = ZipFile.Open(targetZipPath, ZipArchiveMode.Create);
            zip.CreateEntryFromFile(temp, "alpixa.db");
            var files = Path.Combine(paths.DataDirectory, "files");
            if (Directory.Exists(files))
                foreach (var f in Directory.GetFiles(files))
                    zip.CreateEntryFromFile(f, "files/" + Path.GetFileName(f));
            var info = zip.CreateEntry("backup-info.json");
            await using (var w = new StreamWriter(info.Open()))
                await w.WriteAsync(JsonSerializer.Serialize(new { CreatedUtc = DateTime.UtcNow, Version = AppVersion(), Note = Msg.T("Maint_01") }));
            return targetZipPath;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public async Task StageRestoreAsync(string zipPath, CancellationToken ct = default)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry("alpixa.db") ?? zip.GetEntry("mailpilot.db") ?? throw new InvalidDataException(Msg.T("Maint_02"));
        var staged = Path.Combine(paths.DataDirectory, PendingRestoreFileName);
        await using (var source = entry.Open())
        await using (var target = File.Create(staged))
            await source.CopyToAsync(target, ct);

        var files = Path.Combine(paths.DataDirectory, "files");
        Directory.CreateDirectory(files);
        foreach (var fileEntry in zip.Entries.Where(e => e.FullName.StartsWith("files/", StringComparison.Ordinal) && e.Name.Length > 0))
        {
            if (fileEntry.Name.Contains("..") || fileEntry.Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) continue;
            fileEntry.ExtractToFile(Path.Combine(files, fileEntry.Name), overwrite: true);
        }
    }

    public static void ApplyPendingRestore(IAppPaths paths)
    {
        var staged = Path.Combine(paths.DataDirectory, PendingRestoreFileName);
        if (!File.Exists(staged)) return;
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var f = paths.DatabasePath + suffix;
            if (File.Exists(f)) File.Move(f, f + ".before-restore", overwrite: true);
        }
        File.Move(staged, paths.DatabasePath);
    }

    public async Task<string> CreateSupportBundleAsync(string targetZipPath, CancellationToken ct = default)
    {
        if (File.Exists(targetZipPath)) File.Delete(targetZipPath);
        using var zip = ZipFile.Open(targetZipPath, ZipArchiveMode.Create);

        if (Directory.Exists(paths.LogDirectory))
        {
            foreach (var log in Directory.GetFiles(paths.LogDirectory, "*.log").OrderByDescending(File.GetLastWriteTimeUtc).Take(7))
            {
                await using var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var entry = zip.CreateEntry("logs/" + Path.GetFileName(log));
                await using var es = entry.Open();
                await fs.CopyToAsync(es, ct);
            }
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var profiles = await db.SenderProfiles.AsNoTracking().Select(p => new
        {
            p.Id, p.Kind, p.SmtpHost, p.SmtpPort, p.Security, p.Auth, FromDomain = p.FromAddress.Substring(p.FromAddress.IndexOf('@') + 1),
            p.DailyLimit, p.HourlyLimit, p.PerMinuteLimit, p.DkimEnabled, p.DkimSelector, p.ImapEnabled, p.ImapHost, p.WarmupEnabled, p.WarmupStartedUtc
        }).ToListAsync(ct);
        var campaigns = await db.Campaigns.AsNoTracking().Select(c => new { c.Id, c.Status, c.Mode, c.Parallelism, c.DryRun, c.PauseReason, c.CreatedUtc }).ToListAsync(ct);
        var sending = await settings.GetSendingAsync(ct);

        var info = new
        {
            CreatedUtc = DateTime.UtcNow,
            Version = AppVersion(),
            Os = RuntimeInformation.OSDescription,
            Arch = RuntimeInformation.OSArchitecture.ToString(),
            Runtime = RuntimeInformation.FrameworkDescription,
            ContactCount = await db.Contacts.CountAsync(ct),
            SuppressionCount = await db.Suppressions.CountAsync(ct),
            Profiles = profiles,
            Campaigns = campaigns,
            SendingSettings = sending
        };
        var infoEntry = zip.CreateEntry("diagnostics.json");
        await using (var w = new StreamWriter(infoEntry.Open(), Encoding.UTF8))
            await w.WriteAsync(JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));

        return targetZipPath;
    }

    private static string AppVersion() => typeof(MaintenanceService).Assembly.GetName().Version?.ToString() ?? "1.0.0";
}
