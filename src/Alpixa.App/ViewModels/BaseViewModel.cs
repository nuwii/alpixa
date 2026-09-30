using CommunityToolkit.Mvvm.ComponentModel;
using Alpixa.App.Services;
using Alpixa.Infrastructure.Mail;
using Microsoft.Extensions.Logging;

namespace Alpixa.App.ViewModels;

public abstract partial class BaseViewModel(AppStartup startup, IDialogService dialogs, ILogger logger) : ObservableObject
{
    protected IDialogService Dialogs { get; } = dialogs;
    protected ILogger Logger { get; } = logger;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string? BusyText { get; set; }

    public bool IsNotBusy => !IsBusy;

    public async Task OnAppearingAsync()
    {
        try
        {
            await startup.Ready;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            await HandleErrorAsync(ex);
        }
    }

    public virtual void OnDisappearing() { }

    protected virtual Task LoadAsync() => Task.CompletedTask;

    protected async Task RunBusyAsync(Func<Task> action, string? busyText = null)
    {
        if (IsBusy) return;
        IsBusy = true;
        BusyText = busyText ?? Loc.T("Common_Working");
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            await HandleErrorAsync(ex);
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
        }
    }

    protected async Task HandleErrorAsync(Exception ex)
    {
        if (ex is OperationCanceledException) return;
        Logger.LogError(ex, "UI action failed");
        if (ex is InvalidOperationException or NotSupportedException or InvalidDataException)
            await Dialogs.AlertAsync(Loc.T("Common_Error"), ex.Message);
        else
            await Dialogs.ShowErrorAsync(ErrorTranslator.Translate(ex));
    }
}
