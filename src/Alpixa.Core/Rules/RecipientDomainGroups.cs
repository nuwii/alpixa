namespace Alpixa.Core.Rules;

public static class RecipientDomainGroups
{
    public const string Gmail = "gmail";
    public const string Microsoft = "microsoft";
    public const string Yahoo = "yahoo";
    public const string Yandex = "yandex";
    public const string Other = "other";

    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gmail.com"] = Gmail,
        ["googlemail.com"] = Gmail,
        ["outlook.com"] = Microsoft,
        ["hotmail.com"] = Microsoft,
        ["hotmail.com.tr"] = Microsoft,
        ["live.com"] = Microsoft,
        ["msn.com"] = Microsoft,
        ["outlook.com.tr"] = Microsoft,
        ["yahoo.com"] = Yahoo,
        ["ymail.com"] = Yahoo,
        ["rocketmail.com"] = Yahoo,
        ["aol.com"] = Yahoo,
        ["yandex.com"] = Yandex,
        ["yandex.com.tr"] = Yandex,
        ["yandex.ru"] = Yandex,
        ["ya.ru"] = Yandex
    };

    public static string For(string domain)
    {
        if (Known.TryGetValue(domain, out var group)) return group;
        if (domain.StartsWith("yahoo.", StringComparison.OrdinalIgnoreCase)) return Yahoo;
        if (domain.StartsWith("hotmail.", StringComparison.OrdinalIgnoreCase)) return Microsoft;
        return Other;
    }
}
