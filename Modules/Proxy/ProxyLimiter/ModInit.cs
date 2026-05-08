using Shared.Models.Events;
using Shared.Models.Module;
using Shared.Models.Module.Interfaces;
using Shared.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

namespace ProxyLimiter;

public class ModInit : IModuleLoaded
{
    static ConcurrentBag<PartitionedRateLimiter<string>> rates;

    public void Loaded(InitspaceModel baseconf)
    {
        updateConf();
        EventListener.UpdateInitFile += updateConf;
        EventListener.ProxyApiOverride += ProxyApiOverride;
    }

    public void Dispose()
    {
        EventListener.UpdateInitFile -= updateConf;
        EventListener.ProxyApiOverride -= ProxyApiOverride;
    }

    void updateConf()
    {
        var limiters = ModuleInvoke.Init("ProxyLimiter", new List<ModuleConf>());
        if (limiters == null || limiters.Count == 0)
        {
            rates = null;
            return;
        }

        rates = new ConcurrentBag<PartitionedRateLimiter<string>>();

        foreach (var limit in limiters)
        {
            var rate = PartitionedRateLimiter.Create<string, string>(ip =>
                RateLimitPartition.GetSlidingWindowLimiter(ip,
                _ => new SlidingWindowRateLimiterOptions
                {
                    PermitLimit = limit.PermitLimit,
                    Window = TimeSpan.FromSeconds(limit.Window),
                    SegmentsPerWindow = limit.SegmentsPerWindow,
                    QueueLimit = limit.QueueLimit
                }));

            rates.Add(rate);
        }
    }

    async Task<bool> ProxyApiOverride(EventProxyApiOverride e)
    {
        if (rates == null)
            return true;

        string ip = e.requestInfo.IP;

        foreach (var rate in rates)
        {
            using var lease = await rate.AcquireAsync(ip, 1);
            if (!lease.IsAcquired)
                return false;
        }

        return true;
    }
}
