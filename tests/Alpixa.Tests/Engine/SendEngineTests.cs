using Alpixa.Core.Models;
using Alpixa.Core.Settings;
using Alpixa.Infrastructure.Settings;
using Alpixa.Sending.Engine;
using Alpixa.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace Alpixa.Tests.Engine;

public class SendEngineTests
{
    private static IEnumerable<string> Emails(int count) => Enumerable.Range(0, count).Select(i => $"kisi{i}@d{i % 4}.com");

    private static async Task<List<SendJob>> JobsAsync(TestHost host, int campaignId)
    {
        await using var db = await host.DbAsync();
        return await db.SendJobs.AsNoTracking().Where(j => j.CampaignId == campaignId).ToListAsync();
    }

    [Theory]
    [InlineData(SendMode.Sequential)]
    [InlineData(SendMode.Bulk)]
    public async Task Sends_each_recipient_exactly_once(SendMode mode)
    {
        await using var host = await TestHost.CreateAsync();
        var profile = await host.AddProfileAsync();
        var listId = await host.AddListAsync(Emails(40));
        var campaign = await host.AddCampaignAsync(profile.Id, listId, c => c.Mode = mode);

        var result = await host.Get<SendEngine>().RunAsync(campaign.Id, null, CancellationToken.None);

        result.Status.Should().Be(CampaignStatus.Completed);
        host.Transports.Delivered.Should().HaveCount(40).And.OnlyHaveUniqueItems();
        (await JobsAsync(host, campaign.Id)).Should().OnlyContain(j => j.Status == SendJobStatus.Sent && j.MessageId != null);
        if (mode == SendMode.Sequential) host.Delay.Total.TotalSeconds.Should().BeGreaterThanOrEqualTo(40 * 3);
    }

    [Fact]
    public async Task Skips_suppressed_and_inactive_contacts()
    {
        await using var host = await TestHost.CreateAsync();
        var profile = await host.AddProfileAsync();
        var listId = await host.AddListAsync(["a@ornek.com", "b@ornek.com", "gecersiz", "c@ornek.com"]);
        await host.Get<Alpixa.Core.Abstractions.ISuppressionService>().SuppressAsync("b@ornek.com", SuppressionReason.Complaint, "test", null, CancellationToken.None);
        var campaign = await host.AddCampaignAsync(profile.Id, listId);

        await host.Get<SendEngine>().RunAsync(campaign.Id, null, CancellationToken.None);

        host.Transports.Delivered.Should().BeEquivalentTo(["a@ornek.com", "c@ornek.com"]);
    }

    [Fact]
    public async Task Retries_temporary_failures_then_succeeds()
    {
        await using var host = await TestHost.CreateAsync();
        var profile = await host.AddProfileAsync();
        var listId = await host.AddListAsync(["a@ornek.com"]);
        var campaign = await host.AddCampaignAsync(profile.Id, listId);
        host.Transports.Behaviour = (_, call) => call < 3 ? new SendResult(SendOutcome.TransientFailure, 451, "4.3.0") : SendResult.Ok();

        var result = await host.Get<SendEngine>().RunAsync(campaign.Id, null, CancellationToken.None);

        result.Status.Should().Be(CampaignStatus.Completed);
        (await JobsAsync(host, campaign.Id)).Single().Status.Should().Be(SendJobStatus.Sent);
    }

    [Fact]
    public async Task Permanent_recipient_failure_is_suppressed()
    {
        await using var host = await TestHost.CreateAsync();
        var profile = await host.AddProfileAsync();
        var listId = await host.AddListAsync(["yok@ornek.com", "var@ornek.com"]);
        var campaign = await host.AddCampaignAsync(profile.Id, listId);
        host.Transports.Behaviour = (m, _) => m.To.Mailboxes.First().Address.StartsWith("yok")
            ? new SendResult(SendOutcome.PermanentFailure, 550, "5.1.1", "User unknown")
            : SendResult.Ok();

        await host.Get<SendEngine>().RunAsync(campaign.Id, null, CancellationToken.None);

        await using var db = await host.DbAsync();
        (await db.Suppressions.SingleAsync()).EmailNormalized.Should().Be("yok@ornek.com");
        (await db.Contacts.SingleAsync(c => c.EmailNormalized == "yok@ornek.com")).Status.Should().Be(ContactStatus.HardBounced);
        (await db.DeliveryEvents.SingleAsync()).Kind.Should().Be(DeliveryEventKind.HardBounce);
    }

