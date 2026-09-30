using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using Alpixa.App.Services;
using Alpixa.Core.Abstractions;
using Alpixa.Core.Models;
using Alpixa.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Alpixa.App.ViewModels;

public sealed partial class ProfilesViewModel(
    AppStartup startup,
    IDialogService dialogs,
    ILogger<ProfilesViewModel> logger,
    IDbContextFactory<AlpixaDbContext> dbFactory,
    IConnectionTester tester,
    ISecretStore secrets) : BaseViewModel(startup, dialogs, logger)
{
    public ObservableCollection<SenderProfile> Profiles { get; } = new();

    protected override async Task LoadAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var items = await db.SenderProfiles.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
        Profiles.Clear();
        foreach (var p in items) Profiles.Add(p);
    }

    [RelayCommand] private Task Add() => Shell.Current.GoToAsync(AppShell.ProfileEditRoute);

    [RelayCommand]
    private Task Edit(SenderProfile profile)
        => Shell.Current.GoToAsync(AppShell.ProfileEditRoute, new Dictionary<string, object> { ["id"] = profile.Id });

    [RelayCommand]
    private Task Test(SenderProfile profile) => RunBusyAsync(async () =>
    {
        var (ok, error) = await tester.TestSmtpAsync(profile, CancellationToken.None);
        if (ok) await Dialogs.AlertAsync(Loc.T("Profiles_TestOkTitle"), Loc.T("Profiles_TestOk", profile.SmtpHost));
        else await Dialogs.ShowErrorAsync(error!);
    }, Loc.T("Profiles_Testing"));

    [RelayCommand]
    private async Task Delete(SenderProfile profile)
    {
        if (!await Dialogs.ConfirmAsync(Loc.T("Common_Delete"), Loc.T("Profiles_DeleteConfirm", profile.Name), Loc.T("Common_Delete"), Loc.T("Common_Cancel")))
            return;
        await RunBusyAsync(async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            if (await db.Campaigns.AnyAsync(c => c.SenderProfileId == profile.Id && (c.Status == CampaignStatus.Running || c.Status == CampaignStatus.Scheduled || c.Status == CampaignStatus.WaitingForQuota)))
                throw new InvalidOperationException(Loc.T("Profiles_InUse"));
            await db.SenderProfiles.Where(p => p.Id == profile.Id).ExecuteDeleteAsync();
            await secrets.RemoveAsync(profile.SmtpSecretKey);
            await secrets.RemoveAsync(profile.ImapSecretKey);
            await secrets.RemoveAsync(profile.OAuthSecretKey);
            Profiles.Remove(profile);
        });
    }
}
