using Alpixa.App.Views;

namespace Alpixa.App;

public partial class AppShell : Shell
{
    public const string ProfileEditRoute = "profileedit";
    public const string ImportRoute = "import";
    public const string ListDetailRoute = "listdetail";
    public const string TemplateEditRoute = "templateedit";
    public const string CampaignWizardRoute = "campaignwizard";
    public const string CampaignDetailRoute = "campaigndetail";
    public const string SetupRoute = "setup";

    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute(ProfileEditRoute, typeof(ProfileEditPage));
        Routing.RegisterRoute(ImportRoute, typeof(ImportPage));
        Routing.RegisterRoute(ListDetailRoute, typeof(ListDetailPage));
        Routing.RegisterRoute(TemplateEditRoute, typeof(TemplateEditPage));
        Routing.RegisterRoute(CampaignWizardRoute, typeof(CampaignWizardPage));
        Routing.RegisterRoute(CampaignDetailRoute, typeof(CampaignDetailPage));
        Routing.RegisterRoute(SetupRoute, typeof(SetupWizardPage));
    }
}
