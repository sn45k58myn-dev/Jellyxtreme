using Jellyxtreme.Api;
using Jellyxtreme.Cache;
using Jellyxtreme.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyxtreme.Services;

public sealed class XtreamCacheRefreshService
{
    private readonly XtreamApiClient _apiClient;
    private readonly XtreamCacheService _cacheService;
    private readonly XmlTvCacheService _xmlTvCacheService;
    private readonly ILogger<XtreamCacheRefreshService> _logger;

    public XtreamCacheRefreshService(
        XtreamApiClient apiClient,
        XtreamCacheService cacheService,
        XmlTvCacheService xmlTvCacheService,
        ILogger<XtreamCacheRefreshService> logger)
    {
        _apiClient = apiClient;
        _cacheService = cacheService;
        _xmlTvCacheService = xmlTvCacheService;
        _logger = logger;
    }

    public async Task<XtreamCacheDocument> RefreshAsync(
        PluginConfiguration config,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(0);

        if (!HasCredentials(config))
        {
            _logger.LogWarning("JellyXtreme is not configured. Skipping cache refresh.");
            return new XtreamCacheDocument();
        }

        var settings = XtreamConnectionSettings.FromConfig(config);
        var document = new XtreamCacheDocument { RefreshedAt = DateTimeOffset.UtcNow };

        var liveCategories = await _apiClient.GetLiveCategoriesAsync(settings, cancellationToken).ConfigureAwait(false);
        var vodCategories = await _apiClient.GetVodCategoriesAsync(settings, cancellationToken).ConfigureAwait(false);
        var seriesCategories = await _apiClient.GetSeriesCategoriesAsync(settings, cancellationToken).ConfigureAwait(false);

        document.LiveCategories = ToCachedCategories(liveCategories, "live");
        document.VodCategories = ToCachedCategories(vodCategories, "vod");
        document.SeriesCategories = ToCachedCategories(seriesCategories, "series");
        progress?.Report(15);

        var skippedSections = new List<string>();

        if (XtreamSelectionFilter.ShouldCacheSection(config.EnableLiveTv, config.SelectedLiveCategoryIds))
        {
            document.LiveChannels = await RefreshLiveAsync(_apiClient, settings, config, document.LiveCategories, cancellationToken).ConfigureAwait(false);
            document.XmlTv = await _xmlTvCacheService.DownloadAndCacheAsync(settings, document.LiveChannels, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            skippedSections.Add("Live TV");
            _logger.LogInformation("JellyXtreme Live TV cache skipped because it is disabled or no Live TV categories are selected.");
        }

        progress?.Report(45);

        if (XtreamSelectionFilter.ShouldCacheSection(config.EnableVod, config.SelectedVodCategoryIds))
        {
            document.VodItems = await RefreshVodAsync(_apiClient, settings, config, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            skippedSections.Add("VOD");
            _logger.LogInformation("JellyXtreme VOD cache skipped because it is disabled or no VOD categories are selected.");
        }

        progress?.Report(70);

        if (XtreamSelectionFilter.ShouldCacheSection(config.EnableSeries, config.SelectedSeriesCategoryIds))
        {
            document.SeriesItems = await RefreshSeriesAsync(_apiClient, settings, config, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            skippedSections.Add("Series");
            _logger.LogInformation("JellyXtreme Series cache skipped because it is disabled or no Series categories are selected.");
        }

        progress?.Report(95);
        await _cacheService.SaveAsync(document, cancellationToken).ConfigureAwait(false);
        config.LastSuccessfulSyncUtc = document.RefreshedAt;
        Plugin.Instance?.SaveConfiguration();
        _logger.LogInformation(
            "JellyXtreme refresh imported {LiveCount} live channels, {VodCount} VOD items, {SeriesCount} series, and {EpisodeCount} episodes. Skipped sections: {SkippedSections}.",
            document.LiveChannels.Count,
            document.VodItems.Count,
            document.SeriesItems.Count,
            document.SeriesItems.Sum(series => series.Seasons.Sum(season => season.Episodes.Count)),
            skippedSections.Count == 0 ? "none" : string.Join(", ", skippedSections));
        progress?.Report(100);
        return document;
    }

    public async Task<XtreamCategorySnapshot> GetCategoriesAsync(PluginConfiguration config, CancellationToken cancellationToken)
    {
        if (!HasCredentials(config))
        {
            return new XtreamCategorySnapshot([], [], []);
        }

        var settings = XtreamConnectionSettings.FromConfig(config);
        var live = await _apiClient.GetLiveCategoriesAsync(settings, cancellationToken).ConfigureAwait(false);
        var vod = await _apiClient.GetVodCategoriesAsync(settings, cancellationToken).ConfigureAwait(false);
        var series = await _apiClient.GetSeriesCategoriesAsync(settings, cancellationToken).ConfigureAwait(false);

        return new XtreamCategorySnapshot(live, vod, series);
    }

    private static bool HasCredentials(PluginConfiguration config)
        => XtreamApiClient.TryNormalizeServerUrl(config.ServerUrl, out _)
            && !string.IsNullOrWhiteSpace(config.Username)
            && !string.IsNullOrWhiteSpace(config.Password);

    private static List<CachedCategory> ToCachedCategories(IEnumerable<XtreamCategory> categories, string kind)
        => categories
            .Where(category => !string.IsNullOrWhiteSpace(category.CategoryId))
            .Select(category => new CachedCategory
            {
                CategoryId = category.CategoryId,
                Name = category.CategoryName,
                Kind = kind
            })
            .ToList();

    private static async Task<List<CachedLiveChannel>> RefreshLiveAsync(
        XtreamApiClient client,
        XtreamConnectionSettings settings,
        PluginConfiguration config,
        List<CachedCategory> categories,
        CancellationToken cancellationToken)
    {
        var categoryNames = categories.ToDictionary(category => category.CategoryId, category => category.Name, StringComparer.OrdinalIgnoreCase);
        var streams = await client.GetLiveStreamsAsync(settings, cancellationToken).ConfigureAwait(false);

        return streams
            .Where(stream => XtreamSelectionFilter.IsCategorySelected(stream.CategoryId, config.SelectedLiveCategoryIds))
            .Where(stream => stream.StreamId > 0 && !string.IsNullOrWhiteSpace(stream.Name))
            .Select(stream => new CachedLiveChannel
            {
                Name = stream.Name!,
                StreamId = stream.StreamId,
                Logo = stream.StreamIcon,
                EpgChannelId = stream.EpgChannelId,
                CategoryId = stream.CategoryId!,
                GroupName = categoryNames.GetValueOrDefault(stream.CategoryId!, stream.CategoryId!),
                StreamExtension = "ts"
            })
            .ToList();
    }

    private static async Task<List<CachedVodItem>> RefreshVodAsync(
        XtreamApiClient client,
        XtreamConnectionSettings settings,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        var streams = await client.GetVodStreamsAsync(settings, cancellationToken).ConfigureAwait(false);

        return streams
            .Where(stream => XtreamSelectionFilter.IsCategorySelected(stream.CategoryId, config.SelectedVodCategoryIds))
            .Where(stream => stream.StreamId > 0 && !string.IsNullOrWhiteSpace(stream.Name))
            .Select(stream => new CachedVodItem
            {
                Name = stream.Name!,
                StreamId = stream.StreamId,
                CategoryId = stream.CategoryId!,
                Poster = stream.StreamIcon,
                Rating = stream.Rating,
                ContainerExtension = string.IsNullOrWhiteSpace(stream.ContainerExtension) ? "mp4" : stream.ContainerExtension,
                Added = stream.Added
            })
            .ToList();
    }

    private async Task<List<CachedSeriesItem>> RefreshSeriesAsync(
        XtreamApiClient client,
        XtreamConnectionSettings settings,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        var seriesList = await client.GetSeriesAsync(settings, cancellationToken).ConfigureAwait(false);
        var validSeries = seriesList
            .Where(series => series.SeriesId > 0 && !string.IsNullOrWhiteSpace(series.Name))
            .ToList();

        var cached = new List<CachedSeriesItem>();
        foreach (var series in validSeries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var info = await client.GetSeriesInfoAsync(settings, series.SeriesId, cancellationToken).ConfigureAwait(false);
                if (!XtreamSelectionFilter.IsCategorySelected(series.CategoryId, config.SelectedSeriesCategoryIds))
                {
                    continue;
                }

                cached.Add(new CachedSeriesItem
                {
                    Name = series.Name!,
                    SeriesId = series.SeriesId,
                    CategoryId = series.CategoryId!,
                    Poster = series.Cover,
                    Plot = info?.Info?.Plot ?? series.Plot,
                    Rating = series.Rating,
                    Seasons = ToCachedSeasons(info)
                });
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                _logger.LogWarning(ex, "Skipping Xtream series {SeriesId} after metadata fetch failure.", series.SeriesId);
            }
        }

        return cached;
    }

    private static List<CachedSeason> ToCachedSeasons(XtreamSeriesInfoResponse? info)
    {
        if (info?.Episodes is null)
        {
            return [];
        }

        return info.Episodes
            .Select(season =>
            {
                _ = int.TryParse(season.Key, out var seasonNumber);
                return new CachedSeason
                {
                    SeasonNumber = seasonNumber,
                    Episodes = season.Value
                        .Where(episode => int.TryParse(episode.Id, out _))
                        .Select(episode =>
                        {
                            _ = int.TryParse(episode.Id, out var streamId);
                            return new CachedEpisodeItem
                            {
                                Title = string.IsNullOrWhiteSpace(episode.Title) ? $"Episode {episode.EpisodeNumber}" : episode.Title!,
                                StreamId = streamId,
                                EpisodeNumber = episode.EpisodeNumber,
                                ContainerExtension = string.IsNullOrWhiteSpace(episode.ContainerExtension) ? "mp4" : episode.ContainerExtension,
                                Poster = episode.Info?.MovieImage,
                                Plot = episode.Info?.Plot,
                                ReleaseDate = episode.Info?.ReleaseDate
                            };
                        })
                        .ToList()
                };
            })
            .ToList();
    }
}

public sealed record XtreamCategorySnapshot(
    IReadOnlyList<XtreamCategory> Live,
    IReadOnlyList<XtreamCategory> Vod,
    IReadOnlyList<XtreamCategory> Series);
