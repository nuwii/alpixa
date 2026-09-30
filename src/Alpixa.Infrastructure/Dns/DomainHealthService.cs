using Alpixa.Core.Localization;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Alpixa.Core.Abstractions;
using Alpixa.Core.Dns;
using Alpixa.Core.Models;
using Alpixa.Core.Options;
using Alpixa.Core.Rules;
using Microsoft.Extensions.Logging;

namespace Alpixa.Infrastructure.Dns;

public sealed class DomainHealthService(IDnsResolver dns, AlpixaOptions options, ILogger<DomainHealthService> logger) : IDomainHealthService
{
    private static readonly HashSet<string> PersonalDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "gmail.com", "googlemail.com", "outlook.com", "hotmail.com", "live.com", "msn.com", "yahoo.com", "yandex.com", "icloud.com"
    };

    private readonly ConcurrentDictionary<string, (string? Result, DateTime At)> _dnsblCache = new();

    public async Task<DomainHealthReport> CheckAsync(SenderProfile profile, CancellationToken ct)
    {
        var domain = profile.FromDomain;
        var report = new DomainHealthReport { Domain = domain };

        if (profile.Kind == TransportKind.LocalTest)
        {
            report.Checks.Add(new("local", Msg.T("Health_TitleLocal"), CheckStatus.Info,
                Msg.T("Health_01")));
            return report;
        }

        if (string.IsNullOrEmpty(domain))
        {
            report.Checks.Add(new("from", Msg.T("Health_02"), CheckStatus.Fail, Msg.T("Health_03"), Msg.T("Health_04")));
            return report;
        }

        if (PersonalDomains.Contains(domain))
        {
            report.Checks.Add(new("personal", Msg.T("Health_05"), CheckStatus.Warning,
                Msg.T("Health_06", domain),
                Msg.T("Health_07")));
            return report;
        }

        var preset = ProviderPresets.For(profile.Kind);
        var isEsp = profile.Kind is TransportKind.AmazonSes or TransportKind.Brevo or TransportKind.Mailgun or TransportKind.SendGrid;
        var smtpIps = await SafeAsync(() => dns.GetAddressesAsync(profile.SmtpHost, ct), (IReadOnlyList<IPAddress>)[]);

        var spf = await CheckSpfAsync(profile, preset, isEsp, smtpIps, ct);
        report.Checks.Add(spf.Check);
        var dkim = await CheckDkimAsync(profile, preset, ct);
        report.Checks.Add(dkim.Check);
        report.Checks.Add(await CheckDmarcAsync(domain, ct));
        report.Checks.Add(await CheckMxAsync(domain, ct));

        if (profile.Kind == TransportKind.CustomSmtp)
            report.Checks.Add(await CheckPtrAsync(profile.SmtpHost, smtpIps, ct));

        report.Checks.Add(CheckAlignment(profile, spf.Covered, dkim.AlignedDomain));

        if (profile.Kind == TransportKind.CustomSmtp)
            report.Checks.Add(await CheckIpBlocklistsAsync(smtpIps, ct));
        report.Checks.Add(await CheckDomainBlocklistsAsync(domain, ct));

        return report;
    }

    private async Task<(DomainCheck Check, bool Covered)> CheckSpfAsync(SenderProfile profile, ProviderPreset preset, bool isEsp,
        IReadOnlyList<IPAddress> smtpIps, CancellationToken ct)
    {
        var domain = profile.FromDomain;
        var suggestedInclude = preset.SpfIncludes.Length > 0 ? $"include:{preset.SpfIncludes[0]} " : smtpIps.Count > 0 ? $"ip4:{smtpIps[0]} " : "";
        var suggested = $"{domain}.  TXT  \"v=spf1 {suggestedInclude}~all\"";

        var txts = await SafeAsync(() => dns.GetTxtAsync(domain, ct), (IReadOnlyList<string>)[]);
        var spfRecords = txts.Where(SpfRecord.IsSpf).ToList();
        if (spfRecords.Count == 0)
        {
            var status = isEsp ? CheckStatus.Warning : CheckStatus.Fail;
            return (new DomainCheck("spf", "SPF", status, Msg.T("Health_08"),
                Msg.T("Health_09"), suggested), false);
        }
        if (spfRecords.Count > 1)
        {
            return (new DomainCheck("spf", "SPF", CheckStatus.Fail, Msg.T("Health_10", spfRecords.Count),
                Msg.T("Health_11"), suggested), false);
        }

        var expansion = new SpfExpansion();
        await ExpandSpfAsync(domain, spfRecords[0], expansion, 0, ct);
        var record = SpfRecord.Parse(spfRecords[0])!;

        if (record.All?.Qualifier == SpfQualifier.Pass)
            return (new DomainCheck("spf", "SPF", CheckStatus.Fail, Msg.T("Health_12"),
                Msg.T("Health_13"), spfRecords[0].Replace("+all", "~all")), false);

        if (expansion.Lookups > 10)
            return (new DomainCheck("spf", "SPF", CheckStatus.Fail, Msg.T("Health_14", expansion.Lookups),
                Msg.T("Health_15")), false);

        bool covered;
        if (preset.SpfIncludes.Length > 0)
            covered = preset.SpfIncludes.Any(i => expansion.Includes.Contains(i));
        else
            covered = smtpIps.Any(ip => expansion.Ranges.Any(r => SpfRecord.IpMatches(r, ip)));

        if (!covered)
        {
            var status = isEsp ? CheckStatus.Warning : CheckStatus.Fail;
            var what = preset.SpfIncludes.Length > 0 ? $"include:{preset.SpfIncludes[0]}" : Msg.T("Health_16", string.Join(", ", smtpIps.Take(2)));
            return (new DomainCheck("spf", "SPF", status, Msg.T("Health_17", what),
                Msg.T("Health_18", what), spfRecords[0]), false);
        }

        var note = expansion.Lookups >= 8 ? Msg.T("Health_19", expansion.Lookups) : "";
        return (new DomainCheck("spf", "SPF", expansion.Lookups >= 8 ? CheckStatus.Warning : CheckStatus.Pass,
            Msg.T("Health_20") + note), true);
    }

    private async Task ExpandSpfAsync(string domain, string txt, SpfExpansion acc, int depth, CancellationToken ct)
    {
        if (depth > 10 || !acc.Visited.Add(domain)) return;
        var record = SpfRecord.Parse(txt);
        if (record is null) return;

        foreach (var term in record.Terms)
        {
            switch (term.Mechanism)
            {
                case "include" when term.Value is not null:
                    acc.Lookups++;
                    acc.Includes.Add(term.Value.ToLowerInvariant());
                    var childTxt = (await SafeAsync(() => dns.GetTxtAsync(term.Value, ct), (IReadOnlyList<string>)[])).FirstOrDefault(SpfRecord.IsSpf);
                    if (childTxt is not null) await ExpandSpfAsync(term.Value, childTxt, acc, depth + 1, ct);
                    break;
                case "a":
                    acc.Lookups++;
                    foreach (var ip in await SafeAsync(() => dns.GetAddressesAsync(StripCidr(term.Value) ?? domain, ct), (IReadOnlyList<IPAddress>)[]))
                        acc.Ranges.Add(ip.ToString());
                    break;
                case "mx":
                    acc.Lookups++;
                    foreach (var mx in await SafeAsync(() => dns.GetMxAsync(StripCidr(term.Value) ?? domain, ct), (IReadOnlyList<string>)[]))
                    foreach (var ip in await SafeAsync(() => dns.GetAddressesAsync(mx, ct), (IReadOnlyList<IPAddress>)[]))
                        acc.Ranges.Add(ip.ToString());
                    break;
                case "ip4":
                case "ip6":
                    if (term.Value is not null) acc.Ranges.Add(term.Value);
                    break;
                case "ptr":
                case "exists":
                    acc.Lookups++;
                    break;
            }
        }

        if (record.Redirect is not null)
        {
            acc.Lookups++;
            var redirectTxt = (await SafeAsync(() => dns.GetTxtAsync(record.Redirect, ct), (IReadOnlyList<string>)[])).FirstOrDefault(SpfRecord.IsSpf);
            if (redirectTxt is not null) await ExpandSpfAsync(record.Redirect, redirectTxt, acc, depth + 1, ct);
        }
    }

    private static string? StripCidr(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        var slash = value.IndexOf('/');
        return slash == 0 ? null : slash > 0 ? value[..slash] : value;
    }

    private async Task<(DomainCheck Check, string? AlignedDomain)> CheckDkimAsync(SenderProfile profile, ProviderPreset preset, CancellationToken ct)
    {
        var dkimDomain = string.IsNullOrWhiteSpace(profile.DkimDomain) ? profile.FromDomain : profile.DkimDomain!.ToLowerInvariant();
        var explicitSelector = !string.IsNullOrWhiteSpace(profile.DkimSelector);
        var selectors = explicitSelector ? [profile.DkimSelector!] : preset.CommonDkimSelectors;

        foreach (var selector in selectors)
        {
            var name = $"{selector}._domainkey.{dkimDomain}";
            var txts = await SafeAsync(() => dns.GetTxtAsync(name, ct), (IReadOnlyList<string>)[]);
            var record = txts.Select(DkimRecord.Parse).FirstOrDefault(r => r is not null);
            if (record is null) continue;

            if (record.IsRevoked)
                return (new DomainCheck("dkim", "DKIM", CheckStatus.Fail, Msg.T("Health_21", selector),
                    Msg.T("Health_22")), null);

            var bits = record.KeyBits();
            var aligned = OrganizationalDomain.RelaxedAligned(dkimDomain, profile.FromDomain) ? dkimDomain : null;
            if (bits is < 1024)
                return (new DomainCheck("dkim", "DKIM", CheckStatus.Fail, Msg.T("Health_23", bits),
                    Msg.T("Health_24")), aligned);
            if (bits == 1024)
                return (new DomainCheck("dkim", "DKIM", CheckStatus.Warning, Msg.T("Health_25", selector),
                    Msg.T("Health_26")), aligned);
            return (new DomainCheck("dkim", "DKIM", CheckStatus.Pass, Msg.T("Health_27", selector, (bits is null ? "" : $", {bits} bit"))), aligned);
        }

        var hint = explicitSelector
            ? Msg.T("Health_28", profile.DkimSelector, dkimDomain)
            : Msg.T("Health_29");
        var example = $"{(explicitSelector ? profile.DkimSelector : "selector")}._domainkey.{dkimDomain}.  TXT  \"v=DKIM1; k=rsa; p=MIIBIjANBgkqh...\"";
        return (new DomainCheck("dkim", "DKIM", CheckStatus.Fail, Msg.T("Health_30"), hint, example), null);
    }

    private async Task<DomainCheck> CheckDmarcAsync(string domain, CancellationToken ct)
    {
        var suggested = $"_dmarc.{domain}.  TXT  \"v=DMARC1; p=none; rua=mailto:dmarc@{OrganizationalDomain.Of(domain)}\"";
        var record = await FindDmarcAsync(domain, ct) ?? await FindDmarcAsync(OrganizationalDomain.Of(domain), ct);
        if (record is null)
            return new DomainCheck("dmarc", "DMARC", CheckStatus.Fail, Msg.T("Health_31"),
                Msg.T("Health_32"), suggested);

        if (record.Policy == "none")
            return new DomainCheck("dmarc", "DMARC", CheckStatus.Info,
                Msg.T("Health_33") +
                (record.AggregateReportUri is null ? Msg.T("Health_34") : ""));

        return new DomainCheck("dmarc", "DMARC", CheckStatus.Pass, Msg.T("Health_35", record.Policy));
    }

    private async Task<DmarcRecord?> FindDmarcAsync(string domain, CancellationToken ct)
    {
        var txts = await SafeAsync(() => dns.GetTxtAsync($"_dmarc.{domain}", ct), (IReadOnlyList<string>)[]);
        return txts.Select(DmarcRecord.Parse).FirstOrDefault(r => r is not null);
    }

    private async Task<DomainCheck> CheckMxAsync(string domain, CancellationToken ct)
    {
        var mx = await SafeAsync(() => dns.GetMxAsync(domain, ct), (IReadOnlyList<string>)[]);
        return mx.Count > 0 && mx.Any(m => m.Length > 0)
            ? new DomainCheck("mx", "MX", CheckStatus.Pass, Msg.T("Health_36", mx[0]))
            : new DomainCheck("mx", "MX", CheckStatus.Warning, Msg.T("Health_37"),
                Msg.T("Health_38"));
    }

    private async Task<DomainCheck> CheckPtrAsync(string host, IReadOnlyList<IPAddress> ips, CancellationToken ct)
    {
        var publicIps = ips.Where(IsPublic).ToList();
        if (publicIps.Count == 0)
            return new DomainCheck("ptr", Msg.T("Health_TitlePtr"), CheckStatus.Info, Msg.T("Health_39"));

        foreach (var ip in publicIps)
        {
            var names = await SafeAsync(() => dns.GetPtrAsync(ip, ct), (IReadOnlyList<string>)[]);
            foreach (var name in names)
            {
                var forward = await SafeAsync(() => dns.GetAddressesAsync(name, ct), (IReadOnlyList<IPAddress>)[]);
                if (forward.Contains(ip))
                    return new DomainCheck("ptr", Msg.T("Health_TitlePtr"), CheckStatus.Pass, Msg.T("Health_40", ip, name));
            }
        }

        return new DomainCheck("ptr", Msg.T("Health_TitlePtr"), CheckStatus.Fail,
            Msg.T("Health_41", publicIps[0]),
            Msg.T("Health_42", publicIps[0], host));
    }

    private static DomainCheck CheckAlignment(SenderProfile profile, bool spfCovered, string? dkimAlignedDomain)
    {
        if (dkimAlignedDomain is not null)
            return new DomainCheck("alignment", Msg.T("Health_TitleAlignment"), CheckStatus.Pass,
                Msg.T("Health_43"));

        var envelopeIsFromDomain = profile.Kind is TransportKind.CustomSmtp or TransportKind.Gmail or TransportKind.Microsoft365;
        if (spfCovered && envelopeIsFromDomain)
            return new DomainCheck("alignment", Msg.T("Health_TitleAlignment"), CheckStatus.Warning,
                Msg.T("Health_61"),
                Msg.T("Health_44"));

        return new DomainCheck("alignment", Msg.T("Health_TitleAlignment"), CheckStatus.Fail,
            Msg.T("Health_62"),
            Msg.T("Health_45"));
    }

    private async Task<DomainCheck> CheckIpBlocklistsAsync(IReadOnlyList<IPAddress> ips, CancellationToken ct)
    {
        var publicV4 = ips.Where(ip => ip.AddressFamily == AddressFamily.InterNetwork && IsPublic(ip)).ToList();
        if (publicV4.Count == 0)
            return new DomainCheck("dnsbl-ip", Msg.T("Health_46"), CheckStatus.Info, Msg.T("Health_47"));

        var listed = new List<string>();
        var unknown = new List<string>();
        foreach (var ip in publicV4.Take(3))
        {
            var reversed = string.Join('.', ip.GetAddressBytes().Reverse());
            foreach (var zone in options.DnsblIpZones)
            {
                var result = await QueryDnsblAsync($"{reversed}.{zone}", ct);
                if (result is null) continue;
                if (result.StartsWith("127.255.255.")) unknown.Add(zone);
                else if (result.StartsWith("127.")) listed.Add($"{ip} → {zone}");
            }
        }

        if (listed.Count > 0)
            return new DomainCheck("dnsbl-ip", Msg.T("Health_48"), CheckStatus.Fail, $"Sunucu IP'niz kara listede: {string.Join(", ", listed)}.",
                Msg.T("Health_49"));
        if (unknown.Count > 0)
            return new DomainCheck("dnsbl-ip", Msg.T("Health_50"), CheckStatus.Info,
                Msg.T("Health_51", string.Join(", ", unknown.Distinct())));
        return new DomainCheck("dnsbl-ip", Msg.T("Health_52"), CheckStatus.Pass, Msg.T("Health_53"));
    }

    private async Task<DomainCheck> CheckDomainBlocklistsAsync(string domain, CancellationToken ct)
    {
        var org = OrganizationalDomain.Of(domain);
        foreach (var zone in options.DnsblDomainZones)
        {
            var result = await QueryDnsblAsync($"{org}.{zone}", ct);
            if (result is null) continue;
            if (result.StartsWith("127.255.255."))
                return new DomainCheck("dnsbl-domain", Msg.T("Health_54"), CheckStatus.Info,
                    Msg.T("Health_55", zone));
            if (result.StartsWith("127.0.1."))
                return new DomainCheck("dnsbl-domain", Msg.T("Health_56"), CheckStatus.Fail, Msg.T("Health_57", zone),
                    Msg.T("Health_58"));
        }
        return new DomainCheck("dnsbl-domain", Msg.T("Health_59"), CheckStatus.Pass, Msg.T("Health_60"));
    }

    private async Task<string?> QueryDnsblAsync(string name, CancellationToken ct)
    {
        if (_dnsblCache.TryGetValue(name, out var hit) && hit.At > DateTime.UtcNow.AddHours(-1)) return hit.Result;
        var addresses = await SafeAsync(() => dns.GetAddressesAsync(name, ct), (IReadOnlyList<IPAddress>)[]);
        var result = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString();
        _dnsblCache[name] = (result, DateTime.UtcNow);
        await Task.Delay(100, ct);
        return result;
    }

    private static bool IsPublic(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return false;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return !(ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal);
        var b = ip.GetAddressBytes();
        return !(b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254) || b[0] == 127);
    }

    private async Task<T> SafeAsync<T>(Func<Task<T>> action, T fallback)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "DNS query failed");
            return fallback;
        }
    }

    private sealed class SpfExpansion
    {
        public int Lookups { get; set; }
        public HashSet<string> Includes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Ranges { get; } = new();
        public HashSet<string> Visited { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
