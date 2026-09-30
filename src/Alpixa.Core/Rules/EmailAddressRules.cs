using System.Globalization;

namespace Alpixa.Core.Rules;

public static class EmailAddressRules
{
    private static readonly IdnMapping Idn = new();
    private const string AtextSpecials = "!#$%&'*+-/=?^_`{|}~";

    public static bool TryNormalize(string? input, out string normalized, out string domain)
    {
        normalized = "";
        domain = "";
        if (string.IsNullOrWhiteSpace(input)) return false;

        var value = input.Trim().Trim('<', '>', '"', '\'').Trim();
        if (value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)) value = value[7..];

        var at = value.LastIndexOf('@');
        if (at <= 0 || at == value.Length - 1) return false;

        var local = value[..at];
        var rawDomain = value[(at + 1)..].TrimEnd('.');

        if (!IsValidLocalPart(local)) return false;
        if (!TryNormalizeDomain(rawDomain, out domain)) return false;

        normalized = $"{local.ToLowerInvariant()}@{domain}";
        return normalized.Length <= 254;
    }

    public static bool IsValid(string? input) => TryNormalize(input, out _, out _);

    public static bool IsValidLocalPart(string local)
    {
        if (local.Length is 0 or > 64) return false;
        if (local.StartsWith('.') || local.EndsWith('.') || local.Contains("..")) return false;
        foreach (var ch in local)
        {
            if (ch == '.') continue;
            if (char.IsAsciiLetterOrDigit(ch)) continue;
            if (AtextSpecials.Contains(ch)) continue;
            if (ch > 127 && !char.IsWhiteSpace(ch) && !char.IsControl(ch)) continue;
            return false;
        }
        return true;
    }

    public static bool TryNormalizeDomain(string rawDomain, out string asciiDomain)
    {
        asciiDomain = "";
        if (string.IsNullOrWhiteSpace(rawDomain) || rawDomain.Length > 253) return false;
        try
        {
            asciiDomain = Idn.GetAscii(rawDomain.ToLowerInvariant());
        }
        catch (ArgumentException)
        {
            return false;
        }

        var labels = asciiDomain.Split('.');
        if (labels.Length < 2) return false;
        foreach (var label in labels)
        {
            if (label.Length is 0 or > 63) return false;
            if (label.StartsWith('-') || label.EndsWith('-')) return false;
            if (!label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')) return false;
        }
        var tld = labels[^1];
        return tld.Length >= 2 && !tld.All(char.IsAsciiDigit);
    }

    public static string DomainOf(string email)
    {
        var at = email.LastIndexOf('@');
        return at < 0 ? "" : email[(at + 1)..].ToLowerInvariant();
    }

    public static string LocalPartOf(string email)
    {
        var at = email.LastIndexOf('@');
        return at < 0 ? email : email[..at];
    }
}
