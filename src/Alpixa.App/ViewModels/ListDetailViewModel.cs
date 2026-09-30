using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Alpixa.App.Services;
using Alpixa.Core.Models;
using Contact = Alpixa.Core.Models.Contact;
using Alpixa.Infrastructure.Contacts;
using Microsoft.Extensions.Logging;

namespace Alpixa.App.ViewModels;

public sealed partial class ListDetailViewModel(
    AppStartup startup,
    IDialogService dialogs,
    ILogger<ListDetailViewModel> logger,
    ContactService contacts) : BaseViewModel(startup, dialogs, logger), IQueryAttributable
{
    private const int PageSize = 200;
    private int _listId;

    public ObservableCollection<Contact> Contacts { get; } = new();
    public IReadOnlyList<string> StatusFilters { get; } =
        [Loc.T("ListDetail_All"), .. Enum.GetValues<ContactStatus>().Select(s => Loc.T($"Enum_ContactStatus_{s}"))];

    [ObservableProperty] public partial string ListName { get; set; } = "";
    [ObservableProperty] public partial string? Search { get; set; }
    [ObservableProperty] public partial int SelectedStatusIndex { get; set; }
    [ObservableProperty] public partial bool CanLoadMore { get; set; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _listId = Convert.ToInt32(query["id"]);
        ListName = query.TryGetValue("name", out var n) ? n as string ?? "" : "";
    }

    protected override Task LoadAsync() => ReloadAsync();

    partial void OnSelectedStatusIndexChanged(int value) => _ = ReloadAsync();

    [RelayCommand] private Task Find() => ReloadAsync();

    [RelayCommand]
    private Task LoadMore() => RunBusyAsync(async () => await AppendAsync(Contacts.Count));

    private Task ReloadAsync() => RunBusyAsync(async () =>
    {
        Contacts.Clear();
        await AppendAsync(0);
    });

    private async Task AppendAsync(int skip)
    {
        ContactStatus? status = SelectedStatusIndex <= 0 ? null : (ContactStatus)(SelectedStatusIndex - 1);
        var page = await contacts.GetContactsAsync(_listId, Search, status, skip, PageSize);
        foreach (var c in page) Contacts.Add(c);
        CanLoadMore = page.Count == PageSize;
    }

    [RelayCommand]
    private Task History(Contact contact) => RunBusyAsync(async () =>
    {
        var history = await contacts.GetHistoryAsync(contact.EmailNormalized);
        var sb = new StringBuilder();
        if (history.Count == 0) sb.Append(Loc.T("ListDetail_NoHistory"));
        foreach (var (campaign, job) in history.Take(30))
            sb.AppendLine($"{campaign.Name}: {Loc.T($"Enum_SendJobStatus_{job.Status}")} {(job.SentUtc?.ToLocalTime().ToString("g") ?? "")}");
        await Dialogs.AlertAsync(contact.Email, sb.ToString());
    });
}
