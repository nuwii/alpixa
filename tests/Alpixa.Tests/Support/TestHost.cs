using Alpixa.Core.Abstractions;
using Alpixa.Core.Models;
using Alpixa.Core.Options;
using Alpixa.Infrastructure;
using Alpixa.Infrastructure.Contacts;
using Alpixa.Infrastructure.Data;
using Alpixa.Infrastructure.Dns;
using Alpixa.Infrastructure.Settings;
using Alpixa.Sending;
using Alpixa.Sending.Throttling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Alpixa.Tests.Support;

public sealed class TestHost : IAsyncDisposable
{
    private readonly TempAppPaths _paths = new();

    private TestHost(Action<IServiceCollection>? configure)
    {
        Clock = new FakeClock(DateTime.UtcNow);
        Delay = new InstantDelay(Clock);
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IClock>(Clock);
        services.AddSingleton<ISecretStore>(Secrets);
        services.AddSingleton<IPlatformService>(Platform);
        services.AddSingleton<IDelayProvider>(Delay);
        services.AddAlpixaInfrastructure(_paths, new AlpixaOptions());
        services.AddSingleton<IDnsResolver>(Dns);
        services.AddAlpixaSending();
        services.AddSingleton<IMailTransportFactory>(Transports);
        configure?.Invoke(services);
        Services = services.BuildServiceProvider();
    }

    public FakeClock Clock { get; }
    public InstantDelay Delay { get; }
    public InMemorySecretStore Secrets { get; } = new();
    public NullPlatformService Platform { get; } = new();
    public FakeDnsResolver Dns { get; } = new();
    public ScriptedTransportFactory Transports { get; } = new();
    public ServiceProvider Services { get; }
    public IAppPaths Paths => _paths;

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    public static async Task<TestHost> CreateAsync(Action<IServiceCollection>? configure = null)
    {
        var host = new TestHost(configure);
        await host.Get<DatabaseInitializer>().InitializeAsync();
        var settings = host.Get<SettingsStore>();
        var sending = await settings.GetSendingAsync();
        sending.RetryBaseDelaySeconds = 0;
        await settings.SaveSendingAsync(sending);
        return host;
    }

    public async Task<AlpixaDbContext> DbAsync() => await Get<IDbContextFactory<AlpixaDbContext>>().CreateDbContextAsync();

    public async Task<SenderProfile> AddProfileAsync(Action<SenderProfile>? configure = null)
    {
        var profile = new SenderProfile
        {
            Name = "Test",
            Kind = TransportKind.LocalTest,
            SmtpHost = "localhost",
            SmtpPort = 2525,
            Security = ConnectionSecurity.None,
            Auth = AuthMethod.None,
            FromName = "Test Gonderen",
            FromAddress = "bulten@ornek.com",
            PostalAddress = "Ornek Mah. 1, Istanbul",
            DailyLimit = 100000,
            HourlyLimit = 100000,
            PerMinuteLimit = 100000,
            WarmupEnabled = false
        };
        configure?.Invoke(profile);
        await using var db = await DbAsync();
        db.SenderProfiles.Add(profile);
        await db.SaveChangesAsync();
        return profile;
    }

    public async Task<int> AddListAsync(IEnumerable<string> emails)
    {
        var list = await Get<ContactService>().CreateListAsync("Liste", consentConfirmed: true);
        var rows = new List<string[]> { new[] { "email", "ad" } };
        rows.AddRange(emails.Select((e, i) => new[] { e, $"Kisi{i}" }));
        var import = Get<ContactImportService>();
        var preview = import.Preview(rows);
        await import.ImportAsync(list.Id, rows, preview.Mapping, checkMx: false, null, CancellationToken.None);
        return list.Id;
    }

    public async Task<Campaign> AddCampaignAsync(int profileId, int listId, Action<Campaign>? configure = null)
    {
        await using var db = await DbAsync();
        var template = await db.Templates.FirstAsync();
        var campaign = new Campaign
        {
            Name = "Kampanya",
            SenderProfileId = profileId,
            ContactListId = listId,
            TemplateId = template.Id,
            Subject = "Merhaba {{ ad }}",
            HtmlBody = "<html><body><p>Merhaba {{ ad }}</p></body></html>",
            Mode = SendMode.Bulk,
            Parallelism = 3
        };
        configure?.Invoke(campaign);
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync();
        return campaign;
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        _paths.Dispose();
    }
}
