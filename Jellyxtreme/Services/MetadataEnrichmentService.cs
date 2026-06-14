using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyxtreme.Cache;
using Jellyxtreme.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyxtreme.Services;

public sealed class MetadataEnrichmentService
{
    private const string TmdbBaseUrl = "https://api.themoviedb.org/3";
    private const string TmdbImageBaseUrl = "https://image.tmdb.org/t/p/original";
    private const string TvdbApiBaseUrl = "https://api4.thetvdb.com/v4";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<MetadataEnrichmentService> _logger;

    private string? _cachedTvdbToken;
    private DateTimeOffset _cachedTvdbTokenAt;

    public MetadataEnrichmentService(IHttpClientFactory httpClientFactory, ILogger<MetadataEnrichmentService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task EnrichVodMetadataAsync(CachedVodItem item, PluginConfiguration config, CancellationToken cancellationToken)
    {
        if (!ShouldEnrich(config))
        {
            return;
        }

        if (item is null || string.IsNullOrWhiteSpace(item.Name))
        {
            return;
        }

        var metadata = await ResolveMetadataAsync(item.Name, config, SearchType.Movie, cancellationToken).ConfigureAwait(false);
        ApplyMetadata(item, metadata);
    }

    public async Task EnrichSeriesMetadataAsync(CachedSeriesItem item, PluginConfiguration config, CancellationToken cancellationToken)
    {
        if (!ShouldEnrich(config))
        {
            return;
        }

        if (item is null || string.IsNullOrWhiteSpace(item.Name))
        {
            return;
        }

        var metadata = await ResolveMetadataAsync(item.Name, config, SearchType.Series, cancellationToken).ConfigureAwait(false);
        ApplyMetadata(item, metadata);
    }

    private static bool ShouldEnrich(PluginConfiguration config)
    {
        return config.EnableMetadataEnrichment
            && (!string.IsNullOrWhiteSpace(config.TmdbApiKey)
                || !string.IsNullOrWhiteSpace(config.TvdbApiKey));
    }

    private void ApplyMetadata(CachedVodItem item, ExternalMetadataResult? metadata)
    {
        if (metadata is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(item.Poster))
        {
            item.Poster = metadata.Poster;
        }

        item.Fanart = Coalesce(item.Fanart, metadata.Fanart);
        item.Backdrop = Coalesce(item.Backdrop, metadata.Backdrop);
        item.TmdbId = Coalesce(item.TmdbId, metadata.TmdbId);
        item.TvdbId = Coalesce(item.TvdbId, metadata.TvdbId);
        item.MetadataSource = metadata.Source;
    }

    private void ApplyMetadata(CachedSeriesItem item, ExternalMetadataResult? metadata)
    {
        if (metadata is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(item.Poster))
        {
            item.Poster = metadata.Poster;
        }

        item.Fanart = Coalesce(item.Fanart, metadata.Fanart);
        item.Backdrop = Coalesce(item.Backdrop, metadata.Backdrop);
        item.TmdbId = Coalesce(item.TmdbId, metadata.TmdbId);
        item.TvdbId = Coalesce(item.TvdbId, metadata.TvdbId);
        item.MetadataSource = metadata.Source;
    }

    private static string? Coalesce(string? existing, string? incoming)
        => string.IsNullOrWhiteSpace(existing) ? incoming : existing;

    private async Task<ExternalMetadataResult?> ResolveMetadataAsync(
        string title,
        PluginConfiguration config,
        SearchType searchType,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(config.TmdbApiKey))
        {
            try
            {
                var tmdbResult = await SearchTmdbAsync(title, searchType, config.TmdbApiKey, cancellationToken).ConfigureAwait(false);
                if (tmdbResult is not null)
                {
                    return tmdbResult;
                }
            }
            catch (Exception exception) when (IsTransientMetadataFailure(exception))
            {
                _logger.LogWarning(exception, "JellyXtreme metadata lookup failed against TMDB for {Title}.", title);
            }
            catch (Exception)
            {
            }
        }

        if (!string.IsNullOrWhiteSpace(config.TvdbApiKey))
        {
            try
            {
                var tvdbResult = await SearchTvdbAsync(title, searchType, config.TvdbApiKey, cancellationToken).ConfigureAwait(false);
                if (tvdbResult is not null)
                {
                    return tvdbResult;
                }
            }
            catch (Exception exception) when (IsTransientMetadataFailure(exception))
            {
                _logger.LogWarning(exception, "JellyXtreme metadata lookup failed against TVDB for {Title}.", title);
            }
            catch (Exception)
            {
            }
        }

        return null;
    }

