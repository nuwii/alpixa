using Alpixa.Sending.Campaigns;
using Alpixa.Sending.Engine;
using Alpixa.Sending.Queue;
using Alpixa.Sending.Throttling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Alpixa.Sending;

public static class DependencyInjection
{
    public static IServiceCollection AddAlpixaSending(this IServiceCollection services)
    {
        services.TryAddSingleton<IDelayProvider, TaskDelayProvider>();
        services.AddSingleton<ThrottleRegistry>();
        services.AddSingleton<QuotaService>();
        services.AddSingleton<CampaignQueueBuilder>();
        services.AddSingleton<SendEngine>();
        services.AddSingleton<CampaignRunner>();
        services.AddSingleton<CampaignService>();
        services.AddSingleton<BackgroundJobs>();
        return services;
    }
}
