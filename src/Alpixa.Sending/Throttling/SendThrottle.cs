using Alpixa.Core.Abstractions;
using Alpixa.Core.Models;
using Alpixa.Core.Rules;
using Alpixa.Core.Settings;

namespace Alpixa.Sending.Throttling;

public interface IDelayProvider
{
    Task DelayAsync(TimeSpan delay, CancellationToken ct);
}

public sealed class TaskDelayProvider : IDelayProvider
{
    public Task DelayAsync(TimeSpan delay, CancellationToken ct) => delay <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(delay, ct);
}

public sealed class SendThrottle
{
    private const double MaxSlowdown = 8;
    private readonly TokenBucket _perMinute;
    private readonly TokenBucket _perHour;
    private readonly Dictionary<string, TokenBucket> _groups = new();
    private readonly IClock _clock;
    private readonly IDelayProvider _delay;
    private readonly Lock _lock = new();
    private readonly Queue<DateTime> _rateLimitHits = new();
    private int _successesSinceSlowdown;

    public SendThrottle(SenderProfile profile, SendingSettings settings, IClock clock, IDelayProvider delay, int sentInLastHour = 0)
    {
        _clock = clock;
        _delay = delay;
        _perMinute = new TokenBucket(Math.Max(1, profile.PerMinuteLimit), TimeSpan.FromMinutes(1), clock);
        _perHour = new TokenBucket(Math.Max(1, profile.HourlyLimit), TimeSpan.FromHours(1), clock,
            Math.Max(0, profile.HourlyLimit - sentInLastHour));
        foreach (var (group, perMinute) in settings.DomainGroupPerMinute)
            _groups[group] = new TokenBucket(Math.Max(1, perMinute), TimeSpan.FromMinutes(1), clock);
    }

    public double SlowdownFactor { get; private set; } = 1;

    public async Task WaitForSlotAsync(string recipientDomain, CancellationToken ct)
    {
        var group = RecipientDomainGroups.For(recipientDomain);
        var groupBucket = _groups.GetValueOrDefault(group) ?? _groups.GetValueOrDefault(RecipientDomainGroups.Other);

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            TimeSpan wait;
            lock (_lock)
            {
                var cost = SlowdownFactor;
                var w1 = _perMinute.TryTake(cost);
                if (w1 > TimeSpan.Zero)
                {
                    wait = w1;
                }
                else
                {
                    var w2 = _perHour.TryTake();
                    if (w2 > TimeSpan.Zero)
                    {
                        _perMinute.Return(cost);
                        wait = w2;
                    }
                    else
                    {
                        var w3 = groupBucket?.TryTake(cost) ?? TimeSpan.Zero;
                        if (w3 > TimeSpan.Zero)
                        {
                            _perMinute.Return(cost);
                            _perHour.Return();
                            wait = w3;
                        }
                        else
                        {
                            return;
                        }
                    }
                }
            }
            await _delay.DelayAsync(wait < TimeSpan.FromMilliseconds(50) ? TimeSpan.FromMilliseconds(50) : wait, ct);
        }
    }

    public void OnRateLimited()
    {
        lock (_lock)
        {
            var now = _clock.UtcNow;
            _rateLimitHits.Enqueue(now);
            while (_rateLimitHits.Count > 0 && _rateLimitHits.Peek() < now.AddMinutes(-10)) _rateLimitHits.Dequeue();
            if (_rateLimitHits.Count >= 2 || SlowdownFactor == 1)
                SlowdownFactor = Math.Min(MaxSlowdown, SlowdownFactor * 2);
            _successesSinceSlowdown = 0;
        }
    }

    public void OnSuccess()
    {
        lock (_lock)
        {
            if (SlowdownFactor <= 1) return;
            if (++_successesSinceSlowdown >= 50)
            {
                SlowdownFactor = Math.Max(1, SlowdownFactor / 2);
                _successesSinceSlowdown = 0;
            }
        }
    }
}
