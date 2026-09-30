using System.Net.Sockets;
using MailKit;
using IMailTransport = Alpixa.Core.Abstractions.IMailTransport;
using MailKit.Net.Smtp;
using MailKit.Security;
using Alpixa.Core.Abstractions;
using Alpixa.Core.Models;
using Alpixa.Core.Rules;
using Microsoft.Extensions.Logging;
using MimeKit;
using MimeKit.Cryptography;

namespace Alpixa.Infrastructure.Mail;

public sealed class SmtpMailTransport(
    SenderProfile profile,
    ISecretStore secrets,
    IOAuthTokenProvider oauth,
    int reconnectAfterMessages,
    ILogger logger) : IMailTransport
{
    private static readonly string[] DkimHeaders =
        ["From", "To", "Subject", "Date", "Message-ID", "Reply-To", "List-Unsubscribe", "List-Unsubscribe-Post", "MIME-Version", "Content-Type"];

    private SmtpClient? _client;
    private DkimSigner? _dkim;

    public int MessagesOnConnection { get; private set; }

    public async Task ConnectAsync(CancellationToken ct)
    {
        await DisconnectAsync();
        var client = TlsPolicy.Apply(new SmtpClient { Timeout = 60_000 });
        await client.ConnectAsync(profile.SmtpHost, profile.SmtpPort, ToMailKit(profile.Security), ct);

        switch (profile.Auth)
        {
            case AuthMethod.Password:
                var password = await secrets.GetAsync(profile.SmtpSecretKey)
                               ?? throw new AuthenticationException("No password stored for profile.");
                await client.AuthenticateAsync(profile.Username, password, ct);
                break;
            case AuthMethod.OAuthGoogle:
            case AuthMethod.OAuthMicrosoft:
                var token = await oauth.GetAccessTokenAsync(profile, allowInteractive: false, ct);
                await client.AuthenticateAsync(new SaslMechanismOAuth2(profile.Username, token), ct);
                break;
        }

        _client = client;
        MessagesOnConnection = 0;
        logger.LogInformation("SMTP connected to {Host}:{Port} for profile {ProfileId}", profile.SmtpHost, profile.SmtpPort, profile.Id);
    }

    public async Task<SendResult> SendAsync(MimeMessage message, CancellationToken ct)
    {
        try
        {
            if (_client is null || !_client.IsConnected || MessagesOnConnection >= reconnectAfterMessages)
                await ConnectAsync(ct);

            SignIfConfigured(message);
            var response = await _client!.SendAsync(message, ct);
            MessagesOnConnection++;
            return new SendResult(SendOutcome.Sent, 250, null, response);
        }
        catch (SmtpCommandException ex)
        {
            var code = (int)ex.StatusCode;
            var enhanced = SmtpResponseClassifier.ExtractEnhancedStatus(ex.Message);
            logger.LogWarning("SMTP {Code} {Enhanced} for message {MessageId}", code, enhanced, message.MessageId);
            if (code == 421) await DisconnectAsync();
            return new SendResult(SmtpResponseClassifier.Classify(code, ex.Message), code, enhanced, ex.Message);
        }
        catch (AuthenticationException ex)
        {
            await DisconnectAsync();
            return new SendResult(SendOutcome.AuthenticationFailure, 535, null, ex.Message);
        }
        catch (Exception ex) when (ex is SmtpProtocolException or IOException or SocketException or ServiceNotConnectedException or SslHandshakeException)
        {
            logger.LogWarning(ex, "SMTP connection problem for profile {ProfileId}", profile.Id);
            await DisconnectAsync();
            return new SendResult(SendOutcome.ConnectionFailure, null, null, ex.Message);
        }
    }

    private void SignIfConfigured(MimeMessage message)
    {
        if (!profile.DkimEnabled || string.IsNullOrWhiteSpace(profile.DkimPrivateKeyPath)) return;
        _dkim ??= new DkimSigner(profile.DkimPrivateKeyPath, profile.DkimDomain ?? profile.FromDomain, profile.DkimSelector ?? "mp1")
        {
            HeaderCanonicalizationAlgorithm = DkimCanonicalizationAlgorithm.Relaxed,
            BodyCanonicalizationAlgorithm = DkimCanonicalizationAlgorithm.Relaxed,
            AgentOrUserIdentifier = "@" + (profile.DkimDomain ?? profile.FromDomain),
            QueryMethod = "dns/txt"
        };
        message.Prepare(EncodingConstraint.SevenBit);
        var present = DkimHeaders.Where(h => message.Headers.Contains(h)).ToList();
        _dkim.Sign(message, present);
    }

    public static SecureSocketOptions ToMailKit(ConnectionSecurity security) => security switch
    {
        ConnectionSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        ConnectionSecurity.None => SecureSocketOptions.None,
        _ => SecureSocketOptions.StartTls
    };

    private async Task DisconnectAsync()
    {
        if (_client is null) return;
        try
        {
            if (_client.IsConnected) await _client.DisconnectAsync(true);
        }
        catch (Exception ex) when (ex is IOException or SocketException or SmtpProtocolException or OperationCanceledException)
        {
        }
        _client.Dispose();
        _client = null;
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync();
}

public sealed class DryRunMailTransport : IMailTransport
{
    public int MessagesOnConnection { get; private set; }
    public List<MimeMessage> Sent { get; } = new();

    public Task ConnectAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task<SendResult> SendAsync(MimeMessage message, CancellationToken ct)
    {
        await Task.Delay(20, ct);
        MessagesOnConnection++;
        lock (Sent)
        {
            if (Sent.Count < 1000) Sent.Add(message);
        }
        return SendResult.Ok();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
