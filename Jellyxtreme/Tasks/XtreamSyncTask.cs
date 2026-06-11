using Jellyxtreme.Cache;
using Jellyxtreme.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyxtreme.Tasks;

public sealed class XtreamSyncTask : IScheduledTask
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<XtreamSyncTask> _logger;

    public XtreamSyncTask(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    {
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<XtreamSyncTask>();
    }

    public string Name => "JellyXtreme: Refresh Xtream Cache";
    public string Key => "JellyxtremeRefreshXtreamCache";
    public string Description => "Refreshes cached Xtream Live TV, VOD, and Series metadata for selected categories.";
    public string Category => "JellyXtreme";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        var hours = Math.Max(1, Plugin.Instance?.Configuration.SyncIntervalHours ?? 12);
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfo.TriggerInterval,
                IntervalTicks = TimeSpan.FromHours(hours).Ticks
            }
        ];
    }

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null)
        {
            _logger.LogWarning("JellyXtreme plugin configuration is unavailable. Skipping cache refresh.");
            return;
        }

        var service = new XtreamCacheRefreshService(
            _httpClientFactory,
            new XtreamCacheStore(_loggerFactory.CreateLogger<XtreamCacheStore>()),
            _loggerFactory);

        await service.RefreshAsync(config, progress, cancellationToken).ConfigureAwait(false);
    }
}
