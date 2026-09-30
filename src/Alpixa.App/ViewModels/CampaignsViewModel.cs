using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Alpixa.App.Services;
using Alpixa.Core.Models;
using Alpixa.Infrastructure.Reports;
using Alpixa.Sending.Campaigns;
using Alpixa.Sending.Engine;
using Microsoft.Extensions.Logging;

namespace Alpixa.App.ViewModels;

public sealed partial class CampaignRow(Campaign campaign, CampaignReport report) : ObservableObject
{
    public Campaign Campaign { get; } = campaign;
    [ObservableProperty] public partial CampaignReport Report { get; set; } = report;
    [ObservableProperty] public partial CampaignStatus Status { get; set; } = campaign.Status;

    public bool CanPause => Status is CampaignStatus.Running or CampaignStatus.Scheduled or CampaignStatus.WaitingForQuota;
    public bool CanResume => Status is CampaignStatus.Paused or CampaignStatus.PausedBySafety;
    public bool CanCancel => Status is not (CampaignStatus.Completed or CampaignStatus.Cancelled or CampaignStatus.Draft);
    public double Progress => Report.Total == 0 ? 0 : (double)(Report.Sent + Report.Failed + Report.Skipped + Report.Uncertain) / Report.Total;

    partial void OnStatusChanged(CampaignStatus value)
    {
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanCancel));
    }

    partial void OnReportChanged(CampaignReport value) => OnPropertyChanged(nameof(Progress));
}

public sealed partial class CampaignsViewModel(
    AppStartup startup,
    IDialogService dialogs,
    ILogger<CampaignsViewModel> logger,
    CampaignService campaigns,
    CampaignRunner runner,
    ReportService reports) : BaseViewModel(startup, dialogs, logger)
{
    public ObservableCollection<CampaignRow> Campaigns { get; } = new();

    protected override async Task LoadAsync()
    {
        runner.StateChanged -= OnRunnerStateChanged;
        runner.StateChanged += OnRunnerStateChanged;
        await ReloadAsync();
    }

    public override void OnDisappearing() => runner.StateChanged -= OnRunnerStateChanged;

    private void OnRunnerStateChanged(object? sender, int campaignId) => MainThread.BeginInvokeOnMainThread(async () => await ReloadAsync());

    private async Task ReloadAsync()
    {
        var list = await campaigns.ListAsync(CancellationToken.None);
        Campaigns.Clear();
        foreach (var c in list) Campaigns.Add(new CampaignRow(c, await reports.GetCampaignReportAsync(c.Id)));
    }

    [RelayCommand] private Task New() => Shell.Current.GoToAsync(AppShell.CampaignWizardRoute);

    [RelayCommand]
    private Task Open(CampaignRow row)
        => Shell.Current.GoToAsync(AppShell.CampaignDetailRoute, new Dictionary<string, object> { ["id"] = row.Campaign.Id });

    [RelayCommand] private Task Pause(CampaignRow row) => RunBusyAsync(() => runner.PauseAsync(row.Campaign.Id));

    [RelayCommand] private Task Resume(CampaignRow row) => RunBusyAsync(() => campaigns.ResumeAsync(row.Campaign.Id, CancellationToken.None));

    [RelayCommand]
    private async Task Cancel(CampaignRow row)
    {
        if (!await Dialogs.ConfirmAsync(Loc.T("Campaigns_CancelTitle"), Loc.T("Campaigns_CancelConfirm", row.Campaign.Name), Loc.T("Campaigns_CancelYes"), Loc.T("Common_Back")))
            return;
        await RunBusyAsync(() => runner.CancelAsync(row.Campaign.Id));
    }

    [RelayCommand]
    private async Task Delete(CampaignRow row)
    {
        if (!await Dialogs.ConfirmAsync(Loc.T("Common_Delete"), Loc.T("Campaigns_DeleteConfirm", row.Campaign.Name), Loc.T("Common_Delete"), Loc.T("Common_Cancel")))
            return;
        await RunBusyAsync(async () =>
        {
            await campaigns.DeleteAsync(row.Campaign.Id, CancellationToken.None);
            Campaigns.Remove(row);
        });
    }
}

