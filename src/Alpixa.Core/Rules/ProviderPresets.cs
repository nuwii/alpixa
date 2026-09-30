using Alpixa.Core.Localization;
using Alpixa.Core.Models;

namespace Alpixa.Core.Rules;

public sealed record ProviderPreset(
    TransportKind Kind,
    string DisplayName,
    string SmtpHost,
    int SmtpPort,
    ConnectionSecurity Security,
    AuthMethod DefaultAuth,
    string? ImapHost,
    int ImapPort,
    string[] SpfIncludes,
    string[] CommonDkimSelectors,
    int SuggestedDailyLimit,
    int SuggestedHourlyLimit,
    int SuggestedPerMinuteLimit);

public static class ProviderPresets
{
    public static readonly IReadOnlyList<ProviderPreset> All =
    [
        new(TransportKind.Gmail, "Gmail / Google Workspace", "smtp.gmail.com", 587, ConnectionSecurity.StartTls, AuthMethod.OAuthGoogle,
            "imap.gmail.com", 993, ["_spf.google.com"], ["google"], 500, 100, 10),
        new(TransportKind.Microsoft365, "Microsoft 365 / Outlook", "smtp.office365.com", 587, ConnectionSecurity.StartTls, AuthMethod.OAuthMicrosoft,
            "outlook.office365.com", 993, ["spf.protection.outlook.com"], ["selector1", "selector2"], 500, 100, 10),
        new(TransportKind.AmazonSes, "Amazon SES", "email-smtp.eu-central-1.amazonaws.com", 587, ConnectionSecurity.StartTls, AuthMethod.Password,
            null, 993, ["amazonses.com"], [], 10000, 2000, 60),
        new(TransportKind.Brevo, "Brevo", "smtp-relay.brevo.com", 587, ConnectionSecurity.StartTls, AuthMethod.Password,
            null, 993, ["spf.brevo.com", "spf.sendinblue.com"], ["brevo1", "brevo2", "mail"], 10000, 2000, 60),
        new(TransportKind.Mailgun, "Mailgun", "smtp.mailgun.org", 587, ConnectionSecurity.StartTls, AuthMethod.Password,
            null, 993, ["mailgun.org"], ["mx", "k1", "smtp", "pic"], 10000, 2000, 60),
        new(TransportKind.SendGrid, "SendGrid", "smtp.sendgrid.net", 587, ConnectionSecurity.StartTls, AuthMethod.Password,
            null, 993, ["sendgrid.net"], ["s1", "s2"], 10000, 2000, 60),
        new(TransportKind.CustomSmtp, Msg.T("Preset_01"), "", 587, ConnectionSecurity.StartTls, AuthMethod.Password,
            null, 993, [], ["default", "mail", "dkim", "mp1"], 500, 100, 10),
        new(TransportKind.LocalTest, Msg.T("Preset_LocalTest"), "localhost", 2525, ConnectionSecurity.None, AuthMethod.None,
            null, 993, [], [], 100000, 100000, 1000)
    ];

    public static ProviderPreset For(TransportKind kind) => All.First(p => p.Kind == kind);

    public static void Apply(ProviderPreset preset, SenderProfile profile)
    {
        profile.Kind = preset.Kind;
        profile.SmtpHost = preset.SmtpHost;
        profile.SmtpPort = preset.SmtpPort;
        profile.Security = preset.Security;
        profile.Auth = preset.DefaultAuth;
        profile.DailyLimit = preset.SuggestedDailyLimit;
        profile.HourlyLimit = preset.SuggestedHourlyLimit;
        profile.PerMinuteLimit = preset.SuggestedPerMinuteLimit;
        if (preset.ImapHost is not null)
        {
            profile.ImapHost = preset.ImapHost;
            profile.ImapPort = preset.ImapPort;
            profile.ImapSecurity = ConnectionSecurity.SslOnConnect;
        }
    }
}
