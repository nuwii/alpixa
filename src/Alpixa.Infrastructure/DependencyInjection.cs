using Alpixa.Core.Abstractions;
using Alpixa.Core.Options;
using Alpixa.Infrastructure.Auth;
using Alpixa.Infrastructure.Bounces;
using Alpixa.Infrastructure.Consent;
using Alpixa.Infrastructure.Contacts;
using Alpixa.Infrastructure.Data;
using Alpixa.Infrastructure.Dns;
using Alpixa.Infrastructure.Mail;
using Alpixa.Infrastructure.Maintenance;
using Alpixa.Infrastructure.Reports;
using Alpixa.Infrastructure.Settings;
using Alpixa.Infrastructure.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog;
using Serilog.Events;

namespace Alpixa.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAlpixaInfrastructure(this IServiceCollection services, IAppPaths paths, AlpixaOptions options)
    {
        Directory.CreateDirectory(paths.DataDirectory);
        Directory.CreateDirectory(paths.LogDirectory);

        services.AddSingleton(paths);
        services.AddSingleton(options);
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddDbContextFactory<AlpixaDbContext>(o => o.UseSqlite($"Data Source={paths.DatabasePath};Default Timeout=30"));
        services.AddHttpClient("oauth", c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient("endpoint", c => c.Timeout = TimeSpan.FromSeconds(30));

        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<SettingsStore>();
        services.AddSingleton<IDnsResolver, DnsClientResolver>();
        services.AddSingleton<IMxChecker, MxChecker>();
        services.AddSingleton<IDomainHealthService, DomainHealthService>();
        services.AddSingleton<ITemplateRenderer, ScribanTemplateRenderer>();
        services.AddSingleton<AssetStore>();
        services.AddSingleton<UnsubscribeSecretProvider>();
        services.AddSingleton<IMessageComposer, MessageComposer>();
        services.AddSingleton<IOAuthTokenProvider, OAuthTokenProvider>();
        services.AddSingleton<MailAuthenticator>();
        services.AddSingleton<IMailTransportFactory, MailTransportFactory>();
        services.AddSingleton<IConnectionTester, ConnectionTester>();
        services.AddSingleton<ISuppressionService, SuppressionService>();
        services.AddSingleton<SuppressionService>(sp => (SuppressionService)sp.GetRequiredService<ISuppressionService>());
        services.AddSingleton<ContactImportService>();
        services.AddSingleton<ContactService>();
        services.AddSingleton<DeliveryEventService>();
        services.AddSingleton<IBounceProcessor, ImapBounceProcessor>();
        services.AddSingleton<IUnsubscribeSync, EndpointSyncService>();
        services.AddSingleton<ReportService>();
        services.AddSingleton<MaintenanceService>();
        services.TryAddSingleton<IConsentProvider, NoopConsentProvider>();
        return services;
    }

    public static ILogger CreateLogger(IAppPaths paths)
        => new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(Path.Combine(paths.LogDirectory, "alpixa-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
}
