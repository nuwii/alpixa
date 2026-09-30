using Alpixa.Core.Abstractions;
using Alpixa.Core.Models;
using Alpixa.Infrastructure.Mail;
using Alpixa.Sending.Campaigns;
using Alpixa.Sending.Engine;
using Alpixa.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;

namespace Alpixa.Tests.Integration;

[Trait("Category", "Integration")]
public class SmtpEndToEndTests
{
    private static Task<TestHost> CreateHostWithRealSmtpAsync()
        => TestHost.CreateAsync(services => services.AddSingleton<IMailTransportFactory, MailTransportFactory>());

    [Fact]
    public async Task Campaign_is_delivered_through_real_smtp_protocol()
    {
        await using var smtp = new FakeSmtpServer();
        smtp.UnknownRecipients.Add("yok@alici.com");
        smtp.TemporaryFailuresRemaining["yavas@alici.com"] = 1;

        await using var host = await CreateHostWithRealSmtpAsync();
        var profile = await host.AddProfileAsync(p => p.SmtpPort = smtp.Port);
        var recipients = Enumerable.Range(0, 25).Select(i => $"kisi{i}@alici{i % 3}.com").Concat(["yok@alici.com", "yavas@alici.com"]).ToList();
        var listId = await host.AddListAsync(recipients);
        var campaign = await host.AddCampaignAsync(profile.Id, listId, c => { c.Mode = SendMode.Bulk; c.Parallelism = 3; });

        var result = await host.Get<SendEngine>().RunAsync(campaign.Id, null, CancellationToken.None);

        result.Status.Should().Be(CampaignStatus.Completed);
        var delivered = smtp.Messages.ToList();
        delivered.Should().HaveCount(26);
        delivered.Select(m => m.To.Mailboxes.Single().Address).Should().OnlyHaveUniqueItems();
        delivered.Should().OnlyContain(m => m.Cc.Count == 0 && m.Bcc.Count == 0);
        delivered.Should().OnlyContain(m => m.Headers.Contains("List-Unsubscribe") && m.MessageId!.EndsWith("@ornek.com"));
        delivered.Should().OnlyContain(m => m.Body is MultipartAlternative);

        await using var db = await host.DbAsync();
        (await db.Suppressions.SingleAsync()).EmailNormalized.Should().Be("yok@alici.com");
        (await db.SendJobs.SingleAsync(j => j.Email == "yok@alici.com")).SmtpCode.Should().Be(550);
    }

    [Fact]
    public async Task Connection_test_and_test_send_work_against_local_server()
    {
        await using var smtp = new FakeSmtpServer();
        await using var host = await CreateHostWithRealSmtpAsync();
        var profile = await host.AddProfileAsync(p => p.SmtpPort = smtp.Port);

        var (ok, error) = await host.Get<IConnectionTester>().TestSmtpAsync(profile, CancellationToken.None);
        ok.Should().BeTrue(error?.Message);

        var listId = await host.AddListAsync(["a@alici.com"]);
        var campaign = await host.AddCampaignAsync(profile.Id, listId);
        var results = await host.Get<CampaignService>().SendTestAsync(campaign, ["ben@gmail.com", "ben@outlook.com"], CancellationToken.None);

        results.Should().OnlyContain(r => r.Success);
        smtp.Messages.Select(m => m.Subject).Should().OnlyContain(s => s.StartsWith("[TEST] "));
    }

    [Fact]
    public async Task Connection_test_reports_friendly_error_when_server_is_down()
    {
        await using var host = await CreateHostWithRealSmtpAsync();
        var profile = await host.AddProfileAsync(p => p.SmtpPort = 1);

        var (ok, error) = await host.Get<IConnectionTester>().TestSmtpAsync(profile, CancellationToken.None);

        ok.Should().BeFalse();
        error!.Message.Should().Be("Sunucuya bağlanılamadı.");
        error.WhatToDo.Should().Contain("port");
    }

    [Fact]
    public async Task Preflight_blocks_bad_content_and_allows_good_content()
    {
        await using var host = await CreateHostWithRealSmtpAsync();
        var profile = await host.AddProfileAsync();
        var listId = await host.AddListAsync(["a@alici.com"]);
        var service = host.Get<CampaignService>();

        var bad = await host.AddCampaignAsync(profile.Id, listId, c => c.HtmlBody = "<a href=\"https://bit.ly/x\">tikla</a>");
        (await service.PreflightAsync(bad, CancellationToken.None)).CanStart(false).Should().BeFalse();

        var good = await host.AddCampaignAsync(profile.Id, listId);
        var preflight = await service.PreflightAsync(good, CancellationToken.None);
        preflight.Recipients.Should().Be(1);
        preflight.CanStart(false).Should().BeTrue();
    }
}
