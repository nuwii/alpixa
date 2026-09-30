using Alpixa.Core.Abstractions;

namespace Alpixa.Sending.Throttling;

public sealed class TokenBucket
{
    private readonly IClock _clock;
    private readonly Lock _lock = new();
    private double _tokens;
    private DateTime _lastRefillUtc;

    public TokenBucket(double capacity, TimeSpan refillPeriod, IClock clock, double? initialTokens = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
        RefillPerSecond = capacity / refillPeriod.TotalSeconds;
        _clock = clock;
        _tokens = Math.Clamp(initialTokens ?? capacity, 0, capacity);
        _lastRefillUtc = clock.UtcNow;
    }

    public double Capacity { get; }
    public double RefillPerSecond { get; private set; }

    public double AvailableTokens
    {
        get
        {
            lock (_lock)
            {
                Refill();
                return _tokens;
            }
        }
    }

    public TimeSpan TryTake(double tokens = 1)
    {
        lock (_lock)
        {
            Refill();
            if (_tokens >= tokens)
            {
                _tokens -= tokens;
                return TimeSpan.Zero;
            }
            var missing = tokens - _tokens;
            return TimeSpan.FromSeconds(missing / RefillPerSecond);
        }
    }

    public void Return(double tokens = 1)
    {
        lock (_lock)
        {
            _tokens = Math.Min(Capacity, _tokens + tokens);
        }
    }

    private void Refill()
    {
        var now = _clock.UtcNow;
        var elapsed = (now - _lastRefillUtc).TotalSeconds;
        if (elapsed <= 0) return;
        _tokens = Math.Min(Capacity, _tokens + elapsed * RefillPerSecond);
        _lastRefillUtc = now;
    }
}
