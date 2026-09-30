using Alpixa.App.Controls;
using Alpixa.App.Services;
using Alpixa.App.ViewModels;

namespace Alpixa.App.Views;

public abstract class AlpixaPage : ContentPage
{
    private readonly string _helpKey;

    protected AlpixaPage(BaseViewModel viewModel, string helpKey)
    {
        _helpKey = helpKey;
        BindingContext = viewModel;
        ViewModel = viewModel;
        Shell.SetNavBarIsVisible(this, false);

        var modifier = DeviceInfo.Platform == DevicePlatform.MacCatalyst ? KeyboardAcceleratorModifiers.Cmd : KeyboardAcceleratorModifiers.Ctrl;
        var file = new MenuBarItem { Text = Loc.T("Menu_File") };
        file.Add(Shortcut(Loc.T("Menu_NewCampaign"), modifier, "N", () => Shell.Current.GoToAsync(AppShell.CampaignWizardRoute)));
        file.Add(Shortcut(Loc.T("Menu_ImportList"), modifier, "I", () => Shell.Current.GoToAsync(AppShell.ImportRoute)));
        file.Add(Shortcut(Loc.T("Menu_Settings"), modifier, ",", () => Shell.Current.GoToAsync("//settings")));
        MenuBarItems.Add(file);

        var help = new MenuBarItem { Text = Loc.T("Menu_Help") };
        help.Add(new MenuFlyoutItem { Text = Loc.T("Menu_HelpThisPage"), Command = new Command(async () => await Help.ShowAsync(_helpKey)) });
        MenuBarItems.Add(help);
    }

    protected BaseViewModel ViewModel { get; }

    public Task ShowHelpAsync() => Help.ShowAsync(_helpKey);

    private static HelpService Help => IPlatformApplication.Current!.Services.GetRequiredService<HelpService>();

    private static MenuFlyoutItem Shortcut(string text, KeyboardAcceleratorModifiers modifiers, string key, Func<Task> action)
    {
        var item = new MenuFlyoutItem { Text = text, Command = new Command(async () => await action()) };
        item.KeyboardAccelerators.Add(new KeyboardAccelerator { Modifiers = modifiers, Key = key });
        return item;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        Motion.PrepareEntrance(this);
        Dispatcher.Dispatch(() => Motion.PlayEntrance(this));
        await ViewModel.OnAppearingAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        ViewModel.OnDisappearing();
    }
}
