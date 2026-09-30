using Alpixa.Core.Models;
using Alpixa.Core.Rules;
using Alpixa.Core.Settings;
using Alpixa.Sending.Queue;
using Alpixa.Sending.Throttling;
using Alpixa.Tests.Support;

namespace Alpixa.Tests.Unit;

public class ThrottlingTests
{
    [Fact]
    public void TokenBucket_allows_capacity_then_asks_to_wait()
    {
        var clock = new FakeClock(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        var bucket = new TokenBucket(10, TimeSpan.FromMinutes(1), clock);

        for (var i = 0; i < 10; i++) bucket.TryTake().Should().Be(TimeSpan.Zero);

        var wait = bucket.TryTake();
        wait.TotalSeconds.Should().BeApproximately(6, 0.01);
    }

    [Fact]
    public void TokenBucket_refills_over_time_but_not_above_capacity()
    {
        var clock = new FakeClock(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        var bucket = new TokenBucket(60, TimeSpan.FromMinutes(1), clock, initialTokens: 0);

        clock.Advance(TimeSpan.FromSeconds(30));
        bucket.AvailableTokens.Should().BeApproximately(30, 0.001);

        clock.Advance(TimeSpan.FromHours(1));
        bucket.AvailableTokens.Should().Be(60);
    }

    [Fact]
    public async Task SendThrottle_limits_per_minute_rate()
    {
        var clock = new FakeClock(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        var delay = new InstantDelay(clock);
        var profile = new SenderProfile { PerMinuteLimit = 10, HourlyLimit = 1000 };
        var throttle = new SendThrottle(profile, new SendingSettings(), clock, delay);
        var start = clock.UtcNow;

        for (var i = 0; i < 30; i++) await throttle.WaitForSlotAsync("ornek.com", CancellationToken.None);

        (clock.UtcNow - start).TotalSeconds.Should().BeGreaterThanOrEqualTo(119);
    }

    [Fact]
    public async Task SendThrottle_applies_per_domain_group_limits()
    {
        var clock = new FakeClock(new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
        var delay = new InstantDelay(clock);
        var profile = new SenderProfile { PerMinuteLimit = 1000, HourlyLimit = 100000 };
        var settings = new SendingSettings();
        settings.DomainGroupPerMinute[RecipientDomainGroups.Gmail] = 5;
        var throttle = new SendThrottle(profile, settings, clock, delay);
        var start = clock.UtcNow;

        for (var i = 0; i < 10; i++) await throttle.WaitForSlotAsync("gmail.com", CancellationToken.None);
        var gmailElapsed = clock.UtcNow - start;

        gmailElapsed.TotalSeconds.Should().BeGreaterThanOrEqualTo(59);
    }

    [Fact]
    public void SendThrottle_slows_down_after_rate_limit_and_recovers()
    {
        var clock = new FakeClock(DateTime.UtcNow);
        var throttle = new SendThrottle(new SenderProfile { PerMinuteLimit = 10, HourlyLimit = 100 }, new SendingSettings(), clock, new InstantDelay());

        throttle.OnRateLimited();
        throttle.OnRateLimited();
        throttle.SlowdownFactor.Should().Be(4);

        for (var i = 0; i < 50; i++) throttle.OnSuccess();
        throttle.SlowdownFactor.Should().Be(2);
    }

    [Fact]
    public void DomainInterleaver_spreads_same_domain_apart()
    {
        var items = Enumerable.Range(0, 50).Select(i => $"u{i}@gmail.com")
            .Concat(Enumerable.Range(0, 50).Select(i => $"u{i}@outlook.com"))
            .Concat(Enumerable.Range(0, 20).Select(i => $"u{i}@firma.com.tr"))
            .ToList();

        var result = DomainInterleaver.Interleave(items, EmailAddressRules.DomainOf);

        result.Should().HaveCount(items.Count).And.OnlyHaveUniqueItems();
        var maxRun = 1;
        var run = 1;
        for (var i = 1; i < result.Count; i++)
        {
            run = EmailAddressRules.DomainOf(result[i]) == EmailAddressRules.DomainOf(result[i - 1]) ? run + 1 : 1;
            maxRun = Math.Max(maxRun, run);
        }
        maxRun.Should().BeLessThanOrEqualTo(2);
    }

    [Fact]
    public void DomainInterleaver_handles_100k_items_quickly()
    {
        var items = Enumerable.Range(0, 100_000).Select(i => $"u{i}@d{i % 30_000}.com").ToList();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = DomainInterleaver.Interleave(items, EmailAddressRules.DomainOf);
        sw.Stop();
        result.Should().HaveCount(100_000);
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(1, 50)]
    [InlineData(2, 50)]
    [InlineData(3, 100)]
    [InlineData(6, 250)]
    [InlineData(7, 500)]
    [InlineData(10, 1000)]
    [InlineData(14, 2500)]
    [InlineData(15, 3000)]
    public void WarmupPlan_default_matches_documented_table(int day, int expected)
    {
        WarmupPlan.Default().LimitForDay(day).Should().Be(expected);
    }

    [Fact]
    public void WarmupPlan_day_number_starts_at_one()
    {
        var start = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        WarmupPlan.DayNumber(start, start.AddHours(3)).Should().Be(1);
        WarmupPlan.DayNumber(start, start.AddDays(2)).Should().Be(3);
    }
}
