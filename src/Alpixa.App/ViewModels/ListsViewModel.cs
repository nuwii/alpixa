using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Alpixa.App.Services;
using Alpixa.Core.Abstractions;
using Alpixa.Core.Models;
using Alpixa.Infrastructure.Contacts;
using Microsoft.Extensions.Logging;

namespace Alpixa.App.ViewModels;

public sealed partial class ListsViewModel(
    AppStartup startup,
    IDialogService dialogs,
    ILogger<ListsViewModel> logger,
    ContactService contacts,
    SuppressionService suppressions,
    IPlatformService platform) : BaseViewModel(startup, dialogs, logger)
{
    public ObservableCollection<ContactListSummary> Lists { get; } = new();
    public ObservableCollection<SuppressionEntry> Suppressions { get; } = new();

    [ObservableProperty] public partial string? SuppressionSearch { get; set; }
    [ObservableProperty] public partial int SuppressionTotal { get; set; }
    [ObservableProperty] public partial bool IsDragOver { get; set; }

    protected override async Task LoadAsync()
    {
        Lists.Clear();
        foreach (var l in await contacts.GetListSummariesAsync()) Lists.Add(l);
        await SearchSuppressionsAsync();
    }

    [RelayCommand] private Task Import() => Shell.Current.GoToAsync(AppShell.ImportRoute);

    [RelayCommand]
    private Task ImportInto(ContactListSummary list)
        => Shell.Current.GoToAsync(AppShell.ImportRoute, new Dictionary<string, object> { ["listId"] = list.Id });

    [RelayCommand]
    private Task Open(ContactListSummary list)
        => Shell.Current.GoToAsync(AppShell.ListDetailRoute, new Dictionary<string, object> { ["id"] = list.Id, ["name"] = list.Name });

    [RelayCommand]
    private async Task Delete(ContactListSummary list)
    {
        if (!await Dialogs.ConfirmAsync(Loc.T("Common_Delete"), Loc.T("Lists_DeleteConfirm", list.Name, list.Total), Loc.T("Common_Delete"), Loc.T("Common_Cancel")))
            return;
        await RunBusyAsync(async () =>
        {
            await contacts.DeleteListAsync(list.Id);
            Lists.Remove(list);
        });
    }

    [RelayCommand]
    private Task SearchSuppressions() => RunBusyAsync(SearchSuppressionsAsync);

    private async Task SearchSuppressionsAsync()
    {
        SuppressionTotal = await suppressions.CountAsync(CancellationToken.None);
        Suppressions.Clear();
        foreach (var s in await suppressions.ListAsync(SuppressionSearch, 200, CancellationToken.None)) Suppressions.Add(s);
    }

    [RelayCommand]
    private async Task RemoveSuppression(SuppressionEntry entry)
    {
        if (!await Dialogs.ConfirmAsync(Loc.T("Lists_UnsuppressTitle"), Loc.T("Lists_UnsuppressConfirm", entry.EmailNormalized), Loc.T("Lists_Unsuppress"), Loc.T("Common_Cancel")))
            return;
        await RunBusyAsync(async () =>
        {
            await suppressions.RemoveAsync(entry.EmailNormalized, CancellationToken.None);
            Suppressions.Remove(entry);
            SuppressionTotal--;
        });
    }

    [RelayCommand]
    private async Task AddSuppression()
    {
        var email = await Dialogs.PromptAsync(Loc.T("Lists_AddSuppressionTitle"), Loc.T("Lists_AddSuppressionMessage"));
        if (string.IsNullOrWhiteSpace(email)) return;
        await RunBusyAsync(async () =>
        {
            await suppressions.SuppressAsync(email, SuppressionReason.Manual, "manual", null, CancellationToken.None);
            await SearchSuppressionsAsync();
        });
    }

    public void OnDragOver(object? platformArgs)
    {
        IsDragOver = true;
        if (platformArgs is not null) platform.AcceptDragOver(platformArgs);
    }

    public async Task OnDropAsync(object? platformArgs)
    {
        IsDragOver = false;
        if (platformArgs is null) return;
        var files = await platform.GetDroppedFilePathsAsync(platformArgs);
        var file = files.FirstOrDefault(f => f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
        if (file is null)
        {
            await Dialogs.AlertAsync(Loc.T("Import_UnsupportedTitle"), Loc.T("Import_Unsupported"));
            return;
        }
        await Shell.Current.GoToAsync(AppShell.ImportRoute, new Dictionary<string, object> { ["file"] = file });
    }
}
