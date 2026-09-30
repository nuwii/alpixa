using System.Collections.Concurrent;
using System.Net;
using DnsClient;
using DnsClient.Protocol;
using Alpixa.Core.Abstractions;

namespace Alpixa.Infrastructure.Dns;

public interface IDnsResolver
{
    Task<IReadOnlyList<string>> GetTxtAsync(string name, CancellationToken ct);
    Task<IReadOnlyList<string>> GetMxAsync(string domain, CancellationToken ct);
    Task<IReadOnlyList<IPAddress>> GetAddressesAsync(string host, CancellationToken ct);
    Task<IReadOnlyList<string>> GetPtrAsync(IPAddress address, CancellationToken ct);
}

public sealed class DnsClientResolver : IDnsResolver
{
    private readonly LookupClient _client = new(new LookupClientOptions
    {
        UseCache = true,
        Timeout = TimeSpan.FromSeconds(5),
        Retries = 2,
        ContinueOnDnsError = true,
        ThrowDnsErrors = false
    });

    public async Task<IReadOnlyList<string>> GetTxtAsync(string name, CancellationToken ct)
    {
        var result = await _client.QueryAsync(name, QueryType.TXT, cancellationToken: ct);
        return result.Answers.TxtRecords().Select(r => string.Concat(r.Text)).ToList();
    }

    public async Task<IReadOnlyList<string>> GetMxAsync(string domain, CancellationToken ct)
    {
        var result = await _client.QueryAsync(domain, QueryType.MX, cancellationToken: ct);
        return result.Answers.MxRecords().OrderBy(m => m.Preference).Select(m => m.Exchange.Value.TrimEnd('.')).ToList();
    }

    public async Task<IReadOnlyList<IPAddress>> GetAddressesAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var literal)) return [literal];
        var a = await _client.QueryAsync(host, QueryType.A, cancellationToken: ct);
        var aaaa = await _client.QueryAsync(host, QueryType.AAAA, cancellationToken: ct);
        return a.Answers.ARecords().Select(r => r.Address)
            .Concat(aaaa.Answers.AaaaRecords().Select(r => r.Address))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetPtrAsync(IPAddress address, CancellationToken ct)
    {
        var result = await _client.QueryReverseAsync(address, ct);
        return result.Answers.PtrRecords().Select(p => p.PtrDomainName.Value.TrimEnd('.')).ToList();
    }
}

public sealed class MxChecker(IDnsResolver dns) : IMxChecker
{
    private readonly ConcurrentDictionary<string, (bool Ok, DateTime At)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<bool> HasMailServerAsync(string domain, CancellationToken ct)
    {
        if (_cache.TryGetValue(domain, out var hit) && hit.At > DateTime.UtcNow.AddHours(-6)) return hit.Ok;
        bool ok;
        try
        {
            var mx = await dns.GetMxAsync(domain, ct);
            ok = mx.Count > 0 ? mx.Any(m => m.Length > 0) : (await dns.GetAddressesAsync(domain, ct)).Count > 0;
        }
        catch (DnsResponseException)
        {
            ok = true;
        }
        _cache[domain] = (ok, DateTime.UtcNow);
        return ok;
    }
}
