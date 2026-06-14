using Jellyxtreme.Cache;
using Jellyxtreme.Configuration;
using Jellyxtreme.Services;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.MediaInfo;

namespace Jellyxtreme.Providers;

public class SeriesProvider
{
    private readonly XtreamCacheService _cacheService;
    private readonly StreamResolverService _streamResolver;
    private readonly Func<PluginConfiguration> _configProvider;

    public SeriesProvider(XtreamCacheService cacheService, StreamResolverService streamResolver)
        : this(cacheService, streamResolver, () => Plugin.Instance?.Configuration ?? new PluginConfiguration())
    {
    }

    public SeriesProvider(
        XtreamCacheService cacheService,
        StreamResolverService streamResolver,
        Func<PluginConfiguration> configProvider)
    {
        _cacheService = cacheService;
        _streamResolver = streamResolver;
        _configProvider = configProvider;
    }

    public async Task<IReadOnlyList<SeriesItemInfo>> GetSeriesInfosAsync(CancellationToken cancellationToken)
    {
        var cache = await _cacheService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var categoryNames = cache.SeriesCategories.ToDictionary(
            category => XtreamCacheIdentity.BuildItemKey(category.ProviderId, category.CategoryId),
            category => category.Name,
            StringComparer.OrdinalIgnoreCase);

        return cache.SeriesItems
            .Select(series => ToInfo(
                series,
                categoryNames.GetValueOrDefault(XtreamCacheIdentity.BuildItemKey(series.ProviderId, series.CategoryId))))
            .ToList();
    }

    public async Task<SeriesItemInfo?> GetSeriesInfoAsync(int seriesId, CancellationToken cancellationToken)
        => await GetSeriesInfoAsync(seriesId, null, cancellationToken).ConfigureAwait(false);

    public async Task<SeriesItemInfo?> GetSeriesInfoAsync(int seriesId, string? providerId, CancellationToken cancellationToken)
    {
        var cache = await _cacheService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var normalizedProviderId = NormalizeProviderId(providerId);
        var series = cache.SeriesItems.FirstOrDefault(item =>
            item.SeriesId == seriesId
            && (normalizedProviderId is null || string.Equals(item.ProviderId, normalizedProviderId, StringComparison.OrdinalIgnoreCase)));
        if (series is null)
        {
            return null;
        }

        var categoryName = cache.SeriesCategories.FirstOrDefault(category =>
                string.Equals(category.ProviderId, series.ProviderId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(category.CategoryId, series.CategoryId, StringComparison.OrdinalIgnoreCase))
            ?.Name;
        return ToInfo(series, categoryName);
    }

    public async Task<IReadOnlyList<SeriesEpisodeInfo>> GetEpisodesAsync(int seriesId, CancellationToken cancellationToken)
        => await GetEpisodesAsync(seriesId, null, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<SeriesEpisodeInfo>> GetEpisodesAsync(int seriesId, string? providerId, CancellationToken cancellationToken)
    {
        var cache = await _cacheService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var series = cache.SeriesItems.FirstOrDefault(item =>
            item.SeriesId == seriesId
            && (string.IsNullOrWhiteSpace(providerId)
                || string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)));
        if (series is null)
        {
            return [];
        }

        return series.Seasons
            .SelectMany(season => season.Episodes.Select(episode => ToInfo(series, season, episode)))
            .ToList();
    }

    public async Task<IReadOnlyList<MediaSourceInfo>> GetEpisodeMediaSourcesAsync(int episodeStreamId, CancellationToken cancellationToken)
        => await GetEpisodeMediaSourcesAsync(episodeStreamId, null, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<MediaSourceInfo>> GetEpisodeMediaSourcesAsync(int episodeStreamId, string? providerId, CancellationToken cancellationToken)
    {
        var cache = await _cacheService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var episode = cache.SeriesItems
            .SelectMany(series => series.Seasons)
            .SelectMany(season => season.Episodes)
            .FirstOrDefault(item => item.StreamId == episodeStreamId
                && (string.IsNullOrWhiteSpace(providerId)
                    || string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)));

        if (episode is null)
        {
            return [];
        }

        return [CreateMediaSource(episode, _configProvider())];
    }

    public async Task<IReadOnlyList<CachedSeriesItem>> GetSeriesAsync(CancellationToken cancellationToken)
    {
        var cache = await _cacheService.LoadAsync(cancellationToken).ConfigureAwait(false);
        return cache.SeriesItems;
    }

    public string ResolveEpisodeStreamUrl(PluginConfiguration config, CachedEpisodeItem episode)
        => _streamResolver.ResolveEpisodeUrl(config, episode.StreamId, episode.ContainerExtension);

    private MediaSourceInfo CreateMediaSource(CachedEpisodeItem episode, PluginConfiguration config)
    {
        var streamUrl = ResolveEpisodeStreamUrl(config, episode);
        return new MediaSourceInfo
        {
            Id = $"jellyxtreme-series-{episode.StreamId}",
            Name = episode.Title,
            Path = streamUrl,
            Protocol = MediaProtocol.Http,
            Type = MediaSourceType.Default,
            Container = episode.ContainerExtension,
            IsRemote = true,
            SupportsDirectPlay = true,
            SupportsDirectStream = true,
            SupportsTranscoding = true,
            RequiresOpening = false,
            RequiresClosing = false
        };
    }

    private static SeriesItemInfo ToInfo(CachedSeriesItem series, string? categoryName)
        => new(
            series.Name,
            series.SeriesId,
            series.CategoryId,
            categoryName,
            series.Poster,
            series.Plot,
            series.Rating,
            series.Seasons.Count,
            series.Seasons.Sum(season => season.Episodes.Count));

    private static SeriesEpisodeInfo ToInfo(CachedSeriesItem series, CachedSeason season, CachedEpisodeItem episode)
        => new(
            series.SeriesId,
            series.Name,
            season.SeasonNumber,
            episode.Title,
            episode.StreamId,
            episode.EpisodeNumber,
            episode.ContainerExtension,
            episode.Poster,
            episode.Plot,
            episode.ReleaseDate);

    private static string? NormalizeProviderId(string? providerId)
        => string.IsNullOrWhiteSpace(providerId)
            ? null
            : providerId;
}

public sealed class XtreamSeriesProvider : SeriesProvider
{
    public XtreamSeriesProvider(XtreamCacheService cacheService, StreamResolverService streamResolver)
        : base(cacheService, streamResolver)
    {
    }

    public XtreamSeriesProvider(
        XtreamCacheService cacheService,
        StreamResolverService streamResolver,
        Func<PluginConfiguration> configProvider)
        : base(cacheService, streamResolver, configProvider)
    {
    }
}

public sealed record SeriesItemInfo(
    string Name,
    int SeriesId,
    string CategoryId,
    string? CategoryName,
    string? Poster,
    string? Plot,
    double? Rating,
    int SeasonCount,
    int EpisodeCount);

public sealed record SeriesEpisodeInfo(
    int SeriesId,
    string SeriesName,
    int SeasonNumber,
    string Title,
    int StreamId,
    string? EpisodeNumber,
    string ContainerExtension,
    string? Poster,
    string? Plot,
    string? ReleaseDate);
