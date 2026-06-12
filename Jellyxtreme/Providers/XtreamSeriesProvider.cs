using Jellyxtreme.Cache;
using Jellyxtreme.Configuration;
using Jellyxtreme.Services;

namespace Jellyxtreme.Providers;

public sealed class XtreamSeriesProvider
{
    private readonly XtreamCacheService _cacheService;
    private readonly StreamResolverService _streamResolver;

    public XtreamSeriesProvider(XtreamCacheService cacheService, StreamResolverService streamResolver)
    {
        _cacheService = cacheService;
        _streamResolver = streamResolver;
    }

    public async Task<IReadOnlyList<CachedSeriesItem>> GetSeriesAsync(CancellationToken cancellationToken)
    {
        var cache = await _cacheService.LoadAsync(cancellationToken).ConfigureAwait(false);
        return cache.SeriesItems;
    }

    public string ResolveEpisodeStreamUrl(PluginConfiguration config, CachedEpisodeItem episode)
        => _streamResolver.ResolveEpisodeUrl(config, episode.StreamId, episode.ContainerExtension);
}
