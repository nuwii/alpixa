using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Alpixa.App.Services;
using Alpixa.Core.Abstractions;
using Alpixa.Core.Models;
using Alpixa.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Alpixa.App.ViewModels;

public sealed partial class DomainHealthViewModel(
    AppStartup startup,
    IDialogService dialogs,
    ILogger<DomainHealthViewModel> logger,
    IDbContextFactory<AlpixaDbContext> dbFactory,
    IDomainHealthService health) : BaseViewModel(startup, dialogs, logger)
{
    public ObservableCollection<SenderProfile> Profiles { get; } = new();
    public ObservableCollection<DomainCheck> Checks { get; } = new();

    [ObservableProperty] public partial SenderProfile? SelectedProfile { get; set; }
    [ObservableProperty] public partial string? Summary { get; set; }
    [ObservableProperty] public partial CheckStatus Overall { get; set; }
    [ObservableProperty] public partial bool HasReport { get; set; }

    protected override async Task LoadAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var items = await db.SenderProfiles.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
        var previous = SelectedProfile?.Id;
        Profiles.Clear();
        foreach (var p in items) Profiles.Add(p);
        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == previous) ?? Profiles.FirstOrDefault();
    }

    partial void OnSelectedProfileChanged(SenderProfile? value)
    {
        if (value is not null) CheckCommand.Execute(null);
    }

    [RelayCommand]
    private Task Check() => RunBusyAsync(async () =>
    {
        if (SelectedProfile is null) return;
        var report = await health.CheckAsync(SelectedProfile, CancellationToken.None);
        Checks.Clear();
        foreach (var c in report.Checks) Checks.Add(c);
        Overall = report.Overall;
        HasReport = true;
        Summary = report.HasBlockingIssues
            ? Loc.T("Health_SummaryFail", report.Domain)
            : report.Overall == CheckStatus.Warning ? Loc.T("Health_SummaryWarn", report.Domain) : Loc.T("Health_SummaryOk", report.Domain);
    }, Loc.T("Health_Checking"));

    [RelayCommand]
    private async Task Copy(DomainCheck check)
    {
        if (string.IsNullOrEmpty(check.SuggestedRecord)) return;
        await Clipboard.Default.SetTextAsync(check.SuggestedRecord);
        await Dialogs.ToastAsync(Loc.T("Common_Copied"));
    }
}
