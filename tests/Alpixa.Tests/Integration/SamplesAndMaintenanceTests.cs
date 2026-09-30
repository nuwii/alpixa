using System.IO.Compression;
using System.Net;
using System.Text;
using Alpixa.Core.Abstractions;
using Alpixa.Core.Content;
using Alpixa.Core.Models;
using Alpixa.Core.Security;
using Alpixa.Infrastructure.Bounces;
using Alpixa.Infrastructure.Contacts;
using Alpixa.Infrastructure.Mail;
using Alpixa.Infrastructure.Maintenance;
using Alpixa.Infrastructure.Settings;
using Alpixa.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Alpixa.Tests.Integration;

public class SamplesAndMaintenanceTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Alpixa.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found");
    }

    [Fact]
    public async Task Sample_list_imports_cleanly()
    {
        await using var host = await TestHost.CreateAsync();
        var list = await host.Get<ContactService>().CreateListAsync("Ornek", true);
        var import = host.Get<ContactImportService>();
        var path = Path.Combine(RepoRoot(), "samples", "ornek-liste.csv");

        var preview = import.Preview(TabularReader.ReadFile(path));
        preview.Mapping.EmailColumn.Should().Be(0);
        preview.Mapping.CustomColumns.Values.Should().Contain("sehir");

        var summary = await import.ImportAsync(list.Id, TabularReader.ReadFile(path), preview.Mapping, checkMx: false, null, CancellationToken.None);
        summary.Imported.Should().Be(3);
        summary.InvalidSyntax.Should().Be(0);
    }

    [Fact]
    public async Task Sample_template_renders_without_lint_errors()
    {
        await using var host = await TestHost.CreateAsync();
        var html = await File.ReadAllTextAsync(Path.Combine(RepoRoot(), "samples", "ornek-sablon.html"));
        var renderer = host.Get<ITemplateRenderer>();

        renderer.Validate(html).Should().BeEmpty();
        ContentLinter.Lint("Etkinlik daveti", html).Should().NotContain(i => i.Severity == LintSeverity.Error);

        var composer = host.Get<IMessageComposer>();
        var context = await composer.CreateContextAsync(CancellationToken.None);
        var message = composer.Compose(
            new Campaign { Id = 1, Subject = "Etkinlik daveti", HtmlBody = html },
            new SenderProfile { FromName = "Ornek", FromAddress = "bulten@ornek.com", PostalAddress = "Istanbul" },
            new Contact { Email = "ayse@ornek.com", EmailNormalized = "ayse@ornek.com", FirstName = "Ayse", CustomFieldsJson = "{\"sehir\":\"Izmir\"}" },
            context);
        message.HtmlBody.Should().Contain("Merhaba Ayse").And.Contain("Izmir yakında").And.Contain("sizi aramızda");
    }

    [Fact]
    public async Task Backup_and_support_bundle_contain_expected_files_without_secrets()
    {
        await using var host = await TestHost.CreateAsync();
        await host.AddProfileAsync();
        await host.Secrets.SetAsync("profile:1:smtp", "cok-gizli-sifre");
        var maintenance = host.Get<MaintenanceService>();

        var backup = Path.Combine(host.Paths.DataDirectory, "yedek.zip");
        await maintenance.CreateBackupAsync(backup);
        using (var zip = ZipFile.OpenRead(backup))
            zip.Entries.Select(e => e.FullName).Should().Contain(["alpixa.db", "backup-info.json"]);

        Directory.CreateDirectory(host.Paths.LogDirectory);
        await File.WriteAllTextAsync(Path.Combine(host.Paths.LogDirectory, "alpixa-20260101.log"), "log satiri");
        var bundle = Path.Combine(host.Paths.DataDirectory, "destek.zip");
        await maintenance.CreateSupportBundleAsync(bundle);
        using var bundleZip = ZipFile.OpenRead(bundle);
        bundleZip.Entries.Select(e => e.FullName).Should().Contain(["diagnostics.json", "logs/alpixa-20260101.log"]);
        using var reader = new StreamReader(bundleZip.GetEntry("diagnostics.json")!.Open());
        (await reader.ReadToEndAsync()).Should().NotContain("cok-gizli-sifre").And.NotContain("bulten@ornek.com");
    }

    [Fact]
    public async Task Staged_restore_replaces_database_on_next_start()
    {
        await using var host = await TestHost.CreateAsync();
        var maintenance = host.Get<MaintenanceService>();
        var backup = Path.Combine(host.Paths.DataDirectory, "yedek.zip");
        await maintenance.CreateBackupAsync(backup);

        await maintenance.StageRestoreAsync(backup);
        File.Exists(Path.Combine(host.Paths.DataDirectory, MaintenanceService.PendingRestoreFileName)).Should().BeTrue();
    }

    [Fact]
    public async Task Endpoint_sync_applies_one_click_unsubscribes()
    {
        var handler = new StubHandler();
        await using var host = await TestHost.CreateAsync(services =>
            services.AddHttpClient("endpoint").ConfigurePrimaryHttpMessageHandler(() => handler));
        var listId = await host.AddListAsync(["ayse@ornek.com", "ali@ornek.com"]);

        var settings = host.Get<SettingsStore>();
        var general = await settings.GetGeneralAsync();
        general.EndpointBaseUrl = "https://abonelik.ornek.com";
        await settings.SaveGeneralAsync(general);
        await host.Secrets.SetAsync(UnsubscribeSecretProvider.EndpointApiKey, "anahtar");
        var secret = await host.Get<UnsubscribeSecretProvider>().GetOrCreateAsync();
        var token = new UnsubscribeTokenService(secret).Create("ayse@ornek.com", 0);
        handler.Body = $"[{{\"id\":7,\"kind\":\"unsubscribe\",\"token\":\"{token}\",\"occurredUtc\":\"2026-09-01T10:00:00Z\"}}]";

        var applied = await host.Get<IUnsubscribeSync>().SyncAsync(CancellationToken.None);

        applied.Should().Be(1);
        handler.LastRequest!.Headers.GetValues("X-Api-Key").Single().Should().Be("anahtar");
        handler.LastRequest.RequestUri!.ToString().Should().Be("https://abonelik.ornek.com/api/events?after=0");
        await using var db = await host.DbAsync();
        (await db.Contacts.SingleAsync(c => c.EmailNormalized == "ayse@ornek.com")).Status.Should().Be(ContactStatus.Unsubscribed);
        (await db.Contacts.SingleAsync(c => c.EmailNormalized == "ali@ornek.com")).Status.Should().Be(ContactStatus.Active);

        handler.Body = "[]";
        await host.Get<IUnsubscribeSync>().SyncAsync(CancellationToken.None);
        handler.LastRequest!.RequestUri!.ToString().Should().EndWith("after=7");
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public string Body { get; set; } = "[]";
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Body, Encoding.UTF8, "application/json")
            });
        }
    }
}
