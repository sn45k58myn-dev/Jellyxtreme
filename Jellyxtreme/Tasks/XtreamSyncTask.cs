using Jellyxtreme.Cache;
using Jellyxtreme.Services;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyxtreme.Tasks;

public sealed class XtreamSyncTask : IScheduledTask
{
    private readonly XtreamCacheRefreshService _cacheRefreshService;
    private readonly ILogger<XtreamSyncTask> _logger;

    public XtreamSyncTask(XtreamCacheRefreshService cacheRefreshService, ILogger<XtreamSyncTask> logger)
    {
        _cacheRefreshService = cacheRefreshService;
        _logger = logger;
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
                Type = TaskTriggerInfoType.IntervalTrigger,
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

        var document = await _cacheRefreshService.RefreshAsync(config, progress, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "JellyXtreme cache refresh complete. Live: {LiveCount}; VOD: {VodCount}; Series: {SeriesCount}; Episodes: {EpisodeCount}.",
            document.LiveChannels.Count,
            document.VodItems.Count,
            document.SeriesItems.Count,
            document.SeriesItems.Sum(series => series.Seasons.Sum(season => season.Episodes.Count)));
    }
}
