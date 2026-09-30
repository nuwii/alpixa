using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Alpixa.Core.Content;

public static partial class HtmlToText
{
    [GeneratedRegex(@"<(script|style|head)[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex NonContent();

    [GeneratedRegex(@"<a\s[^>]*href\s*=\s*[""']([^""']+)[""'][^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Anchor();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"</(p|div|h[1-6]|tr|table|ul|ol|blockquote)>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEnd();

    [GeneratedRegex(@"<li[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItem();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ManyNewlines();

    public static string Convert(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var text = html.Replace("\r\n", "\n");
        text = NonContent().Replace(text, "");
        text = Anchor().Replace(text, m =>
        {
            var href = m.Groups[1].Value.Trim();
            var label = AnyTag().Replace(m.Groups[2].Value, "").Trim();
            if (string.IsNullOrEmpty(label) || label == href || "mailto:" + label == href)
                return string.IsNullOrEmpty(label) ? href : label;
            return $"{label} ({href})";
        });
        text = LineBreak().Replace(text, "\n");
        text = BlockEnd().Replace(text, "\n\n");
        text = ListItem().Replace(text, "\n- ");
        text = AnyTag().Replace(text, "");
        text = WebUtility.HtmlDecode(text).Replace('\u00A0', ' ');

        var sb = new StringBuilder();
        foreach (var line in text.Split('\n'))
            sb.Append(Spaces().Replace(line, " ").Trim()).Append('\n');

        return ManyNewlines().Replace(sb.ToString(), "\n\n").Trim() + "\n";
    }
}
