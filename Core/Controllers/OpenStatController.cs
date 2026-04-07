using Core.Middlewares;
using Core.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Shared;
using Shared.Attributes;
using Shared.Models.AppConf;
using Shared.PlaywrightCore;
using Shared.Services;
using Shared.Services.Hybrid;
using Shared.Services.Native;
using Shared.Services.Pools;
using Shared.Services.Pools.Json;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;

namespace Core.Controllers
{
    [Authorization]
    public class OpenStatController : BaseController
    {
        readonly IWebHostEnvironment _env;

        public OpenStatController(IWebHostEnvironment env)
        {
            _env = env;
        }

        public OpenStatConf openstat => CoreInit.conf.openstat;

        [HttpGet]
        [Route("/stats")]
        public ActionResult StatsPage()
        {
            if (!openstat.enable)
                return NotFound();

            string path = Path.Combine(_env.WebRootPath, "stats", "index.html");
            if (!System.IO.File.Exists(path))
                return NotFound();

            return PhysicalFile(path, "text/html; charset=utf-8");
        }

        #region GC
        [HttpGet]
        [Route("/stats/gc")]
        public ActionResult GcMemory()
        {
            var proc = Process.GetCurrentProcess();
            var gc = GC.GetGCMemoryInfo();

            return Json(new
            {
                ManagedReserved = $"{gc.HeapSizeBytes / 1024 / 1024} MB",      // вся память, которую GC закоммитил под managed heap (SOH + LOH + POH)
                ManagedUsed = $"{GC.GetTotalMemory(false) / 1024 / 1024} MB",  // объём живых объектов
                ManagedFragmented = $"{gc.FragmentedBytes / 1024 / 1024} MB",  // дыры внутри managed heap (фрагментация)

                WorkingSet = $"{proc.WorkingSet64 / 1024 / 1024} MB",
                PrivateMemory = $"{proc.PrivateMemorySize64 / 1024 / 1024} MB"
            });
        }
        #endregion

        #region browser/context
        [HttpGet]
        [Route("/stats/browser/context")]
        public ActionResult BrowserContext()
        {
            if (!openstat.enable)
                return NotFound();

            return Json(new
            {
                Chromium = new
                {
                    open = Chromium.ContextsCount,
                    req_keepopen = Chromium.stats_keepopen,
                    req_newcontext = Chromium.stats_newcontext,
                    ping = new
                    {
                        Chromium.stats_ping.status,
                        Chromium.stats_ping.time,
                        Chromium.stats_ping.ex
                    }
                },
                Firefox = new
                {
                    open = Firefox.ContextsCount,
                    req_keepopen = Firefox.stats_keepopen,
                    req_newcontext = Firefox.stats_newcontext
                }
            });
        }
        #endregion

        #region request
        [HttpGet]
        [Route("/stats/request")]
        public ActionResult Requests()
        {
            if (!openstat.enable)
                return NotFound();

            var now = DateTime.UtcNow;

            (long req_min, long req_hour) = RequestInfoStats.GetCounters("base", now);
            (long map_req_min, long map_req_hour) = RequestInfoStats.GetCounters("request", now);
            (long proxy_req_min, long proxy_req_hour) = RequestInfoStats.GetCounters("proxy", now);
            (long img_req_min, long img_req_hour) = RequestInfoStats.GetCounters("img", now);
            (long nws_req_min, long nws_req_hour) = RequestInfoStats.GetCounters("nws", now);
            (long bot_req_min, long bot_req_hour) = RequestInfoStats.GetCounters("bot", now);

            var responseStats = ResponseStatisticsTracker.GetResponseTimeStatsLastMinute();
            var nwsCounter = NativeWebSocket.GetStatsLastMinute();

            int http_active = LimitHttpRequests.ActiveHttpRequests;
            if (ResponseStatisticsTracker.ActiveHttpRequests > http_active)
                http_active = ResponseStatisticsTracker.ActiveHttpRequests;

            return Json(new
            {
                req_min,
                req_hour,
                map = new
                {
                    req_min = map_req_min,
                    req_hour = map_req_hour
                },
                img = new
                {
                    req_min = img_req_min,
                    req_hour = img_req_hour
                },
                proxy = new
                {
                    req_min = proxy_req_min,
                    req_hour = proxy_req_hour
                },
                nws = new
                {
                    online = NativeWebSocket.CountConnection,
                    receive_min = nwsCounter.receive,
                    send_min = nwsCounter.send,
                    req_min = nws_req_min,
                    req_hour = nws_req_hour
                },
                bot = new
                {
                    req_min = bot_req_min,
                    req_hour = bot_req_hour
                },
                tcpConnections = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Length,
                http_active,
                low_response = new
                {
                    avg_ms = (int)responseStats.average,
                    count = responseStats.averages.Count,
                    top = responseStats.averages
                        .GroupBy(x => x.path)
                        .Select(g => new
                        {
                            path = g.Key,
                            durationMs = (int)g.Sum(x => x.durationMs)
                        })
                        .OrderByDescending(x => x.durationMs)
                        .Take(20)
                }
            });

        }
        #endregion

