using Jellyxtreme.Api;
using Jellyxtreme.Configuration;

namespace Jellyxtreme.Services;

public sealed class StreamResolverService
{
    private readonly XtreamApiClient _apiClient;
    private readonly ICredentialStore _credentialStore;

    public StreamResolverService(XtreamApiClient apiClient, ICredentialStore credentialStore)
    {
        _apiClient = apiClient;
        _credentialStore = credentialStore;
    }

    public string ResolveLiveUrl(PluginConfiguration config, int streamId, string extension = "ts")
        => ResolveLiveUrl(config, null, streamId, extension);

    public string ResolveVodUrl(PluginConfiguration config, int streamId, string extension = "mp4")
        => ResolveVodUrl(config, null, streamId, extension);

    public string ResolveEpisodeUrl(PluginConfiguration config, int streamId, string extension = "mp4")
        => ResolveEpisodeUrl(config, null, streamId, extension);

    public string ResolveLiveUrl(PluginConfiguration config, string? providerId, int streamId, string extension = "ts")
        => _apiClient.GetLiveStreamUrl(_credentialStore.GetConnectionSettings(config, providerId), streamId, extension);

    public string ResolveVodUrl(PluginConfiguration config, string? providerId, int streamId, string extension = "mp4")
        => _apiClient.GetVodStreamUrl(_credentialStore.GetConnectionSettings(config, providerId), streamId, extension);

    public string ResolveEpisodeUrl(PluginConfiguration config, string? providerId, int streamId, string extension = "mp4")
        => _apiClient.GetSeriesStreamUrl(_credentialStore.GetConnectionSettings(config, providerId), streamId, extension);
}
