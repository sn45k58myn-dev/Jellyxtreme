using Jellyxtreme.Cache;
using Jellyxtreme.Configuration;
using Jellyxtreme.Services;

namespace Jellyxtreme.Providers;

public sealed class XtreamVodProvider
{
    private readonly XtreamCacheStore _cacheStore;
    private readonly XtreamStreamResolver _streamResolver;

    public XtreamVodProvider(XtreamCacheStore cacheStore, XtreamStreamResolver streamResolver)
    {
        _cacheStore = cacheStore;
        _streamResolver = streamResolver;
    }

    public async Task<IReadOnlyList<CachedVodMovie>> GetMoviesAsync(CancellationToken cancellationToken)
    {
        var cache = await _cacheStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        return cache.VodMovies;
    }

    public string ResolveMovieStreamUrl(PluginConfiguration config, CachedVodMovie movie)
        => _streamResolver.ResolveVodUrl(config, movie.StreamId, movie.ContainerExtension);
}
