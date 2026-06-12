using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyxtreme.Api;

public sealed class XtreamApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<XtreamApiClient> _logger;
    private readonly Uri _serverUri;
    private readonly string _username;
    private readonly string _password;

    public XtreamApiClient(
        IHttpClientFactory httpClientFactory,
        ILogger<XtreamApiClient> logger,
        string serverUrl,
        string username,
        string password,
        TimeSpan? timeout = null)
    {
        if (!TryNormalizeServerUrl(serverUrl, out var serverUri))
        {
            throw new ArgumentException("ServerUrl must be an absolute http or https URL.", nameof(serverUrl));
        }

        _httpClient = httpClientFactory.CreateClient(nameof(XtreamApiClient));
        _httpClient.Timeout = timeout ?? TimeSpan.FromSeconds(30);
        _logger = logger;
        _serverUri = serverUri;
        _username = username;
        _password = password;
    }

    public static bool TryNormalizeServerUrl(string? serverUrl, out Uri serverUri)
    {
        serverUri = null!;

        if (string.IsNullOrWhiteSpace(serverUrl)
            || !Uri.TryCreate(serverUrl.Trim().TrimEnd('/'), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        serverUri = uri;
        return true;
    }

    public async Task<XtreamConnectionResult> TestConnectionAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await GetFromApiAsync<XtreamAuthenticationResponse>(null, cancellationToken).ConfigureAwait(false);
            var isAuthenticated = response?.UserInfo?.Auth == 1
                || string.Equals(response?.UserInfo?.Status, "Active", StringComparison.OrdinalIgnoreCase);

            return new XtreamConnectionResult(isAuthenticated, isAuthenticated ? "Connection successful." : "Xtream authentication failed.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Xtream connection test failed for {ServerUrl}.", Redact(_serverUri.ToString()));
            return new XtreamConnectionResult(false, "Connection failed. Check the server URL and credentials.");
        }
    }

    public Task<List<XtreamCategory>> GetLiveCategoriesAsync(CancellationToken cancellationToken)
        => GetListAsync<XtreamCategory>("get_live_categories", cancellationToken);

    public Task<List<XtreamCategory>> GetVodCategoriesAsync(CancellationToken cancellationToken)
        => GetListAsync<XtreamCategory>("get_vod_categories", cancellationToken);

    public Task<List<XtreamCategory>> GetSeriesCategoriesAsync(CancellationToken cancellationToken)
        => GetListAsync<XtreamCategory>("get_series_categories", cancellationToken);

    public Task<List<XtreamLiveStream>> GetLiveStreamsAsync(CancellationToken cancellationToken)
        => GetListAsync<XtreamLiveStream>("get_live_streams", cancellationToken);

    public Task<List<XtreamVodStream>> GetVodStreamsAsync(CancellationToken cancellationToken)
        => GetListAsync<XtreamVodStream>("get_vod_streams", cancellationToken);

    public Task<List<XtreamSeries>> GetSeriesAsync(CancellationToken cancellationToken)
        => GetListAsync<XtreamSeries>("get_series", cancellationToken);

    public async Task<XtreamSeriesInfoResponse?> GetSeriesInfoAsync(int seriesId, CancellationToken cancellationToken)
        => await GetFromApiAsync<XtreamSeriesInfoResponse>("get_series_info", cancellationToken, ("series_id", seriesId.ToString()))
            .ConfigureAwait(false);

    public async Task<string> GetXmlTvAsync(CancellationToken cancellationToken)
    {
        var uri = BuildXmlTvUri();
        _logger.LogDebug("Fetching Xtream XMLTV from {ServerUrl}.", Redact(_serverUri.ToString()));
        return await _httpClient.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
    }

    public string GetLiveStreamUrl(int streamId, string extension = "ts")
        => BuildStreamUrl("live", streamId, extension);

    public string GetVodStreamUrl(int streamId, string extension = "mp4")
        => BuildStreamUrl("movie", streamId, extension);

    public string GetSeriesStreamUrl(int streamId, string extension = "mp4")
        => BuildStreamUrl("series", streamId, extension);

    private async Task<List<T>> GetListAsync<T>(string action, CancellationToken cancellationToken)
        => await GetFromApiAsync<List<T>>(action, cancellationToken).ConfigureAwait(false) ?? [];

    private async Task<T?> GetFromApiAsync<T>(string? action, CancellationToken cancellationToken, params (string Key, string Value)[] query)
    {
        var uri = BuildPlayerApiUri(action, query);
        _logger.LogDebug("Fetching Xtream action {Action} from {ServerUrl}.", action ?? "authenticate", Redact(_serverUri.ToString()));
        return await _httpClient.GetFromJsonAsync<T>(uri, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private Uri BuildPlayerApiUri(string? action, params (string Key, string Value)[] query)
    {
        var parameters = new List<string>
        {
            $"username={Uri.EscapeDataString(_username)}",
            $"password={Uri.EscapeDataString(_password)}"
        };

        if (!string.IsNullOrWhiteSpace(action))
        {
            parameters.Add($"action={Uri.EscapeDataString(action)}");
        }

        parameters.AddRange(query.Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"));

        return new Uri(_serverUri, $"player_api.php?{string.Join("&", parameters)}");
    }

    private Uri BuildXmlTvUri()
        => new(_serverUri, $"xmltv.php?username={Uri.EscapeDataString(_username)}&password={Uri.EscapeDataString(_password)}");

    private string BuildStreamUrl(string type, int streamId, string extension)
    {
        var safeExtension = string.IsNullOrWhiteSpace(extension) ? "mp4" : extension.TrimStart('.');
        return new Uri(_serverUri, $"{type}/{Uri.EscapeDataString(_username)}/{Uri.EscapeDataString(_password)}/{streamId}.{safeExtension}").AbsoluteUri;
    }

    public static string Redact(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var queryIndex = value.IndexOf('?', StringComparison.Ordinal);
        return queryIndex >= 0 ? value[..queryIndex] + "?redacted=true" : value;
    }
}

public sealed record XtreamConnectionResult(bool IsSuccess, string Message);

public sealed class XtreamAuthenticationResponse
{
    [JsonPropertyName("user_info")]
    public XtreamUserInfo? UserInfo { get; set; }

    [JsonPropertyName("server_info")]
    public XtreamServerInfo? ServerInfo { get; set; }
}

public sealed class XtreamUserInfo
{
    [JsonPropertyName("auth")]
    public int Auth { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }
}

public sealed class XtreamServerInfo
{
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("server_protocol")]
    public string? ServerProtocol { get; set; }
}

public sealed class XtreamCategory
{
    [JsonPropertyName("category_id")]
    public string CategoryId { get; set; } = string.Empty;

    [JsonPropertyName("category_name")]
    public string CategoryName { get; set; } = string.Empty;

    [JsonPropertyName("parent_id")]
    public int ParentId { get; set; }
}

public sealed class XtreamLiveStream
{
    [JsonPropertyName("num")]
    public int Number { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("stream_id")]
    public int StreamId { get; set; }

    [JsonPropertyName("stream_icon")]
    public string? StreamIcon { get; set; }

    [JsonPropertyName("epg_channel_id")]
    public string? EpgChannelId { get; set; }

    [JsonPropertyName("category_id")]
    public string? CategoryId { get; set; }
}

public sealed class XtreamVodStream
{
    [JsonPropertyName("num")]
    public int Number { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("stream_id")]
    public int StreamId { get; set; }

    [JsonPropertyName("stream_icon")]
    public string? StreamIcon { get; set; }

    [JsonPropertyName("rating")]
    public double? Rating { get; set; }

    [JsonPropertyName("added")]
    public string? Added { get; set; }

    [JsonPropertyName("category_id")]
    public string? CategoryId { get; set; }

    [JsonPropertyName("container_extension")]
    public string? ContainerExtension { get; set; }
}

public sealed class XtreamSeries
{
    [JsonPropertyName("num")]
    public int Number { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("series_id")]
    public int SeriesId { get; set; }

    [JsonPropertyName("cover")]
    public string? Cover { get; set; }

    [JsonPropertyName("plot")]
    public string? Plot { get; set; }

    [JsonPropertyName("category_id")]
    public string? CategoryId { get; set; }

    [JsonPropertyName("rating")]
    public double? Rating { get; set; }
}

public sealed class XtreamSeriesInfoResponse
{
    [JsonPropertyName("info")]
    public XtreamSeriesInfo? Info { get; set; }

    [JsonPropertyName("episodes")]
    public Dictionary<string, List<XtreamEpisode>>? Episodes { get; set; }
}

public sealed class XtreamSeriesInfo
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("cover")]
    public string? Cover { get; set; }

    [JsonPropertyName("plot")]
    public string? Plot { get; set; }

    [JsonPropertyName("genre")]
    public string? Genre { get; set; }

    [JsonPropertyName("rating")]
    public string? Rating { get; set; }
}

public sealed class XtreamEpisode
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("episode_num")]
    public string? EpisodeNumber { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("container_extension")]
    public string? ContainerExtension { get; set; }

    [JsonPropertyName("info")]
    public XtreamEpisodeInfo? Info { get; set; }

    [JsonPropertyName("season")]
    public int Season { get; set; }
}

public sealed class XtreamEpisodeInfo
{
    [JsonPropertyName("movie_image")]
    public string? MovieImage { get; set; }

    [JsonPropertyName("plot")]
    public string? Plot { get; set; }

    [JsonPropertyName("releasedate")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("rating")]
    public string? Rating { get; set; }
}
