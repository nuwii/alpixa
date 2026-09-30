using System.Net;
using System.Security.Cryptography;
using Alpixa.Core.Dns;
using Alpixa.Core.Models;
using Alpixa.Core.Rules;

namespace Alpixa.Tests.Unit;

public class AddressAndDnsParsingTests
{
    [Theory]
    [InlineData("Ayse@Ornek.COM", "ayse@ornek.com")]
    [InlineData("  mehmet.kaya+bulten@firma.com.tr ", "mehmet.kaya+bulten@firma.com.tr")]
    [InlineData("<info@ornek.com>", "info@ornek.com")]
    [InlineData("user@bücher.de", "user@xn--bcher-kva.de")]
    [InlineData("kullanici@şirket.com.tr", "kullanici@xn--irket-idb.com.tr")]
    public void Normalizes_valid_addresses(string input, string expected)
    {
        EmailAddressRules.TryNormalize(input, out var normalized, out _).Should().BeTrue();
        normalized.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ayse")]
    [InlineData("ayse@")]
    [InlineData("@ornek.com")]
    [InlineData("ayse@ornek")]
    [InlineData("ay..se@ornek.com")]
    [InlineData(".ayse@ornek.com")]
    [InlineData("ayse @ornek.com")]
    [InlineData("ayse@-ornek.com")]
    [InlineData("ayse@ornek.123")]
    [InlineData("ayse@ornek..com")]
    public void Rejects_invalid_addresses(string input)
    {
        EmailAddressRules.IsValid(input).Should().BeFalse();
    }

    [Fact]
    public void Detects_role_and_disposable_addresses()
    {
        AddressClassifier.IsRoleAddress("info@ornek.com").Should().BeTrue();
        AddressClassifier.IsRoleAddress("noreply@ornek.com").Should().BeTrue();
        AddressClassifier.IsRoleAddress("ayse@ornek.com").Should().BeFalse();
        AddressClassifier.IsDisposableDomain("mailinator.com").Should().BeTrue();
        AddressClassifier.IsDisposableDomain("ornek.com").Should().BeFalse();
    }

    [Fact]
    public void Parses_spf_record()
    {
        var spf = SpfRecord.Parse("v=spf1 ip4:203.0.113.0/24 include:_spf.google.com include:amazonses.com a mx ~all")!;
        spf.Should().NotBeNull();
        spf.Includes.Should().BeEquivalentTo(["_spf.google.com", "amazonses.com"]);
        spf.All!.Qualifier.Should().Be(SpfQualifier.SoftFail);
        spf.DirectLookupCount.Should().Be(4);
    }

    [Fact]
    public void Spf_redirect_counts_as_lookup()
    {
        var spf = SpfRecord.Parse("v=spf1 redirect=_spf.ornek.com")!;
        spf.Redirect.Should().Be("_spf.ornek.com");
        spf.DirectLookupCount.Should().Be(1);
    }

    [Fact]
    public void Non_spf_txt_is_ignored()
    {
        SpfRecord.IsSpf("google-site-verification=abc").Should().BeFalse();
        SpfRecord.IsSpf("v=spf10 foo").Should().BeFalse();
        SpfRecord.Parse("v=DMARC1; p=none").Should().BeNull();
    }

    [Theory]
    [InlineData("203.0.113.0/24", "203.0.113.77", true)]
    [InlineData("203.0.113.0/24", "203.0.114.1", false)]
    [InlineData("198.51.100.10", "198.51.100.10", true)]
    [InlineData("2001:db8::/32", "2001:db8:1::5", true)]
    [InlineData("10.0.0.0/8", "11.0.0.1", false)]
    public void Spf_ip_ranges_match(string cidr, string ip, bool expected)
    {
        SpfRecord.IpMatches(cidr, IPAddress.Parse(ip)).Should().Be(expected);
    }

    [Fact]
    public void Parses_dmarc_record()
    {
        var dmarc = DmarcRecord.Parse("v=DMARC1; p=quarantine; rua=mailto:dmarc@ornek.com; pct=50; adkim=s")!;
        dmarc.Policy.Should().Be("quarantine");
        dmarc.AggregateReportUri.Should().Be("mailto:dmarc@ornek.com");
        dmarc.Percent.Should().Be(50);
        dmarc.DkimAlignment.Should().Be("s");
        DmarcRecord.Parse("v=spf1 ~all").Should().BeNull();
    }

    [Theory]
    [InlineData(1024)]
    [InlineData(2048)]
    public void Dkim_key_size_is_read_from_public_key(int bits)
    {
        using var rsa = RSA.Create(bits);
        var publicKey = Convert.ToBase64String(rsa.ExportSubjectPublicKeyInfo());
        var record = DkimRecord.Parse($"v=DKIM1; k=rsa; p={publicKey}")!;
        record.KeyBits().Should().Be(bits);
    }

    [Fact]
    public void Dkim_empty_key_is_revoked()
    {
        DkimRecord.Parse("v=DKIM1; p=")!.IsRevoked.Should().BeTrue();
    }

    [Theory]
    [InlineData("bulten.firma.com.tr", "firma.com.tr")]
    [InlineData("mail.ornek.com", "ornek.com")]
    [InlineData("ornek.co.uk", "ornek.co.uk")]
    public void Organizational_domain(string domain, string expected)
    {
        OrganizationalDomain.Of(domain).Should().Be(expected);
    }

    [Theory]
    [InlineData(250, null, SendOutcome.Sent)]
    [InlineData(421, "Too many connections", SendOutcome.RateLimited)]
    [InlineData(450, null, SendOutcome.RateLimited)]
    [InlineData(451, "4.3.0 local error", SendOutcome.TransientFailure)]
    [InlineData(451, "4.7.1 Try again later", SendOutcome.RateLimited)]
    [InlineData(535, null, SendOutcome.AuthenticationFailure)]
    [InlineData(550, "5.1.1 user unknown", SendOutcome.PermanentFailure)]
    public void Classifies_smtp_responses(int code, string? text, SendOutcome expected)
    {
        SmtpResponseClassifier.Classify(code, text).Should().Be(expected);
    }

    [Fact]
    public void Extracts_enhanced_status()
    {
        SmtpResponseClassifier.ExtractEnhancedStatus("550 5.1.1 <x@y.com>: User unknown").Should().Be("5.1.1");
        SmtpResponseClassifier.IsRecipientProblem(550, "5.1.1").Should().BeTrue();
        SmtpResponseClassifier.IsRecipientProblem(550, "5.7.1").Should().BeFalse();
    }
}
