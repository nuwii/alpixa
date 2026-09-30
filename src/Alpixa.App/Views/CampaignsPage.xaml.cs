using Alpixa.App.ViewModels;

namespace Alpixa.App.Views;

public partial class CampaignsPage : AlpixaPage
{
    public CampaignsPage(CampaignsViewModel viewModel) : base(viewModel, "Campaigns")
    {
        InitializeComponent();
    }
}