        #region TempDb
        [HttpGet]
        [Route("/stats/tempdb")]
        public ActionResult TempDb()
        {
            if (!openstat.enable)
                return NotFound();

            return Json(new
            {
                memoryCache = memoryCache.GetCurrentStatistics().CurrentEntryCount,
                HybridFileCache = HybridFileCache.Stat_ContTempDb,
                SemaphorManager = SemaphorManager.Stat_ContSemaphoreLocks,
                Staticache = Staticache.cacheFiles.Count,
                ProxyLink = ProxyLink.Stat_ContLinks,
                ProxyAPI = ProxyAPI.Stat_ContCacheFiles,
                ProxyImg = ProxyImg.Stat_ContCacheFiles,
                rch = new
                {
                    clients = RchClient.clients.Count,
                    Ids = RchClient.rchIds.Count
                },
                pool = new
                {
                    msm = new
                    {
                        PoolInvk.msm.SmallPoolInUseSize,
                        PoolInvk.msm.LargePoolInUseSize,
                        PoolInvk.msm.SmallBlocksFree,
                        PoolInvk.msm.SmallPoolFreeSize,
                        PoolInvk.msm.LargeBuffersFree,
                        PoolInvk.msm.LargePoolFreeSize
                    },
                    StringBuilder = new
                    {
                        small = StringBuilderPool.FreeSmall,
                        large = StringBuilderPool.FreeLarge,
                        dispose = StringBuilderPool.DisposeCount
                    },
                    BufferPool = new
                    {
                        BufferPool.Free,
                        dispose = BufferPool.DisposeCount
                    },
                    BufferByte = new
                    {
                        small = BufferBytePool.FreeSmall,
                        medium = BufferBytePool.FreeMedium,
                        large = BufferBytePool.FreeLarge,
                        dispose = BufferBytePool.DisposeCount
                    },
                    BufferChar = new
                    {
                        small = BufferCharPool.FreeSmall,
                        medium = BufferCharPool.FreeMedium,
                        large = BufferCharPool.FreeLarge,
                        dispose = BufferCharPool.DisposeCount
                    },
                    BufferWriterByte = new
                    {
                        small = BufferWriterPool<byte>.Free,
                        large = BufferWriterPool<byte>.FreeLarge,
                        dispose = BufferWriterPool<byte>.DisposeCount
                    },
                    NativeBuffer = new
                    {
                        created = NativeBufferStats.Created,
                        dispose = NativeBufferStats.Disposed
                    },
                    Json = new
                    {
                        medium = NewtonsoftCharArrayPool.FreeMedium,
                        large = NewtonsoftCharArrayPool.FreeLarge,
                        dispose = NewtonsoftCharArrayPool.DisposeCount
                    }
                }
            });
        }
        #endregion

        #region thread/task diagnostics
        [HttpGet]
        [Route("/stats/threadpool")]
        public ActionResult ThreadPoolDiagnostics()
        {
            if (!openstat.enable)
                return NotFound();

            var proc = Process.GetCurrentProcess();

            ThreadPool.GetAvailableThreads(out int workerAvailable, out int ioAvailable);
            ThreadPool.GetMaxThreads(out int workerMax, out int ioMax);
            ThreadPool.GetMinThreads(out int workerMin, out int ioMin);

            int workerActive = workerMax - workerAvailable;
            int ioActive = ioMax - ioAvailable;

            long queueLength = ThreadPool.PendingWorkItemCount;

            bool starvationSuspected = queueLength > 0 && workerAvailable == 0;

            return Json(new
            {
                thread_pool = new
                {
                    queue_length = ThreadPool.PendingWorkItemCount,   // Количество задач (work items), которые ожидают выполнения в очереди ThreadPool
                    thread_count = ThreadPool.ThreadCount,            // Сколько потоков ThreadPool сейчас существует и может выполнять задачи
                    completed_work_item_count = ThreadPool.CompletedWorkItemCount,
                    worker = new
                    {
                        min = workerMin,
                        max = workerMax,
                        available = workerAvailable,
                        active = workerActive
                    },
                    io = new
                    {
                        min = ioMin,
                        max = ioMax,
                        available = ioAvailable,
                        active = ioActive
                    }
                },
                uptime = Math.Round((DateTime.UtcNow - proc.StartTime.ToUniversalTime()).TotalSeconds, 2),
                task_diagnostics = new
                {
                    starvation_suspected = starvationSuspected,
                    starvation_hint = starvationSuspected
                        ? "ThreadPool queue has pending items while no worker threads are available."
                        : "No immediate ThreadPool starvation signs by queue/availability snapshot."
                }
            });
        }
        #endregion
    }
}
