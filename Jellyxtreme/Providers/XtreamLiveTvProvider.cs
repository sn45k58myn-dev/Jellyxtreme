using Jellyxtreme.Cache;
using Jellyxtreme.Configuration;
using Jellyxtreme.Services;

namespace Jellyxtreme.Providers;

public sealed class XtreamLiveTvProvider
{
    private readonly XtreamCacheService _cacheService;
    private readonly StreamResolverService _streamResolver;

    public XtreamLiveTvProvider(XtreamCacheService cacheService, StreamResolverService streamResolver)
    {
        _cacheService = cacheService;
        _streamResolver = streamResolver;
    }

    public async Task<IReadOnlyList<XtreamLiveChannelInfo>> GetChannelsAsync(CancellationToken cancellationToken)
    {
        var cache = await _cacheService.LoadAsync(cancellationToken).ConfigureAwait(false);
        return cache.LiveChannels
            .Select(channel => new XtreamLiveChannelInfo(
                channel.Name,
                channel.StreamId,
                channel.Logo,
                channel.EpgChannelId,
                channel.GroupName))
            .ToList();
    }

    public string ResolveChannelStreamUrl(PluginConfiguration config, CachedLiveChannel channel)
        => _streamResolver.ResolveLiveUrl(config, channel.StreamId, channel.StreamExtension);
}

public sealed record XtreamLiveChannelInfo(
    string Name,
    int StreamId,
    string? Logo,
    string? EpgChannelId,
    string GroupName);
