using Alpixa.Core.Localization;
using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Alpixa.Core.Models;
using Alpixa.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Alpixa.Infrastructure.Reports;

public sealed class ReportService(IDbContextFactory<AlpixaDbContext> dbFactory)
{
    public async Task<CampaignReport> GetCampaignReportAsync(int campaignId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var campaign = await db.Campaigns.AsNoTracking().FirstAsync(c => c.Id == campaignId, ct);
        var jobs = await db.SendJobs.AsNoTracking().Where(j => j.CampaignId == campaignId)
            .GroupBy(j => j.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var events = await db.DeliveryEvents.AsNoTracking().Where(e => e.CampaignId == campaignId)
            .GroupBy(e => e.Kind).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);

        int J(SendJobStatus s) => jobs.FirstOrDefault(x => x.Key == s)?.Count ?? 0;
        int E(DeliveryEventKind k) => events.FirstOrDefault(x => x.Key == k)?.Count ?? 0;

        return new CampaignReport
        {
            CampaignId = campaign.Id,
            Name = campaign.Name,
            Status = campaign.Status,
            Total = jobs.Sum(j => j.Count),
            Sent = J(SendJobStatus.Sent),
            Failed = J(SendJobStatus.Failed),
            Pending = J(SendJobStatus.Pending) + J(SendJobStatus.Sending),
            Skipped = J(SendJobStatus.Skipped),
            Uncertain = J(SendJobStatus.Uncertain),
            HardBounces = E(DeliveryEventKind.HardBounce),
            SoftBounces = E(DeliveryEventKind.SoftBounce),
            Unsubscribes = E(DeliveryEventKind.Unsubscribe),
            Complaints = E(DeliveryEventKind.Complaint),
            Opens = E(DeliveryEventKind.Open)
        };
    }

    public async Task<List<CampaignReport>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ids = await db.Campaigns.AsNoTracking().OrderByDescending(c => c.CreatedUtc).Select(c => c.Id).ToListAsync(ct);
        var result = new List<CampaignReport>();
        foreach (var id in ids) result.Add(await GetCampaignReportAsync(id, ct));
        return result;
    }

    public async Task ExportCampaignAsync(int campaignId, string path, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.SendJobs.AsNoTracking().Where(j => j.CampaignId == campaignId).OrderBy(j => j.Sequence)
            .Select(j => new { j.Email, j.Status, j.Attempts, j.SmtpCode, j.LastError, j.SentUtc })
            .ToListAsync(ct);
        var events = await db.DeliveryEvents.AsNoTracking().Where(e => e.CampaignId == campaignId)
            .Select(e => new { e.EmailNormalized, e.Kind }).ToListAsync(ct);
        var eventsByEmail = events.GroupBy(e => e.EmailNormalized)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => EventText(x.Kind)).Distinct()));

        string[] headers = [Msg.T("Report_01"), Msg.T("Report_02"), Msg.T("Report_03"), Msg.T("Report_04"), Msg.T("Report_05"), Msg.T("Report_06"), Msg.T("Report_07")];
        var data = rows.Select(r => new object?[]
        {
            r.Email, StatusText(r.Status), r.Attempts, r.SmtpCode, r.LastError,
            r.SentUtc?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            eventsByEmail.GetValueOrDefault(r.Email)
        }).ToList();

        if (Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            WriteXlsx(path, headers, data);
        else
            await WriteCsvAsync(path, headers, data, ct);
    }

    public static string StatusText(SendJobStatus status) => status switch
    {
        SendJobStatus.Pending => Msg.T("Report_08"),
        SendJobStatus.Sending => Msg.T("Report_09"),
        SendJobStatus.Sent => Msg.T("Report_10"),
        SendJobStatus.Failed => Msg.T("Report_11"),
        SendJobStatus.Skipped => Msg.T("Report_12"),
        SendJobStatus.Uncertain => Msg.T("Report_13"),
        _ => status.ToString()
    };

    public static string EventText(DeliveryEventKind kind) => kind switch
    {
        DeliveryEventKind.HardBounce => Msg.T("Report_14"),
        DeliveryEventKind.SoftBounce => Msg.T("Report_15"),
        DeliveryEventKind.Unsubscribe => Msg.T("Report_16"),
        DeliveryEventKind.Complaint => Msg.T("Report_17"),
        DeliveryEventKind.Open => Msg.T("Report_18"),
        _ => kind.ToString()
    };

    private static void WriteXlsx(string path, string[] headers, List<object?[]> data)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Rapor");
        for (var c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
        for (var r = 0; r < data.Count; r++)
        for (var c = 0; c < headers.Length; c++)
            ws.Cell(r + 2, c + 1).Value = data[r][c]?.ToString() ?? "";
        ws.Row(1).Style.Font.Bold = true;
        ws.Columns().AdjustToContents(1, Math.Min(data.Count + 1, 200));
        wb.SaveAs(path);
    }

    private static async Task WriteCsvAsync(string path, string[] headers, List<object?[]> data, CancellationToken ct)
    {
        await using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        await writer.WriteLineAsync(string.Join(',', headers.Select(Escape)));
        foreach (var row in data)
        {
            ct.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Join(',', row.Select(v => Escape(v?.ToString() ?? ""))));
        }
    }

    private static string Escape(string value)
    {
        if (value.Length > 0 && "=+-@".Contains(value[0])) value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
