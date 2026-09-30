using Alpixa.Core.Localization;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Security;
using Alpixa.Core.Abstractions;
using Alpixa.Core.Models;
using Alpixa.Infrastructure.Settings;
using Microsoft.Extensions.Logging;

namespace Alpixa.Infrastructure.Mail;

public sealed class MailTransportFactory(
    ISecretStore secrets,
    IOAuthTokenProvider oauth,
    SettingsStore settings,
    ILoggerFactory loggerFactory) : IMailTransportFactory
{
    public async Task<IMailTransport> CreateAsync(SenderProfile profile, bool dryRun, CancellationToken ct)
    {
        if (dryRun) return new DryRunMailTransport();
        var sending = await settings.GetSendingAsync(ct);
        return new SmtpMailTransport(profile, secrets, oauth, Math.Max(1, sending.ReconnectAfterMessages),
            loggerFactory.CreateLogger<SmtpMailTransport>());
    }
}

public sealed class ConnectionTester(ISecretStore secrets, IOAuthTokenProvider oauth, ILogger<ConnectionTester> logger) : IConnectionTester
{
    public async Task<(bool Ok, UserFacingError? Error)> TestSmtpAsync(SenderProfile profile, CancellationToken ct)
    {
        try
        {
            using var client = TlsPolicy.Apply(new SmtpClient { Timeout = 30_000 });
            await client.ConnectAsync(profile.SmtpHost, profile.SmtpPort, SmtpMailTransport.ToMailKit(profile.Security), ct);
            await AuthenticateAsync(client, profile, profile.Username, profile.SmtpSecretKey, ct);
            await client.DisconnectAsync(true, ct);
            return (true, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Connection test failed for {Host}", profile.SmtpHost);
            return (false, ErrorTranslator.Translate(ex, profile));
        }
    }

    public async Task<(bool Ok, UserFacingError? Error)> TestImapAsync(SenderProfile profile, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(profile.ImapHost))
            return (false, new UserFacingError(Msg.T("Transport_01"), Msg.T("Transport_02")));
        try
        {
            using var client = TlsPolicy.Apply(new ImapClient { Timeout = 30_000 });
            await client.ConnectAsync(profile.ImapHost, profile.ImapPort, SmtpMailTransport.ToMailKit(profile.ImapSecurity), ct);
            await AuthenticateAsync(client, profile, profile.ImapUsername ?? profile.Username, profile.ImapSecretKey, ct);
            await client.Inbox.OpenAsync(MailKit.FolderAccess.ReadOnly, ct);
            await client.DisconnectAsync(true, ct);
            return (true, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Connection test failed for {Host}", profile.SmtpHost);
            return (false, ErrorTranslator.Translate(ex, profile));
        }
    }

    private Task AuthenticateAsync(MailKit.IMailService client, SenderProfile profile, string username, string secretKey, CancellationToken ct)
        => new MailAuthenticator(secrets, oauth).AuthenticateAsync(client, profile, username, secretKey, allowInteractive: true, ct);
}
