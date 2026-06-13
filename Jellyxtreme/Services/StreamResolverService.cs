using Jellyxtreme.Api;
using Jellyxtreme.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyxtreme.Services;

public sealed class StreamResolverService
{
    private readonly XtreamApiClient _apiClient;

    public StreamResolverService(XtreamApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public string ResolveLiveUrl(PluginConfiguration config, int streamId, string extension = "ts")
        => _apiClient.GetLiveStreamUrl(XtreamConnectionSettings.FromConfig(config), streamId, extension);

    public string ResolveVodUrl(PluginConfiguration config, int streamId, string extension = "mp4")
        => _apiClient.GetVodStreamUrl(XtreamConnectionSettings.FromConfig(config), streamId, extension);

    public string ResolveEpisodeUrl(PluginConfiguration config, int streamId, string extension = "mp4")
        => _apiClient.GetSeriesStreamUrl(XtreamConnectionSettings.FromConfig(config), streamId, extension);
}
