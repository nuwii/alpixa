using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Maui;
using Alpixa.App.Controls;
using Alpixa.App.Services;
using Alpixa.App.ViewModels;
using Alpixa.App.Views;
using Alpixa.Core.Abstractions;
using Alpixa.Core.Options;
using Alpixa.Infrastructure;
using Alpixa.Infrastructure.Maintenance;
using Alpixa.Sending;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Alpixa.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        LanguagePreference.Apply();

        var paths = new MauiAppPaths();
        Directory.CreateDirectory(paths.DataDirectory);
        MaintenanceService.ApplyPendingRestore(paths);
        Directory.CreateDirectory(paths.LogDirectory);
        Log.Logger = Infrastructure.DependencyInjection.CreateLogger(paths);

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureMauiHandlers(handlers =>
            {
#if MACCATALYST
                handlers.AddHandler<Picker, MenuPickerHandler>();
#endif
            });
        InputChrome.Configure();

        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(Log.Logger, dispose: true);
#if DEBUG
        builder.Logging.AddDebug();
#endif

        var services = builder.Services;
#if MACCATALYST
        services.AddSingleton<ISecretStore, KeychainSecretStore>();
#else
        services.AddSingleton<ISecretStore, MauiSecretStore>();
#endif
        services.AddSingleton<IPlatformService, PlatformService>();
        services.AddAlpixaInfrastructure(paths, LoadOptions(paths));
        services.AddAlpixaSending();

        services.AddSingleton<AppStartup>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<HelpService>();
        services.AddSingleton<AppShell>();

        services.AddTransient<DashboardViewModel>();
        services.AddTransient<ProfilesViewModel>();
        services.AddTransient<ProfileEditViewModel>();
        services.AddTransient<DomainHealthViewModel>();
        services.AddTransient<ListsViewModel>();
        services.AddTransient<ImportViewModel>();
        services.AddTransient<ListDetailViewModel>();
        services.AddTransient<TemplatesViewModel>();
        services.AddTransient<TemplateEditViewModel>();
        services.AddTransient<CampaignsViewModel>();
        services.AddTransient<CampaignWizardViewModel>();
        services.AddTransient<CampaignDetailViewModel>();
        services.AddTransient<ReportsViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<SetupWizardViewModel>();

        services.AddTransient<DashboardPage>();
        services.AddTransient<ProfilesPage>();
        services.AddTransient<ProfileEditPage>();
        services.AddTransient<DomainHealthPage>();
        services.AddTransient<ListsPage>();
        services.AddTransient<ImportPage>();
        services.AddTransient<ListDetailPage>();
        services.AddTransient<TemplatesPage>();
        services.AddTransient<TemplateEditPage>();
        services.AddTransient<CampaignsPage>();
        services.AddTransient<CampaignWizardPage>();
        services.AddTransient<CampaignDetailPage>();
        services.AddTransient<ReportsPage>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<SetupWizardPage>();

        return builder.Build();
    }

    private static AlpixaOptions LoadOptions(IAppPaths paths)
    {
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
        var nodeOptions = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip };
        var merged = new JsonObject();

        using (var stream = typeof(MauiProgram).Assembly.GetManifestResourceStream("Alpixa.App.appsettings.json"))
        {
            if (stream is not null && JsonNode.Parse(stream, documentOptions: nodeOptions) is JsonObject embedded)
                merged = embedded;
        }

        // The data-folder file only overrides the values it contains (e.g. OAuth ids saved from Settings).
        var overridePath = Path.Combine(paths.DataDirectory, "appsettings.json");
        if (File.Exists(overridePath))
        {
            try
            {
                if (JsonNode.Parse(File.ReadAllText(overridePath), documentOptions: nodeOptions) is JsonObject local)
                    Merge(merged, local);
            }
            catch (JsonException ex)
            {
                Log.Warning(ex, "Ignoring invalid appsettings.json override");
            }
        }
        return merged.Deserialize<AlpixaOptions>(jsonOptions) ?? new AlpixaOptions();
    }

    private static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var (key, value) in source.ToList())
        {
            var existing = target.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
            if (existing.Value is JsonObject targetChild && value is JsonObject sourceChild)
            {
                Merge(targetChild, sourceChild);
                continue;
            }
            if (existing.Key is not null) target.Remove(existing.Key);
            target[key] = value?.DeepClone();
        }
    }
}
