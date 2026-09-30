using System.Collections.Concurrent;
using Alpixa.Core.Abstractions;
using Alpixa.Core.Models;
using Alpixa.Core.Settings;

namespace Alpixa.Sending.Throttling;

public sealed class ThrottleRegistry(IClock clock, IDelayProvider delay)
{
    private readonly ConcurrentDictionary<int, (SendThrottle Throttle, string Signature)> _throttles = new();

    public SendThrottle Get(SenderProfile profile, SendingSettings settings, int sentInLastHour)
    {
        var signature = $"{profile.PerMinuteLimit}/{profile.HourlyLimit}/{string.Join(',', settings.DomainGroupPerMinute.Select(kv => $"{kv.Key}={kv.Value}"))}";
        var entry = _throttles.AddOrUpdate(profile.Id,
            _ => (new SendThrottle(profile, settings, clock, delay, sentInLastHour), signature),
            (_, existing) => existing.Signature == signature ? existing : (new SendThrottle(profile, settings, clock, delay, sentInLastHour), signature));
        return entry.Throttle;
    }
}
