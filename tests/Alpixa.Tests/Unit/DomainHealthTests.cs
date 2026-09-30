using System.Net;
using System.Security.Cryptography;
using Alpixa.Core.Models;
using Alpixa.Core.Options;
using Alpixa.Infrastructure.Dns;
using Alpixa.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace Alpixa.Tests.Unit;

public class DomainHealthTests
{
    private static string DkimTxt(int bits)
    {
        using var rsa = RSA.Create(bits);
        return $"v=DKIM1; k=rsa; p={Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo())}";
    }

    private static (DomainHealthService Service, FakeDnsResolver Dns) Create()
    {
        var dns = new FakeDnsResolver();
        return (new DomainHealthService(dns, new AlpixaOptions(), NullLogger<DomainHealthService>.Instance), dns);
    }

    private static SenderProfile SesProfile() => new()
    {
        Kind = TransportKind.AmazonSes,
        SmtpHost = "email-smtp.eu-central-1.amazonaws.com",
        FromAddress = "bulten@ornek.com",
        DkimSelector = "mp1"
    };

    [Fact]
    public async Task Healthy_domain_passes()
    {
        var (service, dns) = Create();
        dns.Txt["ornek.com"] = ["v=spf1 include:amazonses.com ~all"];
        dns.Txt["amazonses.com"] = ["v=spf1 ip4:199.255.192.0/22 -all"];
        dns.Txt["mp1._domainkey.ornek.com"] = [DkimTxt(2048)];
        dns.Txt["_dmarc.ornek.com"] = ["v=DMARC1; p=quarantine; rua=mailto:d@ornek.com"];

        var report = await service.CheckAsync(SesProfile(), CancellationToken.None);

        report.HasBlockingIssues.Should().BeFalse();
        report.Checks.Single(c => c.Code == "spf").Status.Should().Be(CheckStatus.Pass);
        report.Checks.Single(c => c.Code == "dkim").Status.Should().Be(CheckStatus.Pass);
        report.Checks.Single(c => c.Code == "dmarc").Status.Should().Be(CheckStatus.Pass);
        report.Checks.Single(c => c.Code == "alignment").Status.Should().Be(CheckStatus.Pass);
    }

    [Fact]
    public async Task Missing_records_fail_with_suggested_values()
    {
        var (service, _) = Create();
        var report = await service.CheckAsync(SesProfile(), CancellationToken.None);

        report.HasBlockingIssues.Should().BeTrue();
        var dkim = report.Checks.Single(c => c.Code == "dkim");
        dkim.Status.Should().Be(CheckStatus.Fail);
        dkim.SuggestedRecord.Should().Contain("mp1._domainkey.ornek.com");
        var dmarc = report.Checks.Single(c => c.Code == "dmarc");
        dmarc.Status.Should().Be(CheckStatus.Fail);
        dmarc.SuggestedRecord.Should().Contain("_dmarc.ornek.com").And.Contain("p=none");
        report.Checks.Single(c => c.Code == "spf").SuggestedRecord.Should().Contain("include:amazonses.com");
    }

    [Fact]
    public async Task Detects_multiple_spf_records_and_plus_all()
    {
        var (service, dns) = Create();
        dns.Txt["ornek.com"] = ["v=spf1 include:amazonses.com ~all", "v=spf1 include:_spf.google.com ~all"];
        (await service.CheckAsync(SesProfile(), CancellationToken.None)).Checks.Single(c => c.Code == "spf").Status.Should().Be(CheckStatus.Fail);

        dns.Txt["ornek.com"] = ["v=spf1 include:amazonses.com +all"];
        (await service.CheckAsync(SesProfile(), CancellationToken.None)).Checks.Single(c => c.Code == "spf").Message.Should().Contain("+all");
    }

    [Fact]
    public async Task Detects_spf_lookup_limit()
    {
        var (service, dns) = Create();
        dns.Txt["ornek.com"] = ["v=spf1 include:amazonses.com " + string.Join(' ', Enumerable.Range(1, 10).Select(i => $"include:s{i}.example")) + " ~all"];
        var spf = (await service.CheckAsync(SesProfile(), CancellationToken.None)).Checks.Single(c => c.Code == "spf");
        spf.Status.Should().Be(CheckStatus.Fail);
        spf.Message.Should().Contain("11");
    }

    [Fact]
    public async Task Custom_smtp_checks_ip_coverage_ptr_and_blocklists()
    {
        var (service, dns) = Create();
        var ip = IPAddress.Parse("203.0.113.10");
        dns.A["mail.ornek.com"] = [ip];
        dns.Txt["ornek.com"] = ["v=spf1 ip4:203.0.113.0/24 -all"];
        dns.Txt["default._domainkey.ornek.com"] = [DkimTxt(1024)];
        dns.Txt["_dmarc.ornek.com"] = ["v=DMARC1; p=none"];
        dns.Ptr[ip.ToString()] = ["mail.ornek.com"];
        dns.A["10.113.0.203.zen.spamhaus.org"] = [IPAddress.Parse("127.0.0.2")];

        var profile = new SenderProfile { Kind = TransportKind.CustomSmtp, SmtpHost = "mail.ornek.com", FromAddress = "info@ornek.com" };
        var report = await service.CheckAsync(profile, CancellationToken.None);

        report.Checks.Single(c => c.Code == "spf").Status.Should().Be(CheckStatus.Pass);
        report.Checks.Single(c => c.Code == "dkim").Status.Should().Be(CheckStatus.Warning);
        report.Checks.Single(c => c.Code == "dmarc").Status.Should().Be(CheckStatus.Info);
        report.Checks.Single(c => c.Code == "ptr").Status.Should().Be(CheckStatus.Pass);
        var blocklist = report.Checks.Single(c => c.Code == "dnsbl-ip");
        blocklist.Status.Should().Be(CheckStatus.Fail);
        blocklist.Message.Should().Contain("zen.spamhaus.org");
    }

    [Fact]
    public async Task Personal_address_is_warned()
    {
        var (service, _) = Create();
        var report = await service.CheckAsync(new SenderProfile { Kind = TransportKind.Gmail, FromAddress = "ben@gmail.com", SmtpHost = "smtp.gmail.com" }, CancellationToken.None);
        report.Checks.Single().Code.Should().Be("personal");
        report.HasBlockingIssues.Should().BeFalse();
    }
}
