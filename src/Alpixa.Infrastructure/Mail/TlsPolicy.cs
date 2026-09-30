using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using MailKit;

namespace Alpixa.Infrastructure.Mail;

/// <summary>
/// Certificate validation for SMTP/IMAP. The chain and host name are always checked; revocation is
/// "soft-fail": a revoked certificate is rejected, but an unknown revocation status is accepted.
/// Some CAs (for example Google Trust Services) publish revocation only via CRL, which .NET cannot
/// fetch on macOS, so a strict check would reject Gmail even though its certificate is valid.
/// </summary>
public static class TlsPolicy
{
    private const X509ChainStatusFlags Tolerated = X509ChainStatusFlags.RevocationStatusUnknown | X509ChainStatusFlags.OfflineRevocation;

    public static T Apply<T>(T client) where T : IMailService
    {
        client.CheckCertificateRevocation = true;
        client.ServerCertificateValidationCallback = Validate;
        return client;
    }

    public static bool Validate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        if (errors == SslPolicyErrors.None) return true;
        if (errors != SslPolicyErrors.RemoteCertificateChainErrors || chain is null) return false;
        return chain.ChainStatus.All(s => (s.Status & ~Tolerated) == X509ChainStatusFlags.NoError);
    }
}
