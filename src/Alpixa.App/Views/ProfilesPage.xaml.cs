using Alpixa.App.ViewModels;

namespace Alpixa.App.Views;

public partial class ProfilesPage : AlpixaPage
{
    public ProfilesPage(ProfilesViewModel viewModel) : base(viewModel, "Profiles")
    {
        InitializeComponent();
    }
}
