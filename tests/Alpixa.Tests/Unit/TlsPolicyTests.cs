using System.Net.Security;
using Alpixa.Infrastructure.Mail;

namespace Alpixa.Tests.Unit;

public class TlsPolicyTests
{
    [Fact]
    public void Valid_certificate_is_accepted_and_name_mismatch_is_rejected()
    {
        TlsPolicy.Validate(this, null, null, SslPolicyErrors.None).Should().BeTrue();
        TlsPolicy.Validate(this, null, null, SslPolicyErrors.RemoteCertificateNameMismatch).Should().BeFalse();
        TlsPolicy.Validate(this, null, null, SslPolicyErrors.RemoteCertificateNotAvailable).Should().BeFalse();
        TlsPolicy.Validate(this, null, null, SslPolicyErrors.RemoteCertificateChainErrors).Should().BeFalse();
    }
}
