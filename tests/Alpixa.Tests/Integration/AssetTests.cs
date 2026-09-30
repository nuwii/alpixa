using System.IO.Compression;
using Alpixa.Core.Abstractions;
using Alpixa.Core.Models;
using Alpixa.Infrastructure.Mail;
using Alpixa.Infrastructure.Maintenance;
using Alpixa.Infrastructure.Settings;
using Alpixa.Infrastructure.Templates;
using Alpixa.Sending.Engine;
using Alpixa.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;

namespace Alpixa.Tests.Integration;

public class AssetTests
{
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static string TempFile(TestHost host, string name, byte[] content)
    {
        var path = Path.Combine(host.Paths.DataDirectory, "src-" + name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static async Task<int> TemplateIdAsync(TestHost host)
    {
        await using var db = await host.DbAsync();
        return (await db.Templates.FirstAsync()).Id;
    }

    [Fact]
    public async Task Images_are_stored_and_previewed_as_data_uri()
    {
        await using var host = await TestHost.CreateAsync();
        var assets = host.Get<AssetStore>();

        var name = await assets.ImportImageAsync(TempFile(host, "logo.png", Png));

        assets.Exists(name).Should().BeTrue();
        var preview = assets.ToPreviewHtml($"<img src=\"asset:{name}\" alt=\"logo\">");
        preview.Should().Contain("src=\"data:image/png;base64,");
        AssetStore.ReferencedImages($"<img src='asset:{name}'>").Should().Equal(name);
    }

    [Fact]
    public async Task Image_tag_uses_real_width_and_is_inserted_at_cursor_or_after_heading()
    {
        await using var host = await TestHost.CreateAsync();
        var assets = host.Get<AssetStore>();
        var name = await assets.ImportImageAsync(TempFile(host, "logo.png", Png));

        AssetStore.ReadImageSize(Png).Should().Be((1, 1));
        var tag = assets.ImageTag(name, "kapak-resmi");
        tag.Should().Contain($"src=\"asset:{name}\"").And.Contain("width=\"1\"").And.Contain("alt=\"kapak resmi\"");

        var (html, cursor) = AssetStore.InsertAt("<h1>Merhaba</h1><p>Metin</p>", "<img>", 0);
        html.Should().Be("<h1>Merhaba</h1>\n<img>\n<p>Metin</p>");
        cursor.Should().Be("<h1>Merhaba</h1>\n<img>\n".Length);
        AssetStore.InsertAt("abc", "X", 1).Html.Should().Be("a\nX\nbc");
    }

    [Fact]
    public async Task Image_and_attachment_limits_are_enforced()
    {
        await using var host = await TestHost.CreateAsync();
        var settings = host.Get<SettingsStore>();
        var sending = await settings.GetSendingAsync();
        sending.MaxImageKb = 1;
        sending.MaxAttachmentMb = 1;
        await settings.SaveSendingAsync(sending);
        var assets = host.Get<AssetStore>();
        var templateId = await TemplateIdAsync(host);

        var bigImage = TempFile(host, "big.png", new byte[5 * 1024]);
        await assets.Invoking(a => a.ImportImageAsync(bigImage)).Should().ThrowAsync<AssetTooLargeException>().WithMessage("*çok büyük*");
        await assets.Invoking(a => a.ImportImageAsync(TempFile(host, "notes.txt", [1, 2, 3]))).Should().ThrowAsync<InvalidOperationException>();

        await assets.AddAttachmentAsync(templateId, TempFile(host, "a.pdf", new byte[700 * 1024]));
        await assets.Invoking(a => a.AddAttachmentAsync(templateId, TempFile(host, "b.pdf", new byte[400 * 1024])))
            .Should().ThrowAsync<AssetTooLargeException>().WithMessage("*1 MB*");
        (await assets.ListAsync(templateId)).Should().ContainSingle().Which.FileName.Should().Be("src-a.pdf");
    }

    [Fact]
    public async Task Composer_embeds_images_inline_and_adds_attachments()
    {
        await using var host = await TestHost.CreateAsync();
        var assets = host.Get<AssetStore>();
        var templateId = await TemplateIdAsync(host);
        var image = await assets.ImportImageAsync(TempFile(host, "logo.png", Png));
        await assets.AddAttachmentAsync(templateId, TempFile(host, "brosur.pdf", [37, 80, 68, 70]));

        var composer = host.Get<IMessageComposer>();
        var message = composer.Compose(
            new Campaign { Id = 1, Subject = "Merhaba", HtmlBody = $"<html><body><img src=\"asset:{image}\" alt=\"logo\"></body></html>", AttachmentsJson = await assets.SnapshotAsync(templateId) },
            new SenderProfile { FromName = "Örnek", FromAddress = "bulten@ornek.com" },
            new Contact { Email = "a@b.com", EmailNormalized = "a@b.com" },
            await composer.CreateContextAsync(CancellationToken.None));

        message.HtmlBody.Should().Contain("src=\"cid:").And.NotContain("asset:");
        message.BodyParts.OfType<MimePart>().Should().Contain(p => p.ContentType.MimeType == "image/png" && p.ContentId != null);
        message.Attachments.OfType<MimePart>().Should().ContainSingle(p => p.FileName == "src-brosur.pdf");
    }

    [Fact]
    public async Task Campaign_with_attachment_is_delivered_over_smtp()
    {
        await using var smtp = new FakeSmtpServer();
        await using var host = await TestHost.CreateAsync(s => s.AddSingleton<IMailTransportFactory, MailTransportFactory>());
        var assets = host.Get<AssetStore>();
        var profile = await host.AddProfileAsync(p => p.SmtpPort = smtp.Port);
        var listId = await host.AddListAsync(["a@alici.com", "b@alici.com"]);
        var templateId = await TemplateIdAsync(host);
        await assets.AddAttachmentAsync(templateId, TempFile(host, "fiyat.pdf", new byte[2048]));
        var image = await assets.ImportImageAsync(TempFile(host, "logo.png", Png));
        var campaign = await host.AddCampaignAsync(profile.Id, listId, c =>
        {
            c.TemplateId = templateId;
            c.HtmlBody = $"<html><body><p>Merhaba</p><img src=\"asset:{image}\" alt=\"logo\"></body></html>";
            c.AttachmentsJson = assets.SnapshotAsync(templateId).GetAwaiter().GetResult();
        });

        (await host.Get<SendEngine>().RunAsync(campaign.Id, null, CancellationToken.None)).Status.Should().Be(CampaignStatus.Completed);

        smtp.Messages.Should().HaveCount(2).And.OnlyContain(m =>
            m.Attachments.OfType<MimePart>().Any(p => p.FileName == "src-fiyat.pdf") &&
            m.HtmlBody!.Contains("cid:"));
    }

    [Fact]
    public async Task Backup_includes_template_files()
    {
        await using var host = await TestHost.CreateAsync();
        var name = await host.Get<AssetStore>().ImportImageAsync(TempFile(host, "logo.png", Png));
        var backup = Path.Combine(host.Paths.DataDirectory, "yedek.zip");

        await host.Get<MaintenanceService>().CreateBackupAsync(backup);

        using var zip = ZipFile.OpenRead(backup);
        zip.Entries.Select(e => e.FullName).Should().Contain("files/" + name);
    }
}