    [Fact]
    public async Task Circuit_breaker_pauses_campaign_on_high_error_rate()
    {
        await using var host = await TestHost.CreateAsync();
        var settings = host.Get<SettingsStore>();
        var sending = await settings.GetSendingAsync();
        sending.ErrorRateMinimumSamples = 10;
        sending.MaxRetries = 1;
        await settings.SaveSendingAsync(sending);

        var profile = await host.AddProfileAsync();
        var listId = await host.AddListAsync(Emails(100));
        var campaign = await host.AddCampaignAsync(profile.Id, listId, c => c.Mode = SendMode.Sequential);
        host.Transports.Behaviour = (_, _) => new SendResult(SendOutcome.PermanentFailure, 554, "5.7.1", "Rejected");

        var result = await host.Get<SendEngine>().RunAsync(campaign.Id, null, CancellationToken.None);

        result.Status.Should().Be(CampaignStatus.PausedBySafety);
        result.Reason.Should().Contain("Hata oranı");
        var jobs = await JobsAsync(host, campaign.Id);
        jobs.Count(j => j.Status == SendJobStatus.Failed).Should().BeLessThan(20);
        jobs.Should().Contain(j => j.Status == SendJobStatus.Pending);
    }

    [Fact]
    public async Task Authentication_failure_stops_immediately()
    {
        await using var host = await TestHost.CreateAsync();
        var profile = await host.AddProfileAsync();
        var listId = await host.AddListAsync(Emails(10));
        var campaign = await host.AddCampaignAsync(profile.Id, listId, c => c.Mode = SendMode.Sequential);
        host.Transports.Behaviour = (_, _) => new SendResult(SendOutcome.AuthenticationFailure, 535);

        var result = await host.Get<SendEngine>().RunAsync(campaign.Id, null, CancellationToken.None);

        result.Status.Should().Be(CampaignStatus.PausedBySafety);
        (await JobsAsync(host, campaign.Id)).Count(j => j.Status == SendJobStatus.Pending).Should().Be(9);
    }

