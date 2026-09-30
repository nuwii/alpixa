using Alpixa.Core.Models;
using Alpixa.Core.Security;
using Alpixa.Infrastructure.Bounces;
using MimeKit;

namespace Alpixa.Tests.Unit;

public class BounceParsingTests
{
    private static MimeMessage Load(string raw) => MimeMessage.Load(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(raw.Replace("\r\n", "\n").Replace("\n", "\r\n"))));

    private const string HardDsn = """
        From: Mail Delivery System <MAILER-DAEMON@mx.ornek.com>
        To: bulten@ornek.com
        Subject: Undelivered Mail Returned to Sender
        MIME-Version: 1.0
        Content-Type: multipart/report; report-type=delivery-status; boundary="BOUND"

        --BOUND
        Content-Type: text/plain

        This is the mail system. Your message could not be delivered.

        --BOUND
        Content-Type: message/delivery-status

        Reporting-MTA: dns; mx.ornek.com

        Final-Recipient: rfc822; yok@alici.com
        Original-Recipient: rfc822;yok@alici.com
        Action: failed
        Status: 5.1.1
        Diagnostic-Code: smtp; 550 5.1.1 User unknown

        --BOUND
        Content-Type: text/rfc822-headers

        From: bulten@ornek.com
        To: yok@alici.com
        Message-ID: <abc123@ornek.com>
        Subject: Merhaba

        --BOUND--
        """;

    [Fact]
    public void Parses_hard_bounce_dsn()
    {
        var result = BounceMessageParser.Parse(Load(HardDsn), null);
        result.Should().ContainSingle();
        var bounce = result[0];
        bounce.Email.Should().Be("yok@alici.com");
        bounce.Kind.Should().Be(DeliveryEventKind.HardBounce);
        bounce.Status.Should().Be("5.1.1");
        bounce.OriginalMessageId.Should().Be("abc123@ornek.com");
    }

    [Fact]
    public void Parses_soft_bounce_dsn()
    {
        var raw = HardDsn.Replace("Action: failed", "Action: delayed").Replace("Status: 5.1.1", "Status: 4.2.2");
        var result = BounceMessageParser.Parse(Load(raw), null);
        result.Single().Kind.Should().Be(DeliveryEventKind.SoftBounce);
    }

    [Fact]
    public void Mailbox_full_is_soft()
    {
        var raw = HardDsn.Replace("Status: 5.1.1", "Status: 5.2.2");
        BounceMessageParser.Parse(Load(raw), null).Single().Kind.Should().Be(DeliveryEventKind.SoftBounce);
    }

    [Fact]
    public void Parses_arf_complaint()
    {
        var raw = """
            From: feedback@isp.example
            To: fbl@ornek.com
            Subject: Abuse report
            MIME-Version: 1.0
            Content-Type: multipart/report; report-type=feedback-report; boundary="FB"

            --FB
            Content-Type: text/plain

            This is an abuse report.

            --FB
            Content-Type: message/feedback-report

            Feedback-Type: abuse
            User-Agent: SomeISP/1.0
            Version: 1
            Original-Rcpt-To: <sikayetci@alici.com>

            --FB
            Content-Type: message/rfc822

            From: bulten@ornek.com
            To: sikayetci@alici.com
            Message-ID: <m1@ornek.com>
            Subject: Kampanya

            Govde

            --FB--
            """;
        var result = BounceMessageParser.Parse(Load(raw), null);
        result.Should().ContainSingle();
        result[0].Kind.Should().Be(DeliveryEventKind.Complaint);
        result[0].Email.Should().Be("sikayetci@alici.com");
        result[0].OriginalMessageId.Should().Be("m1@ornek.com");
    }

    [Fact]
    public void Parses_mailto_unsubscribe_with_token()
    {
        var tokens = new UnsubscribeTokenService("gizli");
        var token = tokens.Create("ayse@alici.com", 5);
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse("baska@adres.com"));
        message.To.Add(MailboxAddress.Parse("bulten@ornek.com"));
        message.Subject = "unsubscribe-" + token;
        message.Body = new TextPart("plain") { Text = "" };

        var result = BounceMessageParser.Parse(message, tokens);
        result.Single().Should().Be(new ParsedFeedback("ayse@alici.com", DeliveryEventKind.Unsubscribe, null, "mailto", null, 5));
    }

    [Fact]
    public void Parses_plain_unsubscribe_subject_from_sender()
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse("Mehmet@Alici.com"));
        message.Subject = "Unsubscribe";
        message.Body = new TextPart("plain") { Text = "" };

        BounceMessageParser.Parse(message, null).Single().Email.Should().Be("mehmet@alici.com");
    }

    [Fact]
    public void Parses_non_standard_bounce()
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse("postmaster@mx.alici.com"));
        message.Subject = "Delivery Status Notification (Failure)";
        message.Body = new TextPart("plain") { Text = "Delivery to kayip@alici.com failed permanently: 550 5.1.1 The email account does not exist." };

        var result = BounceMessageParser.Parse(message, null).Single();
        result.Email.Should().Be("kayip@alici.com");
        result.Kind.Should().Be(DeliveryEventKind.HardBounce);
    }

    [Fact]
    public void Ignores_regular_mail()
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse("musteri@alici.com"));
        message.Subject = "Tesekkurler";
        message.Body = new TextPart("plain") { Text = "Bulteniniz cok guzel." };
        BounceMessageParser.Parse(message, null).Should().BeEmpty();
    }
}
