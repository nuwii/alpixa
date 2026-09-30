using Alpixa.Core.Localization;
using System.Text;
using System.Text.RegularExpressions;
using Alpixa.Core.Models;
using Alpixa.Core.Rules;

namespace Alpixa.Core.Content;

public static partial class ContentLinter
{
    private static readonly string[] UrlShorteners =
    [
        "bit.ly", "tinyurl.com", "goo.gl", "t.co", "ow.ly", "is.gd", "buff.ly", "rebrand.ly", "cutt.ly", "shorturl.at",
        "tiny.cc", "rb.gy", "t.ly", "bl.ink", "s.id", "v.gd", "lnkd.in", "shorte.st", "adf.ly", "bc.vc"
    ];

    private static readonly string[] SpamPhrases =
    [
        "100% free", "100% bedava", "act now", "hemen tikla", "hemen tıkla", "buy now", "click here", "buraya tikla",
        "buraya tıkla", "free money", "bedava para", "winner", "kazandiniz", "kazandınız", "cash bonus", "no cost",
        "risk free", "risksiz", "guaranteed", "garantili kazanc", "garantili kazanç", "limited time", "son firsat",
        "son fırsat", "earn money", "para kazan", "double your", "viagra", "casino", "kumar", "bahis", "lottery",
        "piyango", "credit card", "kredi karti bilgi", "urgent", "acil cevap", "congratulations", "tebrikler kazandiniz",
        "million dollars", "milyon dolar", "no credit check", "not spam", "spam degil", "this is not spam"
    ];

    [GeneratedRegex(@"<a\s[^>]*href\s*=\s*[""']([^""']*)[""'][^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AnchorRegex();

    [GeneratedRegex(@"<img\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ImgRegex();

    [GeneratedRegex(@"\balt\s*=\s*[""'][^""']+[""']", RegexOptions.IgnoreCase)]
    private static partial Regex AltRegex();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"<script\b|\bon(click|load|error|mouseover)\s*=|javascript:", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptRegex();

    [GeneratedRegex(@"<(form|input|button|select|textarea)\b", RegexOptions.IgnoreCase)]
    private static partial Regex FormRegex();

    [GeneratedRegex(@"<(video|audio|iframe|object|embed)\b", RegexOptions.IgnoreCase)]
    private static partial Regex EmbedRegex();

    [GeneratedRegex(@"https?://([^/\s""'<>]+)", RegexOptions.IgnoreCase)]
    private static partial Regex UrlHostRegex();

    [GeneratedRegex(@"\{\{\s*(unsubscribe_url|abonelik_iptal_url)\s*\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex UnsubscribePlaceholderRegex();

    [GeneratedRegex(@"(unsubscribe|abonelik|aboneligi|aboneliği|listeden cik|listeden çık)", RegexOptions.IgnoreCase)]
    private static partial Regex UnsubscribeWordRegex();

    public static IReadOnlyList<LintIssue> Lint(string subject, string html, bool hasAttachments = false)
    {
        var issues = new List<LintIssue>();
        subject ??= "";
        html ??= "";

        LintSubject(subject, issues);

        var size = Encoding.UTF8.GetByteCount(html);
        if (size > DeliverabilityThresholds.HtmlSizeWarningBytes)
            issues.Add(new("html-size", LintSeverity.Warning,
                Msg.T("Lint_01", size / 1024)));

        var text = TagRegex().Replace(html, " ");
        var visibleTextLength = Regex.Replace(text, @"\s+", " ").Trim().Length;
        var images = ImgRegex().Matches(html);

        if (images.Count > 0 && visibleTextLength < 200)
            issues.Add(new("image-heavy", LintSeverity.Warning,
                Msg.T("Lint_02")));

        var missingAlt = images.Count(m => !AltRegex().IsMatch(m.Value));
        if (missingAlt > 0)
            issues.Add(new("img-alt", LintSeverity.Info,
                Msg.T("Lint_03", missingAlt)));

        foreach (Match m in UrlHostRegex().Matches(html))
        {
            var host = m.Groups[1].Value.ToLowerInvariant();
            if (UrlShorteners.Any(s => host == s || host.EndsWith("." + s)))
            {
                issues.Add(new("url-shortener", LintSeverity.Error,
                    Msg.T("Lint_04", host)));
                break;
            }
        }

        foreach (Match m in AnchorRegex().Matches(html))
        {
            var href = m.Groups[1].Value.Trim();
            var label = TagRegex().Replace(m.Groups[2].Value, "").Trim();
            var labelHost = UrlHostRegex().Match(label);
            var hrefHost = UrlHostRegex().Match(href);
            if (!labelHost.Success && label.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                labelHost = UrlHostRegex().Match("http://" + label);
            if (labelHost.Success && hrefHost.Success &&
                !OrganizationalDomainEquals(labelHost.Groups[1].Value, hrefHost.Groups[1].Value))
            {
                issues.Add(new("link-mismatch", LintSeverity.Error,
                    Msg.T("Lint_05", labelHost.Groups[1].Value, hrefHost.Groups[1].Value)));
                break;
            }
        }

        if (hasAttachments)
            issues.Add(new("attachment", LintSeverity.Warning,
                Msg.T("Lint_06")));

        if (ScriptRegex().IsMatch(html))
            issues.Add(new("javascript", LintSeverity.Error,
                Msg.T("Lint_07")));

        if (FormRegex().IsMatch(html))
            issues.Add(new("form", LintSeverity.Warning,
                Msg.T("Lint_08")));

        if (EmbedRegex().IsMatch(html))
            issues.Add(new("embed", LintSeverity.Warning,
                Msg.T("Lint_09")));

        if (!UnsubscribePlaceholderRegex().IsMatch(html) && !UnsubscribeWordRegex().IsMatch(text))
            issues.Add(new("unsubscribe-missing", LintSeverity.Info,
                Msg.T("Lint_10")));

        var lowered = (subject + " " + text).ToLowerInvariant();
        var hits = SpamPhrases.Where(p => lowered.Contains(p)).Distinct().Take(5).ToList();
        if (hits.Count > 0)
            issues.Add(new("spam-phrases", LintSeverity.Warning,
                Msg.T("Lint_11", string.Join(", ", hits.Select(h => $"\"{h}\"")))));

        return issues;
    }

    private static void LintSubject(string subject, List<LintIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            issues.Add(new("subject-empty", LintSeverity.Error, Msg.T("Lint_12")));
            return;
        }

        var letters = subject.Where(char.IsLetter).ToList();
        if (letters.Count >= 6 && letters.All(char.IsUpper))
            issues.Add(new("subject-caps", LintSeverity.Warning,
                Msg.T("Lint_13")));

        var exclamations = subject.Count(c => c == '!');
        if (exclamations >= 2)
            issues.Add(new("subject-exclamation", LintSeverity.Warning,
                Msg.T("Lint_14", exclamations)));

        if (subject.Length > 120)
            issues.Add(new("subject-long", LintSeverity.Info,
                Msg.T("Lint_15")));

        if (subject.Contains('$') || subject.Contains("₺₺") || subject.Contains("%100"))
            issues.Add(new("subject-money", LintSeverity.Info,
                Msg.T("Lint_16")));
    }

    private static bool OrganizationalDomainEquals(string a, string b)
        => Dns.OrganizationalDomain.Of(a.Split(':')[0]) == Dns.OrganizationalDomain.Of(b.Split(':')[0]);
}
