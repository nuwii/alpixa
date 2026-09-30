using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Maui.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Alpixa.App.Services;
using Alpixa.Core.Models;
using Alpixa.Core.Rules;
using Alpixa.Infrastructure.Contacts;
using Alpixa.Infrastructure.Reports;
using Microsoft.Extensions.Logging;

namespace Alpixa.App.ViewModels;

public sealed class ReportRow(CampaignReport report)
{
    public CampaignReport Report { get; } = report;
    public string BounceText => Report.BounceRate.ToString("P1");
    public string ComplaintText => Report.ComplaintRate.ToString("P2");
    public bool BounceAlert => Report.Sent >= DeliverabilityThresholds.RateCheckMinimumSent && Report.BounceRate > DeliverabilityThresholds.HardBounceRateTarget;
    public bool ComplaintAlert => Report.Sent >= DeliverabilityThresholds.RateCheckMinimumSent && Report.ComplaintRate > DeliverabilityThresholds.ComplaintRateTarget;
    public Color BounceColor => Report.BounceRate > DeliverabilityThresholds.HardBounceRateMax ? Color.FromArgb("#E02424") : BounceAlert ? Color.FromArgb("#E3A008") : Color.FromArgb("#0E9F6E");
    public Color ComplaintColor => Report.ComplaintRate > DeliverabilityThresholds.ComplaintRateMax ? Color.FromArgb("#E02424") : ComplaintAlert ? Color.FromArgb("#E3A008") : Color.FromArgb("#0E9F6E");
}

public sealed partial class ReportsViewModel(
    AppStartup startup,
    IDialogService dialogs,
    ILogger<ReportsViewModel> logger,
    ReportService reports,
    ContactService contacts) : BaseViewModel(startup, dialogs, logger)
{
    public ObservableCollection<ReportRow> Rows { get; } = new();
    public ObservableCollection<string> History { get; } = new();

    [ObservableProperty] public partial string? HistoryEmail { get; set; }

    protected override async Task LoadAsync()
    {
        Rows.Clear();
        foreach (var r in await reports.GetAllAsync()) Rows.Add(new ReportRow(r));
    }

    [RelayCommand] private Task Refresh() => RunBusyAsync(LoadAsync);

    [RelayCommand] private Task ExportCsv(ReportRow row) => ExportAsync(row, "csv");

    [RelayCommand] private Task ExportXlsx(ReportRow row) => ExportAsync(row, "xlsx");

    private Task ExportAsync(ReportRow row, string extension) => RunBusyAsync(async () =>
    {
        var temp = Path.Combine(FileSystem.CacheDirectory, $"rapor-{row.Report.CampaignId}.{extension}");
        await reports.ExportCampaignAsync(row.Report.CampaignId, temp);
        await using var stream = File.OpenRead(temp);
        var result = await FileSaver.Default.SaveAsync($"{Sanitize(row.Report.Name)}-rapor.{extension}", stream, CancellationToken.None);
        if (result.IsSuccessful) await Dialogs.ToastAsync(Loc.T("Reports_Saved", result.FilePath));
    });

    [RelayCommand]
    private Task FindHistory() => RunBusyAsync(async () =>
    {
        History.Clear();
        if (string.IsNullOrWhiteSpace(HistoryEmail)) return;
        var rows = await contacts.GetHistoryAsync(HistoryEmail);
        if (rows.Count == 0) History.Add(Loc.T("ListDetail_NoHistory"));
        foreach (var (campaign, job) in rows)
            History.Add($"{campaign.Name} · {Loc.T($"Enum_SendJobStatus_{job.Status}")} · {job.SentUtc?.ToLocalTime().ToString("g") ?? "-"}{(job.LastError is null ? "" : " · " + job.LastError)}");
    });

    private static string Sanitize(string name)
    {
        var sb = new StringBuilder();
        foreach (var ch in name) sb.Append(Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch);
        return sb.ToString();
    }
}
