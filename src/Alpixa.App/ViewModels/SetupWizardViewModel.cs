using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Alpixa.App.Services;
using Alpixa.Core.Abstractions;
using Alpixa.Core.Models;
using Contact = Alpixa.Core.Models.Contact;
using Alpixa.Core.Rules;
using Alpixa.Infrastructure.Data;
using Alpixa.Infrastructure.Settings;
using Alpixa.Sending.Campaigns;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Alpixa.App.ViewModels;

public sealed partial class SetupWizardViewModel(
    AppStartup startup,
    IDialogService dialogs,
    ILogger<SetupWizardViewModel> logger,
    IDbContextFactory<AlpixaDbContext> dbFactory,
    SettingsStore settings,
    ISecretStore secrets,
    IConnectionTester tester,
    IOAuthTokenProvider oauth,
    IDomainHealthService health,
    CampaignService campaigns) : BaseViewModel(startup, dialogs, logger)
{
    private bool _loaded;

    public IReadOnlyList<string> Languages { get; } = LanguagePreference.Names;
    public IReadOnlyList<ProviderPreset> Presets { get; } = ProviderPresets.All.Where(p => p.Kind != TransportKind.LocalTest).ToList();
    public ObservableCollection<DomainCheck> Checks { get; } = new();
    public ObservableCollection<string> TestResults { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep1), nameof(IsStep2), nameof(IsStep3), nameof(IsStep4), nameof(CanGoBack), nameof(IsLastStep), nameof(StepTitle))]
    public partial int Step { get; set; } = 1;

    public bool IsStep1 => Step == 1;
    public bool IsStep2 => Step == 2;
    public bool IsStep3 => Step == 3;
    public bool IsStep4 => Step == 4;
    public bool CanGoBack => Step > 1;
    public bool IsLastStep => Step == 4;
    public string StepTitle => Loc.T("Setup_StepTitle", Step, Loc.T($"Setup_Step{Step}"));

    [ObservableProperty] public partial int LanguageIndex { get; set; }
    [ObservableProperty] public partial ProviderPreset? SelectedPreset { get; set; }
    [ObservableProperty] public partial string FromName { get; set; } = "";
    [ObservableProperty] public partial string FromAddress { get; set; } = "";
    [ObservableProperty] public partial string Username { get; set; } = "";
    [ObservableProperty] public partial string Password { get; set; } = "";
    [ObservableProperty] public partial string SmtpHost { get; set; } = "";
    [ObservableProperty] public partial string? DkimSelector { get; set; }
    [ObservableProperty] public partial bool UseBrowserSignIn { get; set; }
    [ObservableProperty] public partial bool IsOAuthCapable { get; set; }
    [ObservableProperty] public partial bool IsCustomHost { get; set; }
    [ObservableProperty] public partial bool ConnectionOk { get; set; }
    [ObservableProperty] public partial string? ConnectionMessage { get; set; }
    [ObservableProperty] public partial string? HealthSummary { get; set; }
    [ObservableProperty] public partial string SeedAddresses { get; set; } = "";
    [ObservableProperty] public partial int ProfileId { get; set; }

    protected override async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;
        LanguageIndex = LanguagePreference.CurrentIndex;
        SelectedPreset = Presets.First(p => p.Kind == TransportKind.AmazonSes);
        SeedAddresses = string.Join(", ", (await settings.GetGeneralAsync()).SeedAddresses);
    }

    partial void OnLanguageIndexChanged(int value)
    {
        if (!_loaded) return;
        var language = LanguagePreference.CodeAt(value);
        if (language == LanguagePreference.Current) return;
        LanguagePreference.Set(language);
        _ = Dialogs.AlertAsync(Loc.T("Settings_LanguageTitle"), Loc.T("Settings_LanguageRestart"));
    }

    partial void OnSelectedPresetChanged(ProviderPreset? value)
    {
        if (value is null) return;
        SmtpHost = value.SmtpHost;
        IsCustomHost = value.Kind is TransportKind.CustomSmtp or TransportKind.AmazonSes;
        IsOAuthCapable = value.Kind is TransportKind.Gmail or TransportKind.Microsoft365;
        UseBrowserSignIn = IsOAuthCapable && oauth.IsConfigured(value.DefaultAuth);
        ConnectionOk = false;
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
            case 2 when !ConnectionOk:
                await SaveAndTestAsync();
                if (!ConnectionOk) return;
                await RunHealthAsync();
                break;
            case 3:
                break;
        }
        Step++;
    });

    [RelayCommand]
    private Task Skip() => FinishAsync();

    [RelayCommand]
    private Task TestConnection() => RunBusyAsync(SaveAndTestAsync, Loc.T("Profiles_Testing"));

    private async Task SaveAndTestAsync()
    {
        if (SelectedPreset is null) return;
        if (string.IsNullOrWhiteSpace(FromAddress) && EmailAddressRules.IsValid(Username.Trim())) FromAddress = Username.Trim();
        if (!EmailAddressRules.IsValid(FromAddress)) throw new InvalidOperationException(Loc.T("ProfileEdit_NeedFrom"));
        if (string.IsNullOrWhiteSpace(FromName)) throw new InvalidOperationException(Loc.T("ProfileEdit_NeedFromName"));
        if (string.IsNullOrWhiteSpace(SmtpHost)) throw new InvalidOperationException(Loc.T("ProfileEdit_NeedHost"));

        await using var db = await dbFactory.CreateDbContextAsync();
        var profile = ProfileId == 0 ? new SenderProfile() : await db.SenderProfiles.FirstAsync(p => p.Id == ProfileId);
        ProviderPresets.Apply(SelectedPreset, profile);
        profile.Name = $"{SelectedPreset.DisplayName} - {FromAddress}";
        profile.SmtpHost = SmtpHost.Trim();
        profile.FromName = FromName.Trim();
        profile.FromAddress = FromAddress.Trim();
        profile.Username = string.IsNullOrWhiteSpace(Username) ? FromAddress.Trim() : Username.Trim();
        profile.DkimSelector = string.IsNullOrWhiteSpace(DkimSelector) ? null : DkimSelector.Trim();
        profile.Auth = IsOAuthCapable && UseBrowserSignIn ? SelectedPreset.DefaultAuth : AuthMethod.Password;
        if (ProfileId == 0) db.SenderProfiles.Add(profile);
        await db.SaveChangesAsync();
        ProfileId = profile.Id;

        if (profile.Auth == AuthMethod.Password)
        {
            if (string.IsNullOrEmpty(Password)) throw new InvalidOperationException(Loc.T("Setup_NeedPassword"));
            await secrets.SetAsync(profile.SmtpSecretKey, profile.Kind == TransportKind.Gmail ? Password.Replace(" ", "") : Password);
        }
        else
        {
            await oauth.SignInAsync(profile, CancellationToken.None);
        }

        var (ok, error) = await tester.TestSmtpAsync(profile, CancellationToken.None);
        ConnectionOk = ok;
        ConnectionMessage = ok ? Loc.T("Profiles_TestOk", profile.SmtpHost) : $"{error!.Message} {error.WhatToDo}";
    }

    [RelayCommand]
    private Task Recheck() => RunBusyAsync(RunHealthAsync, Loc.T("Health_Checking"));

    private async Task RunHealthAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var profile = await db.SenderProfiles.AsNoTracking().FirstAsync(p => p.Id == ProfileId);
        var report = await health.CheckAsync(profile, CancellationToken.None);
        Checks.Clear();
        foreach (var c in report.Checks) Checks.Add(c);
        HealthSummary = report.HasBlockingIssues ? Loc.T("Health_SummaryFail", report.Domain) : Loc.T("Health_SummaryOk", report.Domain);
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

        await using var db = await dbFactory.CreateDbContextAsync();
        var template = await db.Templates.AsNoTracking().OrderBy(t => t.Id).FirstAsync(t => t.IsBuiltIn);
        var draft = new Campaign
        {
            Name = Loc.T("Setup_TestCampaign"),
            SenderProfileId = ProfileId,
            TemplateId = template.Id,
            Subject = Loc.T("Setup_TestSubject"),
            HtmlBody = template.HtmlBody
        };
        var results = await campaigns.SendTestAsync(draft, seeds, CancellationToken.None);
        TestResults.Clear();
        foreach (var r in results)
            TestResults.Add(r.Success ? Loc.T("Wizard_TestOk", r.Email) : Loc.T("Wizard_TestFail", r.Email, r.Error));
    }, Loc.T("Wizard_Sending"));

    [RelayCommand]
    private Task Finish() => FinishAsync();

    private async Task FinishAsync()
    {
        var general = await settings.GetGeneralAsync();
        general.SetupCompleted = true;
        await settings.SaveGeneralAsync(general);
        await Shell.Current.GoToAsync("//dashboard");
    }
}
