using System.Collections.Concurrent;
using System.Net;
using Alpixa.Core.Abstractions;
using Alpixa.Core.Models;
using Alpixa.Infrastructure.Dns;
using Alpixa.Sending.Throttling;
using MimeKit;

namespace Alpixa.Tests.Support;

public sealed class FakeClock(DateTime utcNow) : IClock
{
    public DateTime UtcNow { get; set; } = utcNow;
    public DateTime LocalNow => UtcNow.ToLocalTime();
    public void Advance(TimeSpan by) => UtcNow = UtcNow.Add(by);
}

public sealed class InstantDelay : IDelayProvider
{
    private readonly FakeClock? _clock;
    public InstantDelay(FakeClock? clock = null) => _clock = clock;
    public TimeSpan Total { get; private set; }

    public Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Total += delay;
        _clock?.Advance(delay);
        return Task.CompletedTask;
    }
}

public sealed class InMemorySecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<string, string> _values = new();
    public Task<string?> GetAsync(string key) => Task.FromResult(_values.TryGetValue(key, out var v) ? v : null);
    public Task SetAsync(string key, string value) { _values[key] = value; return Task.CompletedTask; }
    public Task RemoveAsync(string key) { _values.TryRemove(key, out _); return Task.CompletedTask; }
}

public sealed class NullPlatformService : IPlatformService
{
    public List<string> Notifications { get; } = new();
    public bool KeepAwake { get; private set; }
    public void SetKeepAwake(bool keepAwake) => KeepAwake = keepAwake;
    public void ShowNotification(string title, string message) { lock (Notifications) Notifications.Add(title); }
    public void SetProgress(double? fraction) { }
    public void OpenFolder(string path) { }
    public Task OpenBrowserAsync(Uri uri) => Task.CompletedTask;
    public void AcceptDragOver(object platformDragOverArgs) { }
    public Task<IReadOnlyList<string>> GetDroppedFilePathsAsync(object platformDropArgs) => Task.FromResult<IReadOnlyList<string>>([]);
}

public sealed class TempAppPaths : IAppPaths, IDisposable
{
    public TempAppPaths()
    {
        DataDirectory = Path.Combine(Path.GetTempPath(), "alpixa-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DataDirectory);
    }

    public string DataDirectory { get; }
    public string DatabasePath => Path.Combine(DataDirectory, "alpixa.db");
    public string LogDirectory => Path.Combine(DataDirectory, "logs");

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(DataDirectory, true); } catch (IOException) { }
    }
}

public sealed class FakeDnsResolver : IDnsResolver
{
    public Dictionary<string, List<string>> Txt { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> Mx { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<IPAddress>> A { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<string>> Ptr { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool DefaultHasMx { get; set; } = true;

    public Task<IReadOnlyList<string>> GetTxtAsync(string name, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<string>>(Txt.GetValueOrDefault(name) ?? []);

    public Task<IReadOnlyList<string>> GetMxAsync(string domain, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<string>>(Mx.GetValueOrDefault(domain) ?? (DefaultHasMx ? ["mx." + domain] : []));

    public Task<IReadOnlyList<IPAddress>> GetAddressesAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var ip)) return Task.FromResult<IReadOnlyList<IPAddress>>([ip]);
        return Task.FromResult<IReadOnlyList<IPAddress>>(A.GetValueOrDefault(host) ?? []);
    }

    public Task<IReadOnlyList<string>> GetPtrAsync(IPAddress address, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<string>>(Ptr.GetValueOrDefault(address.ToString()) ?? []);
}

public sealed class ScriptedTransport : IMailTransport
{
    private readonly Func<MimeMessage, int, SendResult> _behaviour;
    private int _calls;

    public ScriptedTransport(Func<MimeMessage, int, SendResult> behaviour, ConcurrentBag<string> delivered)
    {
        _behaviour = behaviour;
        Delivered = delivered;
    }

    public ConcurrentBag<string> Delivered { get; }
    public int MessagesOnConnection { get; private set; }
    public Task ConnectAsync(CancellationToken ct) => Task.CompletedTask;

    public Task<SendResult> SendAsync(MimeMessage message, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var result = _behaviour(message, Interlocked.Increment(ref _calls));
        if (result.Success)
        {
            Delivered.Add(message.To.Mailboxes.First().Address.ToLowerInvariant());
            MessagesOnConnection++;
        }
        return Task.FromResult(result);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class ScriptedTransportFactory : IMailTransportFactory
{
    public ConcurrentBag<string> Delivered { get; } = new();
    public Func<MimeMessage, int, SendResult> Behaviour { get; set; } = (_, _) => SendResult.Ok();
    public Action<MimeMessage>? BeforeSend { get; set; }

    public Task<IMailTransport> CreateAsync(SenderProfile profile, bool dryRun, CancellationToken ct)
        => Task.FromResult<IMailTransport>(new ScriptedTransport((m, n) =>
        {
            BeforeSend?.Invoke(m);
            return Behaviour(m, n);
        }, Delivered));
}
