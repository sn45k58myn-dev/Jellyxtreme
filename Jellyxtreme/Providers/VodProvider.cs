using Jellyxtreme.Cache;
using Jellyxtreme.Configuration;
using Jellyxtreme.Services;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.MediaInfo;

namespace Jellyxtreme.Providers;

public class VodProvider
{
    private readonly XtreamCacheService _cacheService;
    private readonly StreamResolverService _streamResolver;
    private readonly Func<PluginConfiguration> _configProvider;

    public VodProvider(XtreamCacheService cacheService, StreamResolverService streamResolver)
        : this(cacheService, streamResolver, () => Plugin.Instance?.Configuration ?? new PluginConfiguration())
    {
    }

    public VodProvider(
        XtreamCacheService cacheService,
        StreamResolverService streamResolver,
        Func<PluginConfiguration> configProvider)
    {
        _cacheService = cacheService;
        _streamResolver = streamResolver;
        _configProvider = configProvider;
    }

    public async Task<IReadOnlyList<VodItemInfo>> GetItemsAsync(CancellationToken cancellationToken)
    {
        var cache = await _cacheService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var categoryNames = cache.VodCategories.ToDictionary(
            category => XtreamCacheIdentity.BuildItemKey(category.ProviderId, category.CategoryId),
            category => category.Name,
            StringComparer.OrdinalIgnoreCase);

        return cache.VodItems
            .Select(item => ToInfo(
                item,
                categoryNames.GetValueOrDefault(XtreamCacheIdentity.BuildItemKey(item.ProviderId, item.CategoryId))))
            .ToList();
    }

    public async Task<VodItemInfo?> GetItemAsync(int streamId, CancellationToken cancellationToken)
        => await GetItemAsync(streamId, null, cancellationToken).ConfigureAwait(false);

    public async Task<VodItemInfo?> GetItemAsync(int streamId, string? providerId, CancellationToken cancellationToken)
    {
        var cache = await _cacheService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var normalizedProviderId = NormalizeProviderId(providerId);

        var item = cache.VodItems.FirstOrDefault(vod =>
            vod.StreamId == streamId
            && (normalizedProviderId is null || string.Equals(vod.ProviderId, normalizedProviderId, StringComparison.OrdinalIgnoreCase)));
        if (item is null)
        {
            return null;
        }

        var categoryName = cache.VodCategories.FirstOrDefault(category =>
                string.Equals(category.ProviderId, item.ProviderId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(category.CategoryId, item.CategoryId, StringComparison.OrdinalIgnoreCase))
            ?.Name;
        return ToInfo(item, categoryName);
    }

    public async Task<IReadOnlyList<MediaSourceInfo>> GetMediaSourcesAsync(int streamId, CancellationToken cancellationToken)
        => await GetMediaSourcesAsync(streamId, null, cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<MediaSourceInfo>> GetMediaSourcesAsync(int streamId, string? providerId, CancellationToken cancellationToken)
    {
        var cache = await _cacheService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var item = cache.VodItems.FirstOrDefault(vod =>
            vod.StreamId == streamId
            && (string.IsNullOrWhiteSpace(providerId)
                || string.Equals(vod.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)));

        if (item is null)
        {
            return [];
        }

        return [CreateMediaSource(item, _configProvider())];
    }

    public async Task<IReadOnlyList<CachedVodItem>> GetMoviesAsync(CancellationToken cancellationToken)
    {
        var cache = await _cacheService.LoadAsync(cancellationToken).ConfigureAwait(false);
        return cache.VodItems;
    }

    public string ResolveMovieStreamUrl(PluginConfiguration config, CachedVodItem movie)
        => _streamResolver.ResolveVodUrl(config, movie.StreamId, movie.ContainerExtension);

    private MediaSourceInfo CreateMediaSource(CachedVodItem item, PluginConfiguration config)
    {
        var streamUrl = ResolveMovieStreamUrl(config, item);
        return new MediaSourceInfo
        {
            Id = $"jellyxtreme-vod-{XtreamCacheIdentity.BuildItemKey(item.ProviderId, item.StreamId)}",
            Name = item.Name,
            Path = streamUrl,
            Protocol = MediaProtocol.Http,
            Type = MediaSourceType.Default,
            Container = item.ContainerExtension,
            IsRemote = true,
            SupportsDirectPlay = true,
            SupportsDirectStream = true,
            SupportsTranscoding = true,
            RequiresOpening = false,
            RequiresClosing = false
        };
    }

    private static VodItemInfo ToInfo(CachedVodItem item, string? categoryName)
        => new(
            item.Name,
            item.StreamId,
            item.CategoryId,
            categoryName,
            item.Poster,
            item.Rating,
            item.ContainerExtension,
            item.Added);

    private static string? NormalizeProviderId(string? providerId)
        => string.IsNullOrWhiteSpace(providerId)
            ? null
            : providerId;
}

public sealed class XtreamVodProvider : VodProvider
{
    public XtreamVodProvider(XtreamCacheService cacheService, StreamResolverService streamResolver)
        : base(cacheService, streamResolver)
    {
    }

    public XtreamVodProvider(
        XtreamCacheService cacheService,
        StreamResolverService streamResolver,
        Func<PluginConfiguration> configProvider)
        : base(cacheService, streamResolver, configProvider)
    {
    }
}

public sealed record VodItemInfo(
    string Name,
    int StreamId,
    string CategoryId,
    string? CategoryName,
    string? Poster,
    double? Rating,
    string ContainerExtension,
    string? Added);
