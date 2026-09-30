using System.Net;
using System.Security.Cryptography;

namespace Alpixa.Core.Dns;

public enum SpfQualifier { Pass, Fail, SoftFail, Neutral }

public sealed record SpfTerm(SpfQualifier Qualifier, string Mechanism, string? Value);

public sealed class SpfRecord
{
    public IReadOnlyList<SpfTerm> Terms { get; init; } = Array.Empty<SpfTerm>();
    public string? Redirect { get; init; }
    public SpfTerm? All => Terms.FirstOrDefault(t => t.Mechanism == "all");

    public IEnumerable<string> Includes => Terms.Where(t => t.Mechanism == "include" && t.Value is not null).Select(t => t.Value!);

    public int DirectLookupCount => Terms.Count(t => t.Mechanism is "include" or "a" or "mx" or "ptr" or "exists") + (Redirect is null ? 0 : 1);

    public static bool IsSpf(string txt) => txt.TrimStart().StartsWith("v=spf1", StringComparison.OrdinalIgnoreCase)
        && (txt.TrimStart().Length == 6 || txt.TrimStart()[6] == ' ');

    public static SpfRecord? Parse(string txt)
    {
        if (!IsSpf(txt)) return null;
        var terms = new List<SpfTerm>();
        string? redirect = null;
        foreach (var raw in txt.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            var token = raw.Trim();
            if (token.StartsWith("redirect=", StringComparison.OrdinalIgnoreCase))
            {
                redirect = token[9..];
                continue;
            }
            if (token.Contains('=')) continue;

            var qualifier = SpfQualifier.Pass;
            switch (token[0])
            {
                case '+': token = token[1..]; break;
                case '-': qualifier = SpfQualifier.Fail; token = token[1..]; break;
                case '~': qualifier = SpfQualifier.SoftFail; token = token[1..]; break;
                case '?': qualifier = SpfQualifier.Neutral; token = token[1..]; break;
            }

            var sep = token.IndexOfAny([':', '/']);
            string mechanism;
            string? value = null;
            if (sep < 0) mechanism = token.ToLowerInvariant();
            else
            {
                mechanism = token[..sep].ToLowerInvariant();
                value = token[sep] == ':' ? token[(sep + 1)..] : token[sep..];
            }
            terms.Add(new SpfTerm(qualifier, mechanism, value));
        }
        return new SpfRecord { Terms = terms, Redirect = redirect };
    }

    public static bool IpMatches(string cidr, IPAddress address)
    {
        var parts = cidr.Split('/');
        if (!IPAddress.TryParse(parts[0], out var network)) return false;
        if (network.AddressFamily != address.AddressFamily) return false;
        var maxBits = network.GetAddressBytes().Length * 8;
        var prefix = parts.Length > 1 && int.TryParse(parts[1], out var p) ? p : maxBits;
        var netBytes = network.GetAddressBytes();
        var addrBytes = address.GetAddressBytes();
        for (var i = 0; i < netBytes.Length && prefix > 0; i++, prefix -= 8)
        {
            var mask = prefix >= 8 ? 0xFF : (byte)(0xFF << (8 - prefix));
            if ((netBytes[i] & mask) != (addrBytes[i] & mask)) return false;
        }
        return true;
    }
}

public sealed class DmarcRecord
{
    public IReadOnlyDictionary<string, string> Tags { get; init; } = new Dictionary<string, string>();
    public string Policy => Tags.TryGetValue("p", out var p) ? p.ToLowerInvariant() : "";
    public string? SubdomainPolicy => Tags.TryGetValue("sp", out var sp) ? sp.ToLowerInvariant() : null;
    public string? AggregateReportUri => Tags.TryGetValue("rua", out var rua) ? rua : null;
    public int Percent => Tags.TryGetValue("pct", out var pct) && int.TryParse(pct, out var v) ? v : 100;
    public string DkimAlignment => Tags.TryGetValue("adkim", out var a) ? a.ToLowerInvariant() : "r";
    public string SpfAlignment => Tags.TryGetValue("aspf", out var a) ? a.ToLowerInvariant() : "r";

    public static DmarcRecord? Parse(string txt)
    {
        var tags = ParseTags(txt);
        if (!tags.TryGetValue("v", out var v) || !v.Equals("DMARC1", StringComparison.OrdinalIgnoreCase)) return null;
        return new DmarcRecord { Tags = tags };
    }

    internal static Dictionary<string, string> ParseTags(string txt)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in txt.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;
            tags[part[..eq].Trim()] = part[(eq + 1)..].Trim();
        }
        return tags;
    }
}

public sealed class DkimRecord
{
    public IReadOnlyDictionary<string, string> Tags { get; init; } = new Dictionary<string, string>();
    public string KeyType => Tags.TryGetValue("k", out var k) ? k.ToLowerInvariant() : "rsa";
    public string PublicKey => Tags.TryGetValue("p", out var p) ? p.Replace(" ", "") : "";
    public bool IsRevoked => PublicKey.Length == 0;

    public static DkimRecord? Parse(string txt)
    {
        var tags = DmarcRecord.ParseTags(txt);
        if (!tags.ContainsKey("p")) return null;
        if (tags.TryGetValue("v", out var v) && !v.Equals("DKIM1", StringComparison.OrdinalIgnoreCase)) return null;
        return new DkimRecord { Tags = tags };
    }

    public int? KeyBits()
    {
        if (IsRevoked) return null;
        if (KeyType == "ed25519") return 256;
        try
        {
            using var rsa = RSA.Create();
            var bytes = Convert.FromBase64String(PublicKey);
            try { rsa.ImportSubjectPublicKeyInfo(bytes, out _); }
            catch (CryptographicException) { rsa.ImportRSAPublicKey(bytes, out _); }
            return rsa.KeySize;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return null;
        }
    }
}

public static class OrganizationalDomain
{
    private static readonly HashSet<string> MultiLabelSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "com.tr", "net.tr", "org.tr", "gen.tr", "biz.tr", "info.tr", "web.tr", "av.tr", "bel.tr", "edu.tr", "gov.tr", "k12.tr",
        "co.uk", "org.uk", "ac.uk", "gov.uk", "com.au", "net.au", "org.au", "co.nz", "co.jp", "com.br", "com.cn", "co.za",
        "com.mx", "com.ar", "co.in", "com.sg", "com.hk", "co.kr", "com.de", "com.pl", "com.ua", "com.ru"
    };

    public static string Of(string domain)
    {
        var labels = domain.ToLowerInvariant().TrimEnd('.').Split('.');
        if (labels.Length <= 2) return string.Join('.', labels);
        var lastTwo = $"{labels[^2]}.{labels[^1]}";
        return MultiLabelSuffixes.Contains(lastTwo) ? $"{labels[^3]}.{lastTwo}" : lastTwo;
    }

    public static bool RelaxedAligned(string a, string b) => Of(a) == Of(b);
}
