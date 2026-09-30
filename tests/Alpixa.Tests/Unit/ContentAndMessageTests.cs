using Alpixa.Core.Abstractions;
using Alpixa.Core.Content;
using Alpixa.Core.Models;
using Alpixa.Core.Security;
using Alpixa.Infrastructure.Mail;
using Alpixa.Infrastructure.Settings;
using Alpixa.Infrastructure.Templates;
using Alpixa.Tests.Support;
using MimeKit;

namespace Alpixa.Tests.Unit;

public class ContentAndMessageTests
{
    [Fact]
    public void Linter_flags_common_spam_signals()
    {
        var html = """
            <html><body>
            <img src="https://ornek.com/banner.jpg">
            <a href="https://bit.ly/abc">tikla</a>
            <a href="https://kotu-site.com/login">https://www.bankam.com</a>
            <script>alert(1)</script>
            <form><input name="x"></form>
            </body></html>
            """;
        var issues = ContentLinter.Lint("SON FIRSAT KACIRMAYIN!!!", html);
        var codes = issues.Select(i => i.Code).ToList();

        codes.Should().Contain(["subject-caps", "subject-exclamation", "image-heavy", "img-alt", "url-shortener", "link-mismatch", "javascript", "form", "unsubscribe-missing"]);
        issues.Where(i => i.Severity == LintSeverity.Error).Should().NotBeEmpty();
    }

    [Fact]
    public void Linter_accepts_clean_content()
    {
        var text = string.Join(" ", Enumerable.Repeat("Bu ay yeni urunlerimizi ve etkinliklerimizi sizinle paylasmak istiyoruz.", 6));
        var html = $"<html><body><p>{text}</p><p><a href=\"https://www.ornek.com/haber\">www.ornek.com</a></p><p><a href=\"{{{{ unsubscribe_url }}}}\">Abonelikten cik</a></p></body></html>";
        var issues = ContentLinter.Lint("Ekim bulteni", html);
        issues.Should().BeEmpty();
    }

    [Fact]
    public void Linter_warns_on_large_html()
    {
        var html = "<p>" + new string('a', 110 * 1024) + "</p>";
        ContentLinter.Lint("Konu", html).Should().Contain(i => i.Code == "html-size");
    }

    [Fact]
    public void HtmlToText_keeps_links_and_structure()
    {
        var text = HtmlToText.Convert("<h1>Baslik</h1><p>Merhaba&nbsp;Ayse,<br>nasilsin?</p><ul><li>Bir</li><li>Iki</li></ul><p><a href=\"https://ornek.com\">Siteye git</a></p><style>p{}</style>");
        text.Should().Contain("Baslik").And.Contain("Merhaba Ayse,\nnasilsin?").And.Contain("- Bir").And.Contain("Siteye git (https://ornek.com)");
        text.Should().NotContain("<").And.NotContain("p{}");
    }

    [Fact]
    public void HtmlToText_keeps_mailto_unsubscribe_links()
    {
        HtmlToText.Convert("<a href=\"mailto:a@b.com?subject=unsubscribe-x\">Abonelikten cik</a>")
            .Should().Contain("Abonelikten cik (mailto:a@b.com?subject=unsubscribe-x)");
        HtmlToText.Convert("<a href=\"mailto:a@b.com\">a@b.com</a>").Trim().Should().Be("a@b.com");
    }

    [Fact]
    public void Template_renderer_personalizes_with_defaults_and_escaping()
    {
        var renderer = new ScribanTemplateRenderer();
        var model = new Dictionary<string, object?> { ["ad"] = "Ayse <b>", ["firma"] = null, ["sehir"] = "Izmir" };

        renderer.Render("Merhaba {{ ad }}, {{ firma ?? \"degerli musterimiz\" }} - {{ sehir }}", model)
            .Should().Be("Merhaba Ayse <b>, degerli musterimiz - Izmir");
        renderer.Render("{{ ad }}", model, htmlEncodeValues: true).Should().Be("Ayse &lt;b&gt;");
        renderer.Render("{{ bilinmeyen ?? \"yok\" }}", model).Should().Be("yok");
        renderer.Validate("{{ if }}").Should().NotBeEmpty();
        renderer.Validate("Merhaba {{ ad }}").Should().BeEmpty();
    }

