using Jellyxtreme.Cache;
using Jellyxtreme.Configuration;
using Jellyxtreme.Services;

namespace Jellyxtreme.Providers;

public sealed class XtreamVodProvider
{
    private readonly XtreamCacheService _cacheService;
    private readonly StreamResolverService _streamResolver;

    public XtreamVodProvider(XtreamCacheService cacheService, StreamResolverService streamResolver)
    {
        _cacheService = cacheService;
        _streamResolver = streamResolver;
    }

    public async Task<IReadOnlyList<CachedVodItem>> GetMoviesAsync(CancellationToken cancellationToken)
    {
        var cache = await _cacheService.LoadAsync(cancellationToken).ConfigureAwait(false);
        return cache.VodItems;
    }

    public string ResolveMovieStreamUrl(PluginConfiguration config, CachedVodItem movie)
        => _streamResolver.ResolveVodUrl(config, movie.StreamId, movie.ContainerExtension);
}
