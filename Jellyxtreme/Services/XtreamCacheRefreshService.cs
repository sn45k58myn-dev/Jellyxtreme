using Jellyxtreme.Api;
using Jellyxtreme.Cache;
using Jellyxtreme.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyxtreme.Services;

public sealed class XtreamCacheRefreshService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly XtreamCacheStore _cacheStore;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<XtreamCacheRefreshService> _logger;

    public XtreamCacheRefreshService(
        IHttpClientFactory httpClientFactory,
        XtreamCacheStore cacheStore,
        ILoggerFactory loggerFactory)
    {
        _httpClientFactory = httpClientFactory;
        _cacheStore = cacheStore;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<XtreamCacheRefreshService>();
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

        var client = CreateClient(config);
        var document = new XtreamCacheDocument { RefreshedAt = DateTimeOffset.UtcNow };

        var liveCategories = await client.GetLiveCategoriesAsync(cancellationToken).ConfigureAwait(false);
        var vodCategories = await client.GetVodCategoriesAsync(cancellationToken).ConfigureAwait(false);
        var seriesCategories = await client.GetSeriesCategoriesAsync(cancellationToken).ConfigureAwait(false);

        document.LiveCategories = ToCachedCategories(liveCategories, "live");
        document.VodCategories = ToCachedCategories(vodCategories, "vod");
        document.SeriesCategories = ToCachedCategories(seriesCategories, "series");
        progress?.Report(15);

        if (config.EnableLiveTv && config.SelectedLiveCategoryIds.Length > 0)
        {
            document.LiveChannels = await RefreshLiveAsync(client, config, document.LiveCategories, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _logger.LogInformation("JellyXtreme Live TV cache skipped because it is disabled or no Live TV categories are selected.");
        }

        progress?.Report(45);

        if (config.EnableVod && config.SelectedVodCategoryIds.Length > 0)
        {
            document.VodMovies = await RefreshVodAsync(client, config, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _logger.LogInformation("JellyXtreme VOD cache skipped because it is disabled or no VOD categories are selected.");
        }

        progress?.Report(70);

        if (config.EnableSeries && config.SelectedSeriesCategoryIds.Length > 0)
        {
            document.Series = await RefreshSeriesAsync(client, config, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            _logger.LogInformation("JellyXtreme Series cache skipped because it is disabled or no Series categories are selected.");
        }

        progress?.Report(95);
        await _cacheStore.SaveAsync(document, cancellationToken).ConfigureAwait(false);
        progress?.Report(100);
        return document;
    }

    public async Task<XtreamCategorySnapshot> GetCategoriesAsync(PluginConfiguration config, CancellationToken cancellationToken)
    {
        if (!HasCredentials(config))
        {
            return new XtreamCategorySnapshot([], [], []);
        }

        var client = CreateClient(config);
        var live = await client.GetLiveCategoriesAsync(cancellationToken).ConfigureAwait(false);
        var vod = await client.GetVodCategoriesAsync(cancellationToken).ConfigureAwait(false);
        var series = await client.GetSeriesCategoriesAsync(cancellationToken).ConfigureAwait(false);

        return new XtreamCategorySnapshot(live, vod, series);
    }

    private XtreamApiClient CreateClient(PluginConfiguration config)
        => new(
            _httpClientFactory,
            _loggerFactory.CreateLogger<XtreamApiClient>(),
            config.ServerUrl,
            config.Username,
            config.Password,
            TimeSpan.FromMinutes(Math.Max(1, config.CacheMinutes)));

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
        PluginConfiguration config,
        List<CachedCategory> categories,
        CancellationToken cancellationToken)
    {
        var selected = config.SelectedLiveCategoryIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var categoryNames = categories.ToDictionary(category => category.CategoryId, category => category.Name, StringComparer.OrdinalIgnoreCase);
        var streams = await client.GetLiveStreamsAsync(cancellationToken).ConfigureAwait(false);

        return streams
            .Where(stream => !string.IsNullOrWhiteSpace(stream.CategoryId) && selected.Contains(stream.CategoryId))
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

    private static async Task<List<CachedVodMovie>> RefreshVodAsync(
        XtreamApiClient client,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        var selected = config.SelectedVodCategoryIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var streams = await client.GetVodStreamsAsync(cancellationToken).ConfigureAwait(false);

        return streams
            .Where(stream => !string.IsNullOrWhiteSpace(stream.CategoryId) && selected.Contains(stream.CategoryId))
            .Where(stream => stream.StreamId > 0 && !string.IsNullOrWhiteSpace(stream.Name))
            .Select(stream => new CachedVodMovie
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

    private async Task<List<CachedSeries>> RefreshSeriesAsync(
        XtreamApiClient client,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        var selected = config.SelectedSeriesCategoryIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seriesList = await client.GetSeriesAsync(cancellationToken).ConfigureAwait(false);
        var selectedSeries = seriesList
            .Where(series => !string.IsNullOrWhiteSpace(series.CategoryId) && selected.Contains(series.CategoryId))
            .Where(series => series.SeriesId > 0 && !string.IsNullOrWhiteSpace(series.Name))
            .ToList();

        var cached = new List<CachedSeries>(selectedSeries.Count);
        foreach (var series in selectedSeries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var info = await client.GetSeriesInfoAsync(series.SeriesId, cancellationToken).ConfigureAwait(false);
                cached.Add(new CachedSeries
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
                            return new CachedEpisode
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
