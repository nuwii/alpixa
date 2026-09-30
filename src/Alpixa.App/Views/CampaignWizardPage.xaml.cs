using Alpixa.App.Controls;
using Alpixa.App.ViewModels;

namespace Alpixa.App.Views;

public partial class CampaignWizardPage : AlpixaPage
{
    private int _step;

    public CampaignWizardPage(CampaignWizardViewModel viewModel) : base(viewModel, "CampaignWizard")
    {
        InitializeComponent();
        _step = viewModel.Step;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(CampaignWizardViewModel.Step) || viewModel.Step == _step) return;
            var forward = viewModel.Step > _step;
            _step = viewModel.Step;
            Motion.SlideIn(Steps, forward);
        };
    }
}
