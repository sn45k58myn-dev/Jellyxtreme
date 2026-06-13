using System.Net.Http.Json;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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

    public XtreamApiClient(IHttpClientFactory httpClientFactory, ILogger<XtreamApiClient> logger)
    {
        _httpClient = httpClientFactory.CreateClient(nameof(XtreamApiClient));
        _logger = logger;
    }

    public static bool TryNormalizeServerUrl(string? serverUrl, out Uri serverUri)
    {
        serverUri = null!;

        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            return false;
        }

        var candidate = serverUrl.Trim();
        if (!candidate.Contains("://", StringComparison.Ordinal))
        {
            candidate = "http://" + candidate;
        }

        if (!Uri.TryCreate(candidate.TrimEnd('/'), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !IsLikelyServerHost(uri.Host))
        {
            return false;
        }

        serverUri = NormalizeXtreamBaseUri(uri);
        return true;
    }

    public async Task<XtreamConnectionResult> TestConnectionAsync(XtreamConnectionSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var response = await GetFromApiAsync<XtreamAuthenticationResponse>(settings, null, cancellationToken).ConfigureAwait(false);
            var isAuthenticated = response?.UserInfo?.Auth == 1
                || string.Equals(response?.UserInfo?.Status, "Active", StringComparison.OrdinalIgnoreCase);

            return new XtreamConnectionResult(isAuthenticated, isAuthenticated ? "Connection successful." : "Xtream authentication failed.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            _logger.LogWarning("Xtream connection test failed for {ServerUrl}: {Error}", Redact(settings.ServerUrl), Redact(ex.Message));
            return new XtreamConnectionResult(false, GetSafeConnectionFailureMessage(ex));
        }
    }

    public Task<List<XtreamCategory>> GetLiveCategoriesAsync(XtreamConnectionSettings settings, CancellationToken cancellationToken)
        => GetListAsync<XtreamCategory>(settings, "get_live_categories", cancellationToken);

    public Task<List<XtreamCategory>> GetVodCategoriesAsync(XtreamConnectionSettings settings, CancellationToken cancellationToken)
        => GetListAsync<XtreamCategory>(settings, "get_vod_categories", cancellationToken);

    public Task<List<XtreamCategory>> GetSeriesCategoriesAsync(XtreamConnectionSettings settings, CancellationToken cancellationToken)
        => GetListAsync<XtreamCategory>(settings, "get_series_categories", cancellationToken);

    public Task<List<XtreamLiveStream>> GetLiveStreamsAsync(XtreamConnectionSettings settings, CancellationToken cancellationToken)
        => GetListAsync<XtreamLiveStream>(settings, "get_live_streams", cancellationToken);

    public Task<List<XtreamVodStream>> GetVodStreamsAsync(XtreamConnectionSettings settings, CancellationToken cancellationToken)
        => GetListAsync<XtreamVodStream>(settings, "get_vod_streams", cancellationToken);

    public Task<List<XtreamSeries>> GetSeriesAsync(XtreamConnectionSettings settings, CancellationToken cancellationToken)
        => GetListAsync<XtreamSeries>(settings, "get_series", cancellationToken);

    public async Task<XtreamSeriesInfoResponse?> GetSeriesInfoAsync(XtreamConnectionSettings settings, int seriesId, CancellationToken cancellationToken)
        => await GetFromApiAsync<XtreamSeriesInfoResponse>(settings, "get_series_info", cancellationToken, ("series_id", seriesId.ToString()))
            .ConfigureAwait(false);

    public async Task<string> GetXmlTvAsync(XtreamConnectionSettings settings, CancellationToken cancellationToken)
    {
        var uri = BuildXmlTvUri(settings);
        _logger.LogDebug("Fetching Xtream XMLTV from {ServerUrl}.", Redact(settings.ServerUrl));
        return await ExecuteWithTimeout(settings, token => _httpClient.GetStringAsync(uri, token), cancellationToken).ConfigureAwait(false);
    }

    public string GetLiveStreamUrl(XtreamConnectionSettings settings, int streamId, string extension = "ts")
        => BuildStreamUrl(settings, "live", streamId, extension, nameof(GetLiveStreamUrl));

    public string GetVodStreamUrl(XtreamConnectionSettings settings, int streamId, string extension = "mp4")
        => BuildStreamUrl(settings, "movie", streamId, extension, nameof(GetVodStreamUrl));

    public string GetSeriesStreamUrl(XtreamConnectionSettings settings, int streamId, string extension = "mp4")
        => BuildStreamUrl(settings, "series", streamId, extension, nameof(GetSeriesStreamUrl));

    private async Task<List<T>> GetListAsync<T>(XtreamConnectionSettings settings, string action, CancellationToken cancellationToken)
        => await GetFromApiAsync<List<T>>(settings, action, cancellationToken).ConfigureAwait(false) ?? [];

    private async Task<T?> GetFromApiAsync<T>(XtreamConnectionSettings settings, string? action, CancellationToken cancellationToken, params (string Key, string Value)[] query)
    {
        var uri = BuildPlayerApiUri(settings, action, query);
        _logger.LogDebug("Fetching Xtream action {Action} from {ServerUrl}.", action ?? "authenticate", Redact(settings.ServerUrl));
        return await ExecuteWithTimeout(settings, token => _httpClient.GetFromJsonAsync<T>(uri, JsonOptions, token), cancellationToken).ConfigureAwait(false);
    }

    private static Uri BuildPlayerApiUri(XtreamConnectionSettings settings, string? action, params (string Key, string Value)[] query)
    {
        var serverUri = ValidateSettings(settings);

        var parameters = new List<string>
        {
            $"username={Uri.EscapeDataString(settings.Username)}",
            $"password={Uri.EscapeDataString(settings.Password)}"
        };

        if (!string.IsNullOrWhiteSpace(action))
        {
            parameters.Add($"action={Uri.EscapeDataString(action)}");
        }

        parameters.AddRange(query.Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value)}"));

        return new Uri(serverUri, $"player_api.php?{string.Join("&", parameters)}");
    }

    private static Uri BuildXmlTvUri(XtreamConnectionSettings settings)
    {
        var serverUri = ValidateSettings(settings);

        return new Uri(serverUri, $"xmltv.php?username={Uri.EscapeDataString(settings.Username)}&password={Uri.EscapeDataString(settings.Password)}");
    }

    private static Uri NormalizeXtreamBaseUri(Uri uri)
    {
        var path = uri.AbsolutePath.Trim('/');
        if (string.IsNullOrWhiteSpace(path))
        {
            return new UriBuilder(uri) { Path = string.Empty, Query = string.Empty, Fragment = string.Empty }.Uri;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var endpointIndex = Array.FindIndex(segments, segment =>
            string.Equals(segment, "player_api.php", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segment, "get.php", StringComparison.OrdinalIgnoreCase)
            || string.Equals(segment, "xmltv.php", StringComparison.OrdinalIgnoreCase));

        if (endpointIndex < 0)
        {
            return new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty }.Uri;
        }

        var basePath = endpointIndex == 0 ? string.Empty : string.Join("/", segments.Take(endpointIndex));
        return new UriBuilder(uri) { Path = basePath, Query = string.Empty, Fragment = string.Empty }.Uri;
    }

    private static bool IsLikelyServerHost(string host)
        => !string.IsNullOrWhiteSpace(host)
            && (host.Contains('.', StringComparison.Ordinal)
                || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                || IPAddress.TryParse(host, out _));

    private static string BuildStreamUrl(XtreamConnectionSettings settings, string type, int streamId, string extension, string operation)
    {
        var serverUri = ValidateSettings(settings);
        if (streamId <= 0)
        {
            throw new XtreamValidationException("Stream ID must be greater than zero.", operation);
        }

        if (string.IsNullOrWhiteSpace(extension))
        {
            throw new XtreamValidationException("Stream extension is required.", operation);
        }

        var safeExtension = extension.Trim().TrimStart('.');
        if (string.IsNullOrWhiteSpace(safeExtension))
        {
            throw new XtreamValidationException("Stream extension is required.", operation);
        }

        return new Uri(serverUri, $"{type}/{Uri.EscapeDataString(settings.Username)}/{Uri.EscapeDataString(settings.Password)}/{streamId}.{safeExtension}").AbsoluteUri;
    }

    private static Uri ValidateSettings(XtreamConnectionSettings settings)
    {
        if (!TryNormalizeServerUrl(settings.ServerUrl, out var serverUri))
        {
            throw new XtreamValidationException("Server URL must be an absolute http or https URL.", nameof(XtreamConnectionSettings));
        }

        if (string.IsNullOrWhiteSpace(settings.Username))
        {
            throw new XtreamValidationException("Username is required.", nameof(XtreamConnectionSettings));
        }

        if (string.IsNullOrWhiteSpace(settings.Password))
        {
            throw new XtreamValidationException("Password is required.", nameof(XtreamConnectionSettings));
        }

        if (settings.Timeout <= TimeSpan.Zero)
        {
            throw new XtreamValidationException("Timeout must be greater than zero.", nameof(XtreamConnectionSettings));
        }

        return serverUri;
    }

    private static async Task<T> ExecuteWithTimeout<T>(
        XtreamConnectionSettings settings,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(settings.Timeout);

            try
            {
                return await action(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTransient(ex, cancellationToken) && attempt < maxAttempts)
            {
                var delay = TimeSpan.FromMilliseconds(250 * attempt);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        using var finalTimeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        finalTimeoutSource.CancelAfter(settings.Timeout);
        return await action(finalTimeoutSource.Token).ConfigureAwait(false);
    }

    public static string Redact(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return RedactKnownFields(value);
        }

        var builder = new UriBuilder(uri);
        if (!string.IsNullOrEmpty(builder.Query))
        {
            builder.Query = "redacted=true";
        }

        var segments = builder.Path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 4
            && IsStreamSegment(segments[0]))
        {
            segments[1] = "redacted";
            segments[2] = "redacted";
            builder.Path = "/" + string.Join("/", segments);
        }

        return builder.Uri.AbsoluteUri;
    }

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken)
        => exception is HttpRequestException
            || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested;

    private static string GetSafeConnectionFailureMessage(Exception exception)
        => exception switch
        {
            TaskCanceledException => "Connection timed out. Check the server URL and port.",
            JsonException => "The server responded, but not with a valid Xtream JSON response. Check that this is an Xtream player API endpoint.",
            XtreamValidationException validationException => validationException.Message,
            HttpRequestException httpRequestException when httpRequestException.StatusCode is not null => $"Connection failed with HTTP {(int)httpRequestException.StatusCode}. Check the server URL and credentials.",
            HttpRequestException => "Connection failed before authentication. Check the server URL, DNS, port, and network access.",
            _ => "Connection failed. Check the server URL and credentials."
        };

    private static bool IsStreamSegment(string value)
        => string.Equals(value, "live", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "movie", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "series", StringComparison.OrdinalIgnoreCase);

    private static string RedactKnownFields(string value)
    {
        return Regex.Replace(
            value,
            "(username|password)=([^&\\s]+)",
            "$1=redacted",
            RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(100));
    }
}

public sealed record XtreamConnectionSettings(string ServerUrl, string Username, string Password, TimeSpan Timeout)
{
    public static XtreamConnectionSettings FromConfig(Configuration.PluginConfiguration config)
        => new(config.ServerUrl, config.Username, config.Password, TimeSpan.FromMinutes(Math.Max(1, config.CacheMinutes)));

    public static XtreamConnectionSettings FromRequest(string serverUrl, string username, string password)
        => new(serverUrl, username, password, TimeSpan.FromSeconds(30));
}

public sealed record XtreamConnectionResult(bool IsSuccess, string Message);

public sealed class XtreamValidationException : InvalidOperationException
{
    public XtreamValidationException(string message, string operation)
        : base(message)
    {
        Operation = operation;
    }

    public string Operation { get; }
}

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
