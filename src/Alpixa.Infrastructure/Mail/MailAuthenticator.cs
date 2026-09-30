using MailKit.Security;
using Alpixa.Core.Abstractions;
using Alpixa.Core.Models;

namespace Alpixa.Infrastructure.Mail;

public sealed class MailAuthenticator(ISecretStore secrets, IOAuthTokenProvider oauth)
{
    public async Task AuthenticateAsync(MailKit.IMailService client, SenderProfile profile, string username, string secretKey,
        bool allowInteractive, CancellationToken ct)
    {
        switch (profile.Auth)
        {
            case AuthMethod.Password:
                var password = await secrets.GetAsync(secretKey) ?? await secrets.GetAsync(profile.SmtpSecretKey)
                               ?? throw new AuthenticationException("No password stored.");
                await client.AuthenticateAsync(username, password, ct);
                break;
            case AuthMethod.OAuthGoogle:
            case AuthMethod.OAuthMicrosoft:
                var token = await oauth.GetAccessTokenAsync(profile, allowInteractive, ct);
                await client.AuthenticateAsync(new SaslMechanismOAuth2(username, token), ct);
                break;
        }
    }
}
