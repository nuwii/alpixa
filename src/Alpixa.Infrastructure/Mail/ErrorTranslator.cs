using Alpixa.Core.Localization;
using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Alpixa.Core.Models;
using Alpixa.Infrastructure.Auth;

namespace Alpixa.Infrastructure.Mail;

public static class ErrorTranslator
{
    public static UserFacingError Translate(Exception ex, SenderProfile? profile = null)
    {
        var provider = profile?.Kind;
        switch (ex)
        {
            case OAuthNotConfiguredException:
                return new UserFacingError(
                    Msg.T("Err_18"),
                    Msg.T("Err_19"),
                    "https://support.google.com/accounts/answer/185833");
            case OAuthSignInRequiredException:
                return new UserFacingError(
                    Msg.T("Err_20"),
                    Msg.T("Err_01"));
            case AuthenticationException when provider == TransportKind.Gmail:
                return new UserFacingError(
                    Msg.T("Err_21"),
                    Msg.T("Err_22"),
                    "https://support.google.com/accounts/answer/185833");
            case AuthenticationException when provider == TransportKind.Microsoft365:
                return new UserFacingError(
                    Msg.T("Err_23"),
                    Msg.T("Err_24"),
                    "https://learn.microsoft.com/exchange/clients-and-mobile-in-exchange-online/authenticated-client-smtp-submission");
            case AuthenticationException:
                return new UserFacingError(
                    Msg.T("Err_25"),
                    Msg.T("Err_02"));
            case SslHandshakeException:
                return new UserFacingError(
                    Msg.T("Err_26"),
                    Msg.T("Err_03"));
            case SocketException se when se.SocketErrorCode == SocketError.HostNotFound:
                return new UserFacingError(
                    Msg.T("Err_27"),
                    Msg.T("Err_04"));
            case SocketException or TimeoutException or IOException:
                return new UserFacingError(
                    Msg.T("Err_28"),
                    Msg.T("Err_05"));
            case SmtpCommandException sce when (int)sce.StatusCode is 421 or 450 or 451:
                return new UserFacingError(
                    Msg.T("Err_29"),
                    Msg.T("Err_06"));
            case SmtpCommandException sce when (int)sce.StatusCode >= 500:
                return new UserFacingError(
                    Msg.T("Err_07", (int)sce.StatusCode),
                    Msg.T("Err_08"));
            case ServiceNotConnectedException or ProtocolException:
                return new UserFacingError(
                    Msg.T("Err_30"),
                    Msg.T("Err_09"));
            default:
                return new UserFacingError(
                    Msg.T("Err_31"),
                    Msg.T("Err_10"));
        }
    }

    public static string DescribeSendFailure(SendResult result) => result.Outcome switch
    {
        SendOutcome.RateLimited => Msg.T("Err_11"),
        SendOutcome.TransientFailure => Msg.T("Err_12"),
        SendOutcome.PermanentFailure when result.EnhancedStatus?.StartsWith("5.1.") == true => Msg.T("Err_13"),
        SendOutcome.PermanentFailure when result.EnhancedStatus?.StartsWith("5.7.") == true => Msg.T("Err_14"),
        SendOutcome.PermanentFailure => Msg.T("Err_15"),
        SendOutcome.AuthenticationFailure => Msg.T("Err_16"),
        SendOutcome.ConnectionFailure => Msg.T("Err_17"),
        _ => ""
    };
}
