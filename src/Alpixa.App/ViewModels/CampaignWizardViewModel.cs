using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Alpixa.App.Services;
using Alpixa.Core.Models;
using Contact = Alpixa.Core.Models.Contact;
using Alpixa.Core.Rules;
using Alpixa.Infrastructure.Contacts;
using Alpixa.Infrastructure.Data;
using Alpixa.Infrastructure.Settings;
using Alpixa.Sending.Campaigns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Alpixa.App.ViewModels;

public sealed partial class CampaignWizardViewModel(
    AppStartup startup,
    IDialogService dialogs,
    ILogger<CampaignWizardViewModel> logger,
    IDbContextFactory<AlpixaDbContext> dbFactory,
    ContactService contacts,
    CampaignService campaigns,
    SettingsStore settings) : BaseViewModel(startup, dialogs, logger)
{
    public const int StepCount = 6;
    private bool _loaded;
    private PreflightResult? _preflight;

    public ObservableCollection<SenderProfile> Profiles { get; } = new();
    public ObservableCollection<ContactListSummary> Lists { get; } = new();
    public ObservableCollection<EmailTemplate> Templates { get; } = new();
    public ObservableCollection<DomainCheck> HealthChecks { get; } = new();
    public ObservableCollection<LintIssue> LintIssues { get; } = new();
    public ObservableCollection<string> TestResults { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep1), nameof(IsStep2), nameof(IsStep3), nameof(IsStep4), nameof(IsStep5), nameof(IsStep6), nameof(CanGoBack), nameof(CanGoNext), nameof(StepTitle))]
    public partial int Step { get; set; } = 1;

    public bool IsStep1 => Step == 1;
    public bool IsStep2 => Step == 2;
    public bool IsStep3 => Step == 3;
    public bool IsStep4 => Step == 4;
    public bool IsStep5 => Step == 5;
    public bool IsStep6 => Step == 6;
    public bool CanGoBack => Step > 1;
    public bool CanGoNext => Step < StepCount;
    public string StepTitle => Loc.T("Wizard_StepTitle", Step, StepCount, Loc.T($"Wizard_Step{Step}"));

    [ObservableProperty] public partial SenderProfile? SelectedProfile { get; set; }
    [ObservableProperty] public partial ContactListSummary? SelectedList { get; set; }
    [ObservableProperty] public partial EmailTemplate? SelectedTemplate { get; set; }
    [ObservableProperty] public partial string CampaignName { get; set; } = "";
    [ObservableProperty] public partial string Subject { get; set; } = "";

    [ObservableProperty] public partial string? HealthSummary { get; set; }
    [ObservableProperty] public partial bool HealthBlocks { get; set; }
    [ObservableProperty] public partial bool LintBlocks { get; set; }
    [ObservableProperty] public partial bool HealthOverride { get; set; }
    [ObservableProperty] public partial string? QuotaText { get; set; }
    [ObservableProperty] public partial string? RecipientsText { get; set; }
    [ObservableProperty] public partial string? TemplateErrors { get; set; }
    [ObservableProperty] public partial string SeedAddresses { get; set; } = "";
    [ObservableProperty] public partial bool TestSent { get; set; }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsBulk))] public partial bool IsSequential { get; set; } = true;
    public bool IsBulk => !IsSequential;
    [ObservableProperty] public partial int Parallelism { get; set; } = 2;
    [ObservableProperty] public partial int MaxParallelism { get; set; } = 5;
    [ObservableProperty] public partial bool DryRun { get; set; }
    [ObservableProperty] public partial bool TrackOpens { get; set; }
    [ObservableProperty] public partial bool ScheduleLater { get; set; }
    [ObservableProperty] public partial DateTime ScheduleDate { get; set; } = DateTime.Today.AddDays(1);
    [ObservableProperty] public partial TimeSpan ScheduleTime { get; set; } = new(9, 0, 0);
    [ObservableProperty] public partial string? Summary { get; set; }

    protected override async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;
        await using var db = await dbFactory.CreateDbContextAsync();
        foreach (var p in await db.SenderProfiles.AsNoTracking().OrderBy(p => p.Name).ToListAsync()) Profiles.Add(p);
        foreach (var t in await db.Templates.AsNoTracking().OrderByDescending(t => t.UpdatedUtc).ToListAsync()) Templates.Add(t);
        foreach (var l in await contacts.GetListSummariesAsync()) Lists.Add(l);
        SelectedProfile = Profiles.FirstOrDefault();
        SelectedList = Lists.FirstOrDefault();
        SelectedTemplate = Templates.FirstOrDefault();

        var general = await settings.GetGeneralAsync();
        SeedAddresses = string.Join(", ", general.SeedAddresses);
        TrackOpens = general.OpenTrackingDefault;
        var sending = await settings.GetSendingAsync();
        Parallelism = sending.DefaultBulkParallelism;
        MaxParallelism = sending.MaxBulkParallelism;
        CampaignName = Loc.T("Wizard_DefaultName", DateTime.Now.ToString("d MMM yyyy"));

        if (Profiles.Count == 0) await Dialogs.AlertAsync(Loc.T("Wizard_NoProfileTitle"), Loc.T("Wizard_NoProfile"));
        else if (Lists.Count == 0) await Dialogs.AlertAsync(Loc.T("Wizard_NoListTitle"), Loc.T("Wizard_NoList"));
    }

    partial void OnSelectedTemplateChanged(EmailTemplate? value)
    {
        if (value is not null) Subject = value.Subject;
    }

    [RelayCommand]
    private void Back()
    {
        if (Step > 1) Step--;
    }

    [RelayCommand]
    private Task Next() => RunBusyAsync(async () =>
    {
        switch (Step)
        {
            case 1 when SelectedProfile is null:
                throw new InvalidOperationException(Loc.T("Wizard_NeedProfile"));
            case 2 when SelectedList is null:
                throw new InvalidOperationException(Loc.T("Wizard_NeedList"));
            case 3 when SelectedTemplate is null || string.IsNullOrWhiteSpace(Subject) || string.IsNullOrWhiteSpace(CampaignName):
                throw new InvalidOperationException(Loc.T("Wizard_NeedTemplate"));
            case 3:
                await RunPreflightAsync();
                break;
            case 4 when _preflight is null:
                await RunPreflightAsync();
                break;
            case 4 when !_preflight!.CanStart(HealthOverride):
                throw new InvalidOperationException(Loc.T("Wizard_Blocked"));
            case 5:
                BuildSummary();
                break;
        }
        Step++;
    });

    [RelayCommand]
    private Task Recheck() => RunBusyAsync(RunPreflightAsync, Loc.T("Wizard_Checking"));

    private async Task RunPreflightAsync()
    {
        var draft = BuildDraft();
        _preflight = await campaigns.PreflightAsync(draft, CancellationToken.None);

        HealthChecks.Clear();
        foreach (var c in _preflight.Health.Checks) HealthChecks.Add(c);
        LintIssues.Clear();
        foreach (var i in _preflight.Lint) LintIssues.Add(i);

        HealthBlocks = _preflight.HealthBlocks;
        LintBlocks = _preflight.LintBlocks;
        HealthSummary = _preflight.HealthBlocks ? Loc.T("Wizard_HealthBlocks") : Loc.T("Wizard_HealthOk");
        TemplateErrors = _preflight.TemplateErrors.Count == 0 ? null : string.Join("\n", _preflight.TemplateErrors);
        RecipientsText = Loc.T("Wizard_Recipients", _preflight.Recipients);
        var q = _preflight.Quota;
        QuotaText = q.WarmupActive
            ? Loc.T("Wizard_QuotaWarmup", q.WarmupDay, q.DailyLimit, q.SentToday, EstimateDays(_preflight.Recipients, q))
            : Loc.T("Wizard_Quota", q.DailyLimit, q.SentToday);
    }

    private static int EstimateDays(int recipients, Alpixa.Sending.Queue.QuotaStatus quota)
    {
        var plan = WarmupPlan.Default();
        var remaining = recipients;
        var days = 0;
        var day = quota.WarmupDay;
        var today = quota.Remaining;
        while (remaining > 0 && days < 365)
        {
            remaining -= days == 0 ? today : plan.LimitForDay(day);
            days++;
            day++;
        }
        return Math.Max(1, days);
    }

    [RelayCommand]
    private Task SendTest() => RunBusyAsync(async () =>
    {
        var seeds = SeedAddresses.Split([',', ';', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(EmailAddressRules.IsValid).ToList();
        if (seeds.Count == 0) throw new InvalidOperationException(Loc.T("Wizard_NeedSeeds"));

        var general = await settings.GetGeneralAsync();
        general.SeedAddresses = seeds;
        await settings.SaveGeneralAsync(general);

        var results = await campaigns.SendTestAsync(BuildDraft(), seeds, CancellationToken.None);
        TestResults.Clear();
        foreach (var r in results)
            TestResults.Add(r.Success ? Loc.T("Wizard_TestOk", r.Email) : Loc.T("Wizard_TestFail", r.Email, r.Error));
        TestSent = results.Any(r => r.Success);
    }, Loc.T("Wizard_Sending"));

    [RelayCommand]
    private Task OpenDomainHealth() => Shell.Current.GoToAsync("//domainhealth");

    private void BuildSummary()
    {
        var when = ScheduleLater ? ScheduleDate.Date.Add(ScheduleTime).ToString("f") : Loc.T("Wizard_Now");
        var mode = IsSequential ? Loc.T("Wizard_ModeSequential") : Loc.T("Wizard_ModeBulkWith", Parallelism);
        Summary = Loc.T("Wizard_Summary", CampaignName, SelectedProfile?.FromAddress, SelectedList?.Name, _preflight?.Recipients ?? 0,
            SelectedTemplate?.Name, Subject, mode, when,
            DryRun ? Loc.T("Common_Yes") : Loc.T("Common_No"), TrackOpens ? Loc.T("Common_Yes") : Loc.T("Common_No"));
    }

    [RelayCommand]
    private Task Launch() => RunBusyAsync(async () =>
    {
        if (_preflight is null || !_preflight.CanStart(HealthOverride)) throw new InvalidOperationException(Loc.T("Wizard_Blocked"));
        if (ScheduleLater && ScheduleDate.Date.Add(ScheduleTime) <= DateTime.Now) throw new InvalidOperationException(Loc.T("Wizard_PastSchedule"));

        var draft = BuildDraft();
        draft.Status = CampaignStatus.Draft;
        await campaigns.SaveDraftAsync(draft, CancellationToken.None);
        await campaigns.LaunchAsync(draft.Id, HealthOverride, CancellationToken.None);
        await Shell.Current.GoToAsync($"//campaigns/{AppShell.CampaignDetailRoute}", new Dictionary<string, object> { ["id"] = draft.Id });
    }, Loc.T("Wizard_Starting"));

    private Campaign BuildDraft()
    {
        var template = SelectedTemplate!;
        return new Campaign
        {
            Name = CampaignName.Trim(),
            SenderProfileId = SelectedProfile!.Id,
            ContactListId = SelectedList!.Id,
            TemplateId = template.Id,
            Subject = Subject,
            HtmlBody = template.HtmlBody,
            TextBody = template.TextBody,
            Mode = IsSequential ? SendMode.Sequential : SendMode.Bulk,
            Parallelism = Math.Clamp(Parallelism, 1, MaxParallelism),
            DryRun = DryRun,
            TrackOpens = TrackOpens,
            ScheduledUtc = ScheduleLater ? DateTime.SpecifyKind(ScheduleDate.Date.Add(ScheduleTime), DateTimeKind.Local).ToUniversalTime() : null
        };
    }
}
