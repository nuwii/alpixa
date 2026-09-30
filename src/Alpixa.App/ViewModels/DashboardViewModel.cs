using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Alpixa.App.Services;
using Alpixa.Core.Models;
using Alpixa.Infrastructure.Contacts;
using Alpixa.Infrastructure.Data;
using Alpixa.Infrastructure.Reports;
using Alpixa.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Alpixa.App.ViewModels;

public sealed partial class DashboardViewModel(
    AppStartup startup,
    IDialogService dialogs,
    ILogger<DashboardViewModel> logger,
    IDbContextFactory<AlpixaDbContext> dbFactory,
    ReportService reports,
    SettingsStore settings) : BaseViewModel(startup, dialogs, logger)
{
    [ObservableProperty] public partial int ProfileCount { get; set; }
    [ObservableProperty] public partial int ContactCount { get; set; }
    [ObservableProperty] public partial int ListCount { get; set; }
    [ObservableProperty] public partial int SuppressedCount { get; set; }
    [ObservableProperty] public partial int SentLast30Days { get; set; }
    [ObservableProperty] public partial bool SetupIncomplete { get; set; }

    public string Greeting => DateTime.Now.Hour switch
    {
        < 5 => Loc.T("Dashboard_GreetingNight"),
        < 12 => Loc.T("Dashboard_GreetingMorning"),
        < 18 => Loc.T("Dashboard_GreetingDay"),
        _ => Loc.T("Dashboard_GreetingEvening")
    };
    [ObservableProperty] public partial bool HasNoProfile { get; set; }

    public ObservableCollection<CampaignReport> RecentCampaigns { get; } = new();
    public ObservableCollection<string> Warnings { get; } = new();

    protected override async Task LoadAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        ProfileCount = await db.SenderProfiles.CountAsync();
        ListCount = await db.ContactLists.CountAsync();
        ContactCount = await db.Contacts.CountAsync(c => c.Status == ContactStatus.Active);
        SuppressedCount = await db.Suppressions.CountAsync();
        var since = DateTime.UtcNow.AddDays(-30);
        SentLast30Days = await db.SendJobs.CountAsync(j => j.Status == SendJobStatus.Sent && j.SentUtc >= since);
        SetupIncomplete = !(await settings.GetGeneralAsync()).SetupCompleted;
        HasNoProfile = ProfileCount == 0;

        RecentCampaigns.Clear();
        foreach (var r in (await reports.GetAllAsync()).Take(5)) RecentCampaigns.Add(r);

        Warnings.Clear();
        foreach (var r in RecentCampaigns)
        {
            if (r.Status == CampaignStatus.PausedBySafety) Warnings.Add(Loc.T("Dashboard_WarnPaused", r.Name));
            if (r.Sent >= 50 && r.ComplaintRate > 0.001) Warnings.Add(Loc.T("Dashboard_WarnComplaints", r.Name, r.ComplaintRate.ToString("P2")));
            if (r.Sent >= 50 && r.BounceRate > 0.02) Warnings.Add(Loc.T("Dashboard_WarnBounces", r.Name, r.BounceRate.ToString("P1")));
        }
    }

    [RelayCommand] private Task NewCampaign() => Shell.Current.GoToAsync(AppShell.CampaignWizardRoute);
    [RelayCommand] private Task ImportList() => Shell.Current.GoToAsync(AppShell.ImportRoute);
    [RelayCommand] private Task AddProfile() => Shell.Current.GoToAsync(AppShell.ProfileEditRoute);
    [RelayCommand] private Task OpenSetup() => Shell.Current.GoToAsync(AppShell.SetupRoute);

    [RelayCommand]
    private Task OpenCampaign(CampaignReport report)
        => Shell.Current.GoToAsync(AppShell.CampaignDetailRoute, new Dictionary<string, object> { ["id"] = report.CampaignId });
}
