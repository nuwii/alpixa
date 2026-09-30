using Alpixa.App.ViewModels;

namespace Alpixa.App.Views;

public partial class SettingsPage : AlpixaPage
{
    public SettingsPage(SettingsViewModel viewModel) : base(viewModel, "Settings")
    {
        InitializeComponent();
    }
}
