using System.Net;
using Alpixa.Core.Abstractions;
using Scriban;
using Scriban.Runtime;

namespace Alpixa.Infrastructure.Templates;

public sealed class ScribanTemplateRenderer : ITemplateRenderer
{
    private readonly Dictionary<string, Template> _cache = new();
    private readonly Lock _lock = new();

    public string Render(string template, IReadOnlyDictionary<string, object?> model, bool htmlEncodeValues = false)
    {
        var parsed = GetTemplate(template);
        if (parsed.HasErrors)
            throw new TemplateException(string.Join(Environment.NewLine, parsed.Messages.Select(m => m.ToString())));

        var globals = new ScriptObject();
        foreach (var (key, value) in model)
        {
            object? v = value is string s && string.IsNullOrWhiteSpace(s) ? null : value;
            if (htmlEncodeValues && v is string str)
                v = WebUtility.HtmlEncode(str);
            globals.SetValue(key, v, true);
        }

        var context = new TemplateContext
        {
            StrictVariables = false,
            MemberRenamer = m => m.Name,
            EnableRelaxedMemberAccess = true
        };
        context.PushGlobal(globals);
        return parsed.Render(context);
    }

    public IReadOnlyList<string> Validate(string template)
    {
        var parsed = Template.Parse(template ?? "");
        return parsed.HasErrors ? parsed.Messages.Select(m => m.ToString()).ToList() : [];
    }

    private Template GetTemplate(string text)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(text, out var t)) return t;
            t = Template.Parse(text ?? "");
            if (_cache.Count > 64) _cache.Clear();
            _cache[text ?? ""] = t;
            return t;
        }
    }
}

public sealed class TemplateException(string message) : Exception(message);
