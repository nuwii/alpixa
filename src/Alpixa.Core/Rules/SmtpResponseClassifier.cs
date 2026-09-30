using System.Text.RegularExpressions;
using Alpixa.Core.Models;

namespace Alpixa.Core.Rules;

public static partial class SmtpResponseClassifier
{
    [GeneratedRegex(@"\b([245])\.(\d{1,3})\.(\d{1,3})\b")]
    private static partial Regex EnhancedCodeRegex();

    public static string? ExtractEnhancedStatus(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var m = EnhancedCodeRegex().Match(text);
        return m.Success ? m.Value : null;
    }

    public static SendOutcome Classify(int code, string? response)
    {
        if (code is >= 200 and < 300) return SendOutcome.Sent;
        if (code is 421 or 450 or 451 or 452)
        {
            return IsRateLimitText(response) || code is 421 or 450 ? SendOutcome.RateLimited : SendOutcome.TransientFailure;
        }
        if (code is >= 400 and < 500) return SendOutcome.TransientFailure;
        if (code is 535 or 534 or 530) return SendOutcome.AuthenticationFailure;
        if (code >= 500) return SendOutcome.PermanentFailure;
        return SendOutcome.TransientFailure;
    }

    public static bool IsRateLimitText(string? response)
    {
        if (string.IsNullOrEmpty(response)) return false;
        var r = response.ToLowerInvariant();
        return r.Contains("rate") || r.Contains("too many") || r.Contains("throttl") || r.Contains("try again later")
               || r.Contains("4.7.28") || r.Contains("4.7.0") || r.Contains("temporarily deferred");
    }

    public static bool IsRecipientProblem(int code, string? enhanced)
    {
        if (enhanced is not null) return enhanced.StartsWith("5.1.") || enhanced == "5.2.1";
        return code is 550 or 551 or 553;
    }
}
