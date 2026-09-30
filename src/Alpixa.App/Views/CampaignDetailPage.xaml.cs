using Alpixa.App.ViewModels;

namespace Alpixa.App.Views;

public partial class CampaignDetailPage : AlpixaPage
{
    public CampaignDetailPage(CampaignDetailViewModel viewModel) : base(viewModel, "CampaignDetail")
    {
        InitializeComponent();
    }
}
