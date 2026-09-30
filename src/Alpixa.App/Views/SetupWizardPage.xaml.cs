using Alpixa.App.Controls;
using Alpixa.App.ViewModels;

namespace Alpixa.App.Views;

public partial class SetupWizardPage : AlpixaPage
{
    private int _step;

    public SetupWizardPage(SetupWizardViewModel viewModel) : base(viewModel, "Setup")
    {
        InitializeComponent();
        _step = viewModel.Step;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(SetupWizardViewModel.Step) || viewModel.Step == _step) return;
            var forward = viewModel.Step > _step;
            _step = viewModel.Step;
            Motion.SlideIn(Steps, forward);
        };
    }
}
