using System.Text.RegularExpressions;
using Alpixa.Core.Models;
using Alpixa.Core.Rules;
using Alpixa.Core.Security;
using MimeKit;

namespace Alpixa.Infrastructure.Bounces;

public sealed record ParsedFeedback(string Email, DeliveryEventKind Kind, string? Status, string? Diagnostic, string? OriginalMessageId, int? CampaignId);

public static partial class BounceMessageParser
{
    [GeneratedRegex(@"[A-Za-z0-9._%+\-']+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\b[45]\.\d{1,3}\.\d{1,3}\b")]
    private static partial Regex StatusRegex();

    [GeneratedRegex(@"unsubscribe-([A-Za-z0-9_\-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex UnsubscribeTokenRegex();

    public static IReadOnlyList<ParsedFeedback> Parse(MimeMessage message, UnsubscribeTokenService? tokens)
    {
        if (message.Body is MultipartReport report)
        {
            if (string.Equals(report.ReportType, "delivery-status", StringComparison.OrdinalIgnoreCase))
                return ParseDsn(report);
            if (string.Equals(report.ReportType, "feedback-report", StringComparison.OrdinalIgnoreCase))
                return ParseArf(report);
        }

        var unsubscribe = ParseUnsubscribe(message, tokens);
        if (unsubscribe is not null) return [unsubscribe];

        return ParseNonStandardBounce(message);
    }

    public static bool IsHardStatus(string? status)
    {
        if (string.IsNullOrEmpty(status) || !status.StartsWith("5.")) return false;
        return status.StartsWith("5.1.") || status is "5.2.1" or "5.0.0";
    }

    private static IReadOnlyList<ParsedFeedback> ParseDsn(MultipartReport report)
    {
        var results = new List<ParsedFeedback>();
        var status = report.OfType<MessageDeliveryStatus>().FirstOrDefault();
        var originalId = FindOriginalMessageId(report);
        if (status is null) return results;

        foreach (var group in status.StatusGroups.Skip(1))
        {
            var recipient = StripAddressType(group["Final-Recipient"] ?? group["Original-Recipient"]);
            if (string.IsNullOrEmpty(recipient)) continue;
            var action = group["Action"]?.Trim().ToLowerInvariant();
            if (action is "delivered" or "relayed" or "expanded") continue;

            var code = group["Status"]?.Trim();
            var diagnostic = group["Diagnostic-Code"]?.Trim();
            var hard = action == "failed" && IsHardStatus(code);
            results.Add(new ParsedFeedback(recipient.ToLowerInvariant(), hard ? DeliveryEventKind.HardBounce : DeliveryEventKind.SoftBounce,
                code, Truncate(diagnostic), originalId, null));
        }
        return results;
    }

    private static IReadOnlyList<ParsedFeedback> ParseArf(MultipartReport report)
    {
        var originalId = FindOriginalMessageId(report);
        string? recipient = null;

        foreach (var part in report.OfType<MimePart>())
        {
            if (!part.ContentType.IsMimeType("message", "feedback-report")) continue;
            using var stream = new MemoryStream();
            part.Content?.DecodeTo(stream);
            var text = System.Text.Encoding.UTF8.GetString(stream.ToArray());
            foreach (var line in text.Split('\n'))
            {
                if (line.StartsWith("Original-Rcpt-To:", StringComparison.OrdinalIgnoreCase))
                    recipient = StripAddressType(line[17..]);
            }
        }

        if (recipient is null)
        {
            var original = report.OfType<MessagePart>().FirstOrDefault()?.Message;
            recipient = original?.To.Mailboxes.FirstOrDefault()?.Address;
        }

        return recipient is null
            ? []
            : [new ParsedFeedback(recipient.ToLowerInvariant(), DeliveryEventKind.Complaint, null, "feedback-report", originalId, null)];
    }

    private static ParsedFeedback? ParseUnsubscribe(MimeMessage message, UnsubscribeTokenService? tokens)
    {
        var subject = message.Subject ?? "";
        var match = UnsubscribeTokenRegex().Match(subject);
        if (match.Success && tokens is not null && tokens.TryRead(match.Groups[1].Value, out var payload) && payload is not null)
            return new ParsedFeedback(payload.Email, DeliveryEventKind.Unsubscribe, null, "mailto", null, payload.CampaignId);

        var trimmed = subject.Trim();
        if (trimmed.Equals("unsubscribe", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("abonelikten cik", StringComparison.OrdinalIgnoreCase))
        {
            var from = message.From.Mailboxes.FirstOrDefault()?.Address;
            if (from is not null) return new ParsedFeedback(from.ToLowerInvariant(), DeliveryEventKind.Unsubscribe, null, "mailto", null, null);
        }
        return null;
    }

    private static IReadOnlyList<ParsedFeedback> ParseNonStandardBounce(MimeMessage message)
    {
        var from = message.From.Mailboxes.FirstOrDefault()?.Address?.ToLowerInvariant() ?? "";
        var subject = (message.Subject ?? "").ToLowerInvariant();
        var looksLikeBounce = from.StartsWith("mailer-daemon") || from.StartsWith("postmaster")
                              || subject.Contains("undeliver") || subject.Contains("delivery status") || subject.Contains("delivery failure")
                              || subject.Contains("returned mail") || subject.Contains("iletilemedi") || subject.Contains("teslim edilemedi");
        if (!looksLikeBounce) return [];

        var body = message.TextBody ?? message.HtmlBody ?? "";
        var status = StatusRegex().Match(body);
        var recipients = EmailRegex().Matches(body)
            .Select(m => m.Value.ToLowerInvariant())
            .Where(e => !e.StartsWith("mailer-daemon") && !e.StartsWith("postmaster") && e != from)
            .Distinct()
            .Take(1)
            .ToList();
        if (recipients.Count == 0) return [];

        var code = status.Success ? status.Value : null;
        var kind = IsHardStatus(code) ? DeliveryEventKind.HardBounce : DeliveryEventKind.SoftBounce;
        return [new ParsedFeedback(recipients[0], kind, code, "non-standard bounce", FindOriginalMessageId(message.Body), null)];
    }

    private static string? FindOriginalMessageId(MimeEntity? root)
    {
        if (root is null) return null;
        foreach (var entity in (root as Multipart)?.ToList() ?? [root])
        {
            if (entity is MessagePart mp && mp.Message?.MessageId is { } id) return id;
            if (entity is TextPart tp && tp.ContentType.IsMimeType("text", "rfc822-headers"))
            {
                var m = Regex.Match(tp.Text ?? "", @"^Message-ID:\s*<([^>]+)>", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                if (m.Success) return m.Groups[1].Value;
            }
            if (entity is MimePart part && part.ContentType.IsMimeType("text", "rfc822-headers"))
            {
                using var s = new MemoryStream();
                part.Content?.DecodeTo(s);
                var m = Regex.Match(System.Text.Encoding.UTF8.GetString(s.ToArray()), @"^Message-ID:\s*<([^>]+)>", RegexOptions.IgnoreCase | RegexOptions.Multiline);
                if (m.Success) return m.Groups[1].Value;
            }
        }
        return null;
    }

    private static string? StripAddressType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        var semi = v.IndexOf(';');
        if (semi >= 0) v = v[(semi + 1)..].Trim();
        v = v.Trim('<', '>', ' ');
        return EmailAddressRules.TryNormalize(v, out var n, out _) ? n : null;
    }

    private static string? Truncate(string? s) => s is null ? null : s.Length > 500 ? s[..500] : s;
}
