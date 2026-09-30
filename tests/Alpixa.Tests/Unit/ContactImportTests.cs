using System.Diagnostics;
using System.Text;
using Alpixa.Core.Abstractions;
using Alpixa.Core.Models;
using Alpixa.Infrastructure.Contacts;
using Alpixa.Sending.Queue;
using Alpixa.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Alpixa.Tests.Unit;

public class ContactImportTests
{
    [Fact]
    public void Guesses_turkish_and_english_columns()
    {
        var headers = new[] { "E-posta", "Adı", "Soyadı", "Şirket", "İzin Kaynağı", "izin_tarihi", "Şehir" };
        var mapping = ColumnGuesser.Guess(headers, []);
        mapping.EmailColumn.Should().Be(0);
        mapping.FirstNameColumn.Should().Be(1);
        mapping.LastNameColumn.Should().Be(2);
        mapping.CompanyColumn.Should().Be(3);
        mapping.ConsentSourceColumn.Should().Be(4);
        mapping.ConsentDateColumn.Should().Be(5);
        mapping.CustomColumns.Should().ContainKey(6).WhoseValue.Should().Be("sehir");
    }

    [Fact]
    public void Detects_email_column_without_header()
    {
        var rows = new[] { new[] { "Ayse", "ayse@ornek.com" }, new[] { "Ali", "ali@ornek.com" } };
        var service = new ContactImportService(null!, null!, null!);
        var preview = service.Preview(rows);
        preview.Mapping.EmailColumn.Should().Be(1);
        preview.Headers.Should().Equal("kolon1", "kolon2");
    }

    [Fact]
    public void Reads_semicolon_csv_with_quotes()
    {
        var csv = "email;ad;not\n\"ayse@ornek.com\";Ayse;\"a;b\"\n\nali@ornek.com;Ali;x\n";
        var rows = TabularReader.ReadCsv(new MemoryStream(Encoding.UTF8.GetBytes(csv))).ToList();
        rows.Should().HaveCount(3);
        rows[1].Should().Equal("ayse@ornek.com", "Ayse", "a;b");
    }

    [Fact]
    public async Task Import_cleans_list_and_reports_reasons()
    {
        await using var host = await TestHost.CreateAsync();
        await host.Get<ISuppressionService>().SuppressAsync("cikmis@ornek.com", SuppressionReason.Unsubscribed, "test", null, CancellationToken.None);
        host.Dns.Mx["mx-yok.com"] = [];
        var list = await host.Get<ContactService>().CreateListAsync("Test", true);

        var csv = """
            email,ad,soyad,firma,izin_kaynagi,izin_tarihi,sehir
            ayse@ornek.com,Ayse,Yilmaz,ABC,web formu,2026-03-12,Izmir
            AYSE@ornek.com,Ayse,Tekrar,,,,
            hatali-adres,X,,,,,
            biri@mailinator.com,Y,,,,,
            info@ornek.com,Z,,,,,
            cikmis@ornek.com,Q,,,,,
            kisi@mx-yok.com,W,,,,,
            ,bos,,,,,
            """;
        var rows = TabularReader.ReadText(csv).ToList();
        var import = host.Get<ContactImportService>();
        var preview = import.Preview(rows);
        var summary = await import.ImportAsync(list.Id, rows, preview.Mapping, checkMx: true, null, CancellationToken.None);

        summary.TotalRows.Should().Be(8);
        summary.Duplicates.Should().Be(1);
        summary.InvalidSyntax.Should().Be(1);
        summary.Disposable.Should().Be(1);
        summary.RoleAddresses.Should().Be(1);
        summary.Suppressed.Should().Be(1);
        summary.NoMx.Should().Be(1);
        summary.MissingEmail.Should().Be(1);

        await using var db = await host.DbAsync();
        var ayse = await db.Contacts.SingleAsync(c => c.EmailNormalized == "ayse@ornek.com");
        ayse.ConsentSource.Should().Be("web formu");
        ayse.ConsentDate.Should().Be(new DateTime(2026, 3, 12));
        ayse.CustomFieldsJson.Should().Contain("\"sehir\":\"Izmir\"");
        (await db.Contacts.CountAsync(c => c.Status == ContactStatus.Active)).Should().Be(2);
    }

    [Fact]
    public async Task Gdpr_forget_and_export()
    {
        await using var host = await TestHost.CreateAsync();
        var listId = await host.AddListAsync(["ayse@ornek.com", "ali@ornek.com"]);
        var contacts = host.Get<ContactService>();

        var export = await contacts.ExportPersonalDataAsync("Ayse@ornek.com");
        export.Should().Contain("ayse@ornek.com").And.Contain("Kisi0");

        (await contacts.ForgetAsync("ayse@ornek.com")).Should().Be(1);
        (await contacts.GetContactsAsync(listId, null, null, 0, 10)).Select(c => c.EmailNormalized).Should().Equal("ali@ornek.com");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task Imports_and_queues_100k_contacts()
    {
        await using var host = await TestHost.CreateAsync();
        var list = await host.Get<ContactService>().CreateListAsync("Buyuk", true);
        var domains = new[] { "gmail.com", "outlook.com", "yahoo.com", "firma.com.tr", "ornek.com" };

        IEnumerable<string[]> Rows()
        {
            yield return ["email", "ad", "firma"];
            for (var i = 0; i < 100_000; i++)
                yield return [$"kisi{i}@{(i % 7 == 0 ? $"d{i % 5000}.com" : domains[i % domains.Length])}", $"Ad{i}", $"Firma{i % 100}"];
        }

        var import = host.Get<ContactImportService>();
        var mapping = import.Preview(Rows().Take(21)).Mapping;
        var memoryBefore = GC.GetTotalMemory(true);
        var sw = Stopwatch.StartNew();
        var summary = await import.ImportAsync(list.Id, Rows(), mapping, checkMx: false, null, CancellationToken.None);
        var importTime = sw.Elapsed;

        summary.Imported.Should().Be(100_000);
        importTime.Should().BeLessThan(TimeSpan.FromSeconds(60));
        (GC.GetTotalMemory(false) - memoryBefore).Should().BeLessThan(300L * 1024 * 1024);

        var profile = await host.AddProfileAsync();
        var campaign = await host.AddCampaignAsync(profile.Id, list.Id);
        sw.Restart();
        var queued = await host.Get<CampaignQueueBuilder>().BuildAsync(campaign.Id, CancellationToken.None);
        queued.Should().Be(100_000);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(60));
        (await host.Get<CampaignQueueBuilder>().BuildAsync(campaign.Id, CancellationToken.None)).Should().Be(100_000);
    }
}