public sealed partial class CampaignDetailViewModel(
    AppStartup startup,
    IDialogService dialogs,
    ILogger<CampaignDetailViewModel> logger,
    CampaignService campaigns,
    CampaignRunner runner,
    ReportService reports) : BaseViewModel(startup, dialogs, logger), IQueryAttributable
{
    private int _id;
    private IDispatcherTimer? _timer;

    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial CampaignStatus Status { get; set; }
    [ObservableProperty] public partial string? PauseReason { get; set; }
    [ObservableProperty] public partial CampaignReport? Report { get; set; }
    [ObservableProperty] public partial double Progress { get; set; }
    [ObservableProperty] public partial string? RateText { get; set; }
    [ObservableProperty] public partial string? EtaText { get; set; }
    [ObservableProperty] public partial string? ModeText { get; set; }
    [ObservableProperty] public partial string? ScheduleText { get; set; }
    [ObservableProperty] public partial bool CanPause { get; set; }
    [ObservableProperty] public partial bool CanResume { get; set; }
    [ObservableProperty] public partial bool CanCancel { get; set; }

    public void ApplyQueryAttributes(IDictionary<string, object> query) => _id = Convert.ToInt32(query["id"]);

    protected override async Task LoadAsync()
    {
        runner.ProgressChanged -= OnProgress;
        runner.ProgressChanged += OnProgress;
        runner.StateChanged -= OnState;
        runner.StateChanged += OnState;
        _timer ??= Application.Current!.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(5);
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
        await RefreshAsync();
    }

    public override void OnDisappearing()
    {
        runner.ProgressChanged -= OnProgress;
        runner.StateChanged -= OnState;
        _timer?.Stop();
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        try { await RefreshAsync(); }
        catch (Exception ex) { Logger.LogDebug(ex, "Refresh failed"); }
    }

    private void OnState(object? sender, int id)
    {
        if (id == _id) MainThread.BeginInvokeOnMainThread(async () => await RefreshAsync());
    }

    private void OnProgress(object? sender, CampaignProgress p)
    {
        if (p.CampaignId != _id) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Progress = p.Fraction;
            RateText = Loc.T("Detail_Rate", p.MessagesPerMinute.ToString("0.0"));
            EtaText = p.EstimatedRemaining is { } eta ? Loc.T("Detail_Eta", FormatDuration(eta)) : null;
        });
    }

    private async Task RefreshAsync()
    {
        var campaign = (await campaigns.ListAsync(CancellationToken.None)).First(c => c.Id == _id);
        Name = campaign.Name;
        Status = campaign.Status;
        PauseReason = campaign.PauseReason;
        ModeText = campaign.Mode == SendMode.Sequential ? Loc.T("Wizard_ModeSequential") : Loc.T("Wizard_ModeBulkWith", campaign.Parallelism);
        if (campaign.DryRun) ModeText += " · " + Loc.T("Detail_DryRun");
        ScheduleText = campaign.Status == CampaignStatus.Scheduled && campaign.ScheduledUtc is { } s
            ? Loc.T("Detail_Scheduled", s.ToLocalTime().ToString("f"))
            : campaign.Status == CampaignStatus.WaitingForQuota && campaign.NextRunUtc is { } n ? Loc.T("Detail_NextRun", n.ToLocalTime().ToString("f")) : null;

        Report = await reports.GetCampaignReportAsync(_id);
        Progress = Report.Total == 0 ? 0 : (double)(Report.Sent + Report.Failed + Report.Skipped + Report.Uncertain) / Report.Total;
        var running = runner.IsRunning(_id);
        if (!running)
        {
            RateText = null;
            EtaText = null;
        }
        CanPause = running || Status is CampaignStatus.Scheduled or CampaignStatus.WaitingForQuota;
        CanResume = !running && Status is CampaignStatus.Paused or CampaignStatus.PausedBySafety;
        CanCancel = Status is not (CampaignStatus.Completed or CampaignStatus.Cancelled or CampaignStatus.Draft);
    }

    [RelayCommand] private Task Pause() => RunBusyAsync(async () => { await runner.PauseAsync(_id); await RefreshAsync(); });

    [RelayCommand] private Task Resume() => RunBusyAsync(async () => { await campaigns.ResumeAsync(_id, CancellationToken.None); await RefreshAsync(); });

    [RelayCommand]
    private async Task Cancel()
    {
        if (!await Dialogs.ConfirmAsync(Loc.T("Campaigns_CancelTitle"), Loc.T("Campaigns_CancelConfirm", Name), Loc.T("Campaigns_CancelYes"), Loc.T("Common_Back")))
            return;
        await RunBusyAsync(async () => { await runner.CancelAsync(_id); await RefreshAsync(); });
    }

    [RelayCommand] private Task OpenReports() => Shell.Current.GoToAsync("//reports");

    private static string FormatDuration(TimeSpan t)
        => t.TotalHours >= 1 ? Loc.T("Detail_Hours", (int)t.TotalHours, t.Minutes) : Loc.T("Detail_Minutes", Math.Max(1, (int)Math.Ceiling(t.TotalMinutes)));
}