    private async Task<ExternalMetadataResult?> SearchTmdbAsync(
        string query,
        SearchType searchType,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var endpoint = searchType == SearchType.Movie ? "search/movie" : "search/tv";
        var uri = new Uri($"{TmdbBaseUrl}/{endpoint}?api_key={Uri.EscapeDataString(apiKey)}&query={Uri.EscapeDataString(query)}&include_adult=false");

        var response = await ExecuteGetAsync(uri, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(response))
        {
            return null;
        }

        var payload = JsonSerializer.Deserialize<TmdbSearchResponse>(response, JsonOptions);
        var result = payload?.Results
            .OrderByDescending(result => !string.IsNullOrWhiteSpace(result.PosterPath) ? 1 : 0)
            .ThenByDescending(result => !string.IsNullOrWhiteSpace(result.BackdropPath) ? 1 : 0)
            .FirstOrDefault();

        if (result is null || result.Id <= 0)
        {
            return null;
        }

        return new ExternalMetadataResult
        {
            Source = "tmdb",
            TmdbId = result.Id.ToString(CultureInfo.InvariantCulture),
            Poster = ResolveImageUrl(result.PosterPath),
            Fanart = ResolveImageUrl(result.FanartPath),
            Backdrop = ResolveImageUrl(result.BackdropPath)
        };
    }

    private async Task<ExternalMetadataResult?> SearchTvdbAsync(
        string query,
        SearchType searchType,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var token = await GetTvdbTokenAsync(apiKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var type = searchType == SearchType.Movie ? "movie" : "series";
        var uri = new Uri($"{TvdbApiBaseUrl}/search?query={Uri.EscapeDataString(query)}&type={type}");

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        var response = await ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(response))
        {
            return null;
        }

        using var json = JsonDocument.Parse(response);
        var entries = json.RootElement.TryGetProperty("data", out var data)
            ? data
            : (json.RootElement.TryGetProperty("results", out var results) ? results : default);

        if (entries.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in entries.EnumerateArray())
        {
            var id = FindStringProperty(item, "id")
                ?? FindStringProperty(item, "objectID")
                ?? FindStringProperty(item.GetProperty("object"), "id")
                ?? FindStringProperty(item.GetProperty("object"), "tvdb_id")
                ?? FindStringProperty(item.GetProperty("object"), "tvdbId");

            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var poster = FindStringProperty(item, "image")
                ?? FindStringProperty(item.GetProperty("object"), "image")
                ?? FindStringProperty(item.GetProperty("object"), "image_url");

            var fanart = FindStringProperty(item, "fanart")
                ?? FindStringProperty(item.GetProperty("object"), "fanart")
                ?? FindStringProperty(item.GetProperty("object"), "fanart_url");

            var backdrop = FindStringProperty(item, "backdrop")
                ?? FindStringProperty(item.GetProperty("object"), "backdrop")
                ?? FindStringProperty(item.GetProperty("object"), "backdrop_url");

            return new ExternalMetadataResult
            {
                Source = "tvdb",
                TvdbId = id,
                Poster = ResolveImageUrl(poster),
                Fanart = ResolveImageUrl(fanart),
                Backdrop = ResolveImageUrl(backdrop)
            };
        }

        return null;
    }

    private async Task<string?> GetTvdbTokenAsync(string apiKey, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (!string.IsNullOrWhiteSpace(_cachedTvdbToken) && (now - _cachedTvdbTokenAt).TotalMinutes < 55)
        {
            return _cachedTvdbToken;
        }

        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, new Uri($"{TvdbApiBaseUrl}/login"));
        loginRequest.Headers.TryAddWithoutValidation("Accept", "application/json");
        loginRequest.Content = new StringContent(
            JsonSerializer.Serialize(new TvdbLoginRequest(apiKey), JsonOptions),
            Encoding.UTF8,
            "application/json");

        var loginResponse = await ExecuteAsync(loginRequest, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(loginResponse))
        {
            return null;
        }

        using var json = JsonDocument.Parse(loginResponse);
        if (!json.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("token", out var tokenElement)
            || tokenElement.ValueKind is not JsonValueKind.String)
        {
            return null;
        }

        var token = tokenElement.GetString();
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        _cachedTvdbToken = token;
        _cachedTvdbTokenAt = now;
        return token;
    }

    private async Task<string?> ExecuteGetAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        return await ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> ExecuteAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(nameof(MetadataEnrichmentService));

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(15));

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutSource.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadAsStringAsync(timeoutSource.Token).ConfigureAwait(false);
    }

    private static string? ResolveImageUrl(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        if (Uri.TryCreate(path, UriKind.Absolute, out _))
        {
            return path;
        }

        return new Uri(new Uri(TmdbImageBaseUrl), path.TrimStart('/')).ToString();
    }

    private static string? FindStringProperty(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null
        };
    }

    private static bool IsTransientMetadataFailure(Exception exception)
        => exception is HttpRequestException || exception is TaskCanceledException;
}

internal sealed record ExternalMetadataResult
{
    public string? Source { get; init; }
    public string? TmdbId { get; init; }
    public string? TvdbId { get; init; }
    public string? Poster { get; init; }
    public string? Fanart { get; init; }
    public string? Backdrop { get; init; }
}

internal sealed class TmdbSearchResponse
{
    [JsonPropertyName("results")]
    public List<TmdbSearchItem> Results { get; set; } = [];
}

internal sealed class TmdbSearchItem
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("backdrop_path")]
    public string? BackdropPath { get; set; }

    [JsonPropertyName("profile_path")]
    public string? FanartPath { get; set; }
}

internal sealed record TvdbLoginRequest([property: JsonPropertyName("apikey")] string ApiKey);

internal enum SearchType
{
    Movie,
    Series
}