    [Fact]
    public void Unsubscribe_token_round_trips_and_rejects_tampering()
    {
        var service = new UnsubscribeTokenService(UnsubscribeTokenService.GenerateSecret());
        var token = service.Create("Ayse@Ornek.com", 42);

        token.Should().NotContain("ayse").And.NotContain("@");
        service.TryRead(token, out var payload).Should().BeTrue();
        payload!.Email.Should().Be("ayse@ornek.com");
        payload.CampaignId.Should().Be(42);

        var tampered = token[..^2] + (token[^2] == 'A' ? "B" : "A") + token[^1];
        service.TryRead(tampered, out _).Should().BeFalse();
        new UnsubscribeTokenService("baska-anahtar").TryRead(token, out _).Should().BeFalse();
    }

    private static async Task<(MessageComposer Composer, ComposeContext Context)> CreateComposerAsync(TestHost host, string? endpoint)
    {
        var settings = host.Get<SettingsStore>();
        var general = await settings.GetGeneralAsync();
        general.EndpointBaseUrl = endpoint;
        await settings.SaveGeneralAsync(general);
        var composer = (MessageComposer)host.Get<IMessageComposer>();
        return (composer, await composer.CreateContextAsync(CancellationToken.None));
    }

    private static (Campaign, SenderProfile, Contact) Sample() => (
        new Campaign { Id = 7, Name = "Ekim", Subject = "Merhaba {{ ad }}", HtmlBody = "<html><body><p>Merhaba {{ ad }} ({{ firma }})</p></body></html>" },
        new SenderProfile { Id = 3, FromName = "Ornek AS", FromAddress = "bulten@ornek.com", ReplyTo = "destek@ornek.com", PostalAddress = "Istanbul" },
        new Contact { Email = "Ayse@Alici.com", EmailNormalized = "ayse@alici.com", FirstName = "Ayse", Company = "ABC", CustomFieldsJson = "{\"sehir\":\"Izmir\"}" });

    [Fact]
    public async Task Composer_builds_compliant_message_with_one_click_unsubscribe()
    {
        await using var host = await TestHost.CreateAsync();
        var (composer, context) = await CreateComposerAsync(host, "https://abonelik.ornek.com/");
        var (campaign, profile, contact) = Sample();

        var message = composer.Compose(campaign, profile, contact, context);

        message.To.Mailboxes.Should().ContainSingle().Which.Address.Should().Be("Ayse@Alici.com");
        message.Cc.Count.Should().Be(0);
        message.Bcc.Count.Should().Be(0);
        message.From.Mailboxes.Single().Address.Should().Be("bulten@ornek.com");
        message.ReplyTo.Mailboxes.Single().Address.Should().Be("destek@ornek.com");
        message.Subject.Should().Be("Merhaba Ayse");
        message.MessageId.Should().EndWith("@ornek.com");
        message.Headers["List-Unsubscribe"].Should().StartWith("<https://abonelik.ornek.com/u/").And.Contain("<mailto:destek@ornek.com?subject=unsubscribe-");
        message.Headers["List-Unsubscribe-Post"].Should().Be("List-Unsubscribe=One-Click");
        message.Headers["Feedback-ID"].Should().Be("c7:p3:alpixa:ornek.com");
        message.Body.Should().BeOfType<MultipartAlternative>();
        message.HtmlBody.Should().Contain("Merhaba Ayse (ABC)").And.Contain("Abonelikten çık").And.Contain("Istanbul");
        message.TextBody.Should().Contain("Merhaba Ayse (ABC)").And.Contain("https://abonelik.ornek.com/u/");
    }

    [Fact]
    public async Task Composer_without_endpoint_uses_mailto_only()
    {
        await using var host = await TestHost.CreateAsync();
        var (composer, context) = await CreateComposerAsync(host, null);
        var (campaign, profile, contact) = Sample();

        var message = composer.Compose(campaign, profile, contact, context);

        message.Headers["List-Unsubscribe"].Should().StartWith("<mailto:");
        message.Headers.Contains("List-Unsubscribe-Post").Should().BeFalse();
    }

    [Fact]
    public async Task Composer_respects_template_unsubscribe_placeholder_and_tracking()
    {
        await using var host = await TestHost.CreateAsync();
        var (composer, context) = await CreateComposerAsync(host, "https://abonelik.ornek.com");
        var (campaign, profile, contact) = Sample();
        campaign.HtmlBody = "<html><body><p>{{ sehir }}</p><a href=\"{{ unsubscribe_url }}\">cik</a></body></html>";
        campaign.TrackOpens = true;

        var message = composer.Compose(campaign, profile, contact, context);

        message.HtmlBody.Should().Contain("Izmir").And.Contain("href=\"https://abonelik.ornek.com/u/").And.NotContain("Abonelikten çık");
        message.HtmlBody.Should().Contain("/o/").And.Contain(".gif");
    }
}
