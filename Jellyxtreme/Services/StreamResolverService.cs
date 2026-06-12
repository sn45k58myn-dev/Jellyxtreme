using Jellyxtreme.Api;
using Jellyxtreme.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyxtreme.Services;

public sealed class StreamResolverService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;

    public StreamResolverService(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    {
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
    }

    public string ResolveLiveUrl(PluginConfiguration config, int streamId, string extension = "ts")
        => CreateClient(config).GetLiveStreamUrl(streamId, extension);

    public string ResolveVodUrl(PluginConfiguration config, int streamId, string extension = "mp4")
        => CreateClient(config).GetVodStreamUrl(streamId, extension);

    public string ResolveEpisodeUrl(PluginConfiguration config, int streamId, string extension = "mp4")
        => CreateClient(config).GetSeriesStreamUrl(streamId, extension);

    private XtreamApiClient CreateClient(PluginConfiguration config)
        => new(
            _httpClientFactory,
            _loggerFactory.CreateLogger<XtreamApiClient>(),
            config.ServerUrl,
            config.Username,
            config.Password);
}
