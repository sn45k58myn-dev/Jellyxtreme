using Jellyxtreme.Cache;
using Jellyxtreme.Configuration;
using Jellyxtreme.Services;

namespace Jellyxtreme.Providers;

public sealed class XtreamSeriesProvider
{
    private readonly XtreamCacheStore _cacheStore;
    private readonly XtreamStreamResolver _streamResolver;

    public XtreamSeriesProvider(XtreamCacheStore cacheStore, XtreamStreamResolver streamResolver)
    {
        _cacheStore = cacheStore;
        _streamResolver = streamResolver;
    }

    public async Task<IReadOnlyList<CachedSeries>> GetSeriesAsync(CancellationToken cancellationToken)
    {
        var cache = await _cacheStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        return cache.Series;
    }

    public string ResolveEpisodeStreamUrl(PluginConfiguration config, CachedEpisode episode)
        => _streamResolver.ResolveEpisodeUrl(config, episode.StreamId, episode.ContainerExtension);
}