    [Fact]
    public async Task Warmup_limit_moves_campaign_to_waiting_for_quota_and_resumes_next_day()
    {
        await using var host = await TestHost.CreateAsync();
        var profile = await host.AddProfileAsync(p => p.WarmupEnabled = true);
        var listId = await host.AddListAsync(Emails(80));
        var campaign = await host.AddCampaignAsync(profile.Id, listId);
        var engine = host.Get<SendEngine>();

        var first = await engine.RunAsync(campaign.Id, null, CancellationToken.None);

        first.Status.Should().Be(CampaignStatus.WaitingForQuota);
        first.NextRunUtc.Should().BeAfter(host.Clock.UtcNow);
        host.Transports.Delivered.Should().HaveCount(50);

        host.Clock.Advance(TimeSpan.FromDays(1));
        var second = await engine.RunAsync(campaign.Id, null, CancellationToken.None);

        second.Status.Should().Be(CampaignStatus.Completed);
        host.Transports.Delivered.Should().HaveCount(80).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Dry_run_sends_nothing_real_and_ignores_quota()
    {
        await using var host = await TestHost.CreateAsync();
        var profile = await host.AddProfileAsync(p => { p.WarmupEnabled = true; p.DailyLimit = 5; });
        var listId = await host.AddListAsync(Emails(30));
        var campaign = await host.AddCampaignAsync(profile.Id, listId, c => c.DryRun = true);
        var engine = host.Get<SendEngine>();

        (await engine.RunAsync(campaign.Id, null, CancellationToken.None)).Status.Should().Be(CampaignStatus.Completed);

        await using var db = await host.DbAsync();
        (await db.SenderProfiles.SingleAsync()).WarmupStartedUtc.Should().BeNull();
    }

    [Fact]
    public async Task Pause_and_resume_continues_where_it_left_off()
    {
        await using var host = await TestHost.CreateAsync();
        var profile = await host.AddProfileAsync();
        var listId = await host.AddListAsync(Emails(60));
        var campaign = await host.AddCampaignAsync(profile.Id, listId, c => c.Mode = SendMode.Sequential);
        var runner = host.Get<CampaignRunner>();
        var gate = new SemaphoreSlim(0);
        host.Transports.BeforeSend = _ =>
        {
            if (host.Transports.Delivered.Count == 20) gate.Release();
            Thread.Sleep(2);
        };

        await runner.StartAsync(campaign.Id);
        (await gate.WaitAsync(TimeSpan.FromSeconds(30))).Should().BeTrue();
        await runner.PauseAsync(campaign.Id);

        await using (var db = await host.DbAsync())
            (await db.Campaigns.SingleAsync()).Status.Should().Be(CampaignStatus.Paused);
        var afterPause = host.Transports.Delivered.Count;
        afterPause.Should().BeInRange(20, 59);

        host.Transports.BeforeSend = null;
        await host.Get<Alpixa.Sending.Campaigns.CampaignService>().ResumeAsync(campaign.Id, CancellationToken.None);
        await WaitUntilAsync(() => !runner.IsRunning(campaign.Id));

        var jobs = await JobsAsync(host, campaign.Id);
        var delivered = host.Transports.Delivered.ToList();
        delivered.Should().OnlyHaveUniqueItems();
        (delivered.Count + jobs.Count(j => j.Status == SendJobStatus.Uncertain)).Should().Be(60);
        jobs.Should().NotContain(j => j.Status == SendJobStatus.Pending || j.Status == SendJobStatus.Sending);
    }

    [Fact]
    public async Task Crash_recovery_never_sends_twice()
    {
        await using var host = await TestHost.CreateAsync();
        var profile = await host.AddProfileAsync();
        var listId = await host.AddListAsync(Emails(30));
        var campaign = await host.AddCampaignAsync(profile.Id, listId, c => c.Mode = SendMode.Sequential);

        await host.Get<Alpixa.Sending.Queue.CampaignQueueBuilder>().BuildAsync(campaign.Id, CancellationToken.None);
        await using (var db = await host.DbAsync())
        {
            var jobs = await db.SendJobs.OrderBy(j => j.Sequence).ToListAsync();
            foreach (var j in jobs.Take(10)) { j.Status = SendJobStatus.Sent; j.SentUtc = DateTime.UtcNow; }
            jobs[10].Status = SendJobStatus.Sending;
            (await db.Campaigns.SingleAsync()).Status = CampaignStatus.Running;
            await db.SaveChangesAsync();
        }

        var runner = host.Get<CampaignRunner>();
        await runner.RecoverAsync();
        await WaitUntilAsync(() => !runner.IsRunning(campaign.Id));

        host.Transports.Delivered.Should().HaveCount(19).And.OnlyHaveUniqueItems();
        var final = await JobsAsync(host, campaign.Id);
        final.Count(j => j.Status == SendJobStatus.Uncertain).Should().Be(1);
        final.Count(j => j.Status == SendJobStatus.Sent).Should().Be(29);
        await using (var db = await host.DbAsync())
            (await db.Campaigns.SingleAsync()).Status.Should().Be(CampaignStatus.Completed);
    }

    [Fact]
    public async Task Scheduler_starts_due_campaigns()
    {
        await using var host = await TestHost.CreateAsync();
        var profile = await host.AddProfileAsync();
        var listId = await host.AddListAsync(Emails(5));
        var campaign = await host.AddCampaignAsync(profile.Id, listId, c =>
        {
            c.Status = CampaignStatus.Scheduled;
            c.ScheduledUtc = DateTime.UtcNow.AddMinutes(-1);
        });

        await host.Get<BackgroundJobs>().RunSchedulerOnceAsync(CancellationToken.None);
        await WaitUntilAsync(() => !host.Get<CampaignRunner>().IsRunning(campaign.Id) && host.Transports.Delivered.Count == 5);

        host.Transports.Delivered.Should().HaveCount(5);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        await Task.Delay(50);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException();
            await Task.Delay(20);
        }
    }
}
