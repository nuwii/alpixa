using Alpixa.App.ViewModels;

namespace Alpixa.App.Views;

public partial class ProfileEditPage : AlpixaPage
{
    public ProfileEditPage(ProfileEditViewModel viewModel) : base(viewModel, "ProfileEdit")
    {
        InitializeComponent();
    }
}
