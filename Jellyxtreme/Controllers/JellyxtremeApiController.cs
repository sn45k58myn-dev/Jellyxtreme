using Jellyxtreme.Api;
using Jellyxtreme.Cache;
using Jellyxtreme.Configuration;
using Jellyxtreme.Providers;
using Jellyxtreme.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MediaBrowser.Model.Dto;
using System.Net;

namespace Jellyxtreme.Controllers;

[ApiController]
[Authorize]
[Route("Jellyxtreme")]
public sealed class JellyxtremeApiController : ControllerBase
{
    private readonly XtreamApiClient _apiClient;
    private readonly XtreamCacheRefreshService _cacheRefreshService;
    private readonly XtreamCacheService _cacheService;
    private readonly VodProvider _vodProvider;
    private readonly SeriesProvider _seriesProvider;
    private readonly StreamResolverService _streamResolver;
    private readonly ICredentialStore _credentialStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<JellyxtremeApiController> _logger;

    public JellyxtremeApiController(
        XtreamApiClient apiClient,
        XtreamCacheRefreshService cacheRefreshService,
        XtreamCacheService cacheService,
        VodProvider vodProvider,
        SeriesProvider seriesProvider,
        StreamResolverService streamResolver,
        ICredentialStore credentialStore,
        IHttpClientFactory httpClientFactory,
        ILogger<JellyxtremeApiController> logger)
    {
        _apiClient = apiClient;
        _cacheRefreshService = cacheRefreshService;
        _cacheService = cacheService;
        _vodProvider = vodProvider;
        _seriesProvider = seriesProvider;
        _streamResolver = streamResolver;
        _credentialStore = credentialStore;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [HttpPost("TestConnection")]
    public async Task<ActionResult<XtreamConnectionResult>> TestConnection(
        [FromBody] XtreamConnectionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryBuildSettings(request, out var settings, out var validationMessage))
        {
            return BadRequest(new XtreamConnectionResult(false, validationMessage));
        }

        return Ok(await _apiClient.TestConnectionAsync(settings, cancellationToken).ConfigureAwait(false));
    }

    [HttpPost("Categories")]
    public async Task<ActionResult<XtreamCategorySnapshot>> GetCategories(
        [FromBody] XtreamConnectionRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryBuildSettings(request, out var settings, out var validationMessage))
        {
            return BadRequest(new { Message = validationMessage });
        }

        if (HasInlineCredentials(request) || !string.IsNullOrWhiteSpace(request.ProviderId))
        {
            return Ok(await GetProviderCategorySnapshotAsync(
                settings,
                XtreamCacheIdentity.NormalizeProviderId(request.ProviderId),
                cancellationToken).ConfigureAwait(false));
        }

        return Ok(await _cacheRefreshService.GetCategoriesAsync(Plugin.Instance?.Configuration ?? new PluginConfiguration(), cancellationToken).ConfigureAwait(false));
    }

    [HttpGet("Categories")]
    public async Task<ActionResult<XtreamCategoryCache>> GetCachedCategories(CancellationToken cancellationToken)
    {
        return Ok(await _cacheService.GetCategoryCacheAsync(cancellationToken).ConfigureAwait(false));
    }

    [HttpGet("CacheSummary")]
    public async Task<ActionResult<XtreamCacheSummary>> GetCacheSummary(CancellationToken cancellationToken)
    {
        return Ok(await _cacheService.GetSummaryAsync(cancellationToken).ConfigureAwait(false));
    }

    [AllowAnonymous]
    [HttpGet("Live/{streamId:int}.{extension}")]
    [HttpGet("Live/{providerId}/{streamId:int}.{extension}")]
    public async Task<IActionResult> ProxyLiveStream(
        [FromRoute] string? providerId,
        [FromRoute] int streamId,
        [FromRoute] string extension,
        CancellationToken cancellationToken)
    {
        if (!IsLoopbackRequest(HttpContext.Connection.RemoteIpAddress))
        {
            _logger.LogWarning("Rejected non-local JellyXtreme live proxy request for stream {StreamId}.", streamId);
            return NotFound();
        }

        if (streamId <= 0)
        {
            return BadRequest(new { Message = "Stream ID must be greater than zero." });
        }

        if (string.IsNullOrWhiteSpace(extension))
        {
            return BadRequest(new { Message = "Stream extension is required." });
        }

        var cache = await _cacheService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var channel = cache.LiveChannels.FirstOrDefault(item =>
            item.StreamId == streamId
            && (string.IsNullOrWhiteSpace(providerId)
                || string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)));
        if (channel is null)
        {
            return NotFound();
        }

        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        string streamUrl;
        try
        {
            streamUrl = _streamResolver.ResolveLiveUrl(config, channel.ProviderId, channel.StreamId, channel.StreamExtension);
        }
        catch (XtreamValidationException exception)
        {
            _logger.LogWarning(
                "JellyXtreme live proxy rejected invalid playback configuration for provider/stream {ProviderId}/{StreamId}: {Message}",
                channel.ProviderId,
                streamId,
                exception.Message);
            return BadRequest(new { Message = "Live playback configuration is invalid." });
        }

        try
        {
            var client = _httpClientFactory.CreateClient(nameof(JellyxtremeApiController));
            var upstreamRequest = new HttpRequestMessage(HttpMethod.Get, streamUrl);
            ApplyStreamRequestHeaders(upstreamRequest);
            var upstreamResponse = await client.SendAsync(
                upstreamRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (!upstreamResponse.IsSuccessStatusCode)
            {
                upstreamResponse.Dispose();
                _logger.LogWarning(
                    "JellyXtreme live proxy upstream request failed for provider/stream {ProviderId}/{StreamId} with status {StatusCode}.",
                    channel.ProviderId,
                    streamId,
                    (int)upstreamResponse.StatusCode);
                return StatusCode(StatusCodes.Status502BadGateway, new { Message = "Unable to open upstream live stream." });
            }

            HttpContext.Response.RegisterForDispose(upstreamResponse);
            var contentType = string.Equals(extension, "m3u8", StringComparison.OrdinalIgnoreCase)
                ? "application/vnd.apple.mpegurl"
                : "video/MP2T";
            var stream = await upstreamResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return File(stream, contentType, enableRangeProcessing: false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogWarning("JellyXtreme live proxy could not open upstream stream {ProviderId}/{StreamId}.", providerId, streamId);
            return StatusCode(StatusCodes.Status502BadGateway, new { Message = "Unable to open upstream live stream." });
        }
    }

    private static void ApplyStreamRequestHeaders(HttpRequestMessage request)
    {
        request.Headers.UserAgent.TryParseAdd("VLC/3.0.20 LibVLC/3.0.20");
        request.Headers.Accept.TryParseAdd("*/*");
    }

    private static bool IsLoopbackRequest(IPAddress? remoteIpAddress)
    {
        if (remoteIpAddress is null)
        {
            return false;
        }

        if (IPAddress.IsLoopback(remoteIpAddress))
        {
            return true;
        }

        return remoteIpAddress.IsIPv4MappedToIPv6
            && IPAddress.IsLoopback(remoteIpAddress.MapToIPv4());
    }

    [HttpGet("Vod")]
    public async Task<ActionResult<PagedResult<VodItemInfo>>> GetVodItems(
        [FromQuery] int startIndex,
        [FromQuery] int limit,
        CancellationToken cancellationToken)
    {
        var items = await _vodProvider.GetItemsAsync(cancellationToken).ConfigureAwait(false);
        return Ok(Page(items, startIndex, limit));
    }

    [HttpGet("Vod/{streamId:int}/MediaSources")]
    public async Task<ActionResult<IReadOnlyList<MediaSourceInfo>>> GetVodMediaSources(
        [FromRoute] int streamId,
        [FromQuery] bool includePlaybackUrl,
        [FromQuery] string? providerId,
        CancellationToken cancellationToken)
    {
        if (streamId <= 0)
        {
            return BadRequest(new { Message = "Stream ID must be greater than zero." });
        }

        if (!includePlaybackUrl)
        {
            return BadRequest(new { Message = "Set includePlaybackUrl=true to explicitly request authenticated playback URLs." });
        }

        var mediaSources = await _vodProvider.GetMediaSourcesAsync(streamId, providerId, cancellationToken).ConfigureAwait(false);
        return mediaSources.Count == 0 ? NotFound() : Ok(mediaSources);
    }

    [HttpGet("Series")]
    public async Task<ActionResult<PagedResult<SeriesItemInfo>>> GetSeriesItems(
        [FromQuery] int startIndex,
        [FromQuery] int limit,
        CancellationToken cancellationToken)
    {
        var items = await _seriesProvider.GetSeriesInfosAsync(cancellationToken).ConfigureAwait(false);
        return Ok(Page(items, startIndex, limit));
    }

    [HttpGet("Series/{seriesId:int}/Episodes")]
    public async Task<ActionResult<PagedResult<SeriesEpisodeInfo>>> GetSeriesEpisodes(
        [FromRoute] int seriesId,
        [FromQuery] int startIndex,
        [FromQuery] int limit,
        [FromQuery] string? providerId,
        CancellationToken cancellationToken)
    {
        if (seriesId <= 0)
        {
            return BadRequest(new { Message = "Series ID must be greater than zero." });
        }

        var episodes = await _seriesProvider.GetEpisodesAsync(seriesId, providerId, cancellationToken).ConfigureAwait(false);
        return Ok(Page(episodes, startIndex, limit));
    }

    [HttpGet("Series/Episodes/{episodeStreamId:int}/MediaSources")]
    public async Task<ActionResult<IReadOnlyList<MediaSourceInfo>>> GetSeriesEpisodeMediaSources(
        [FromRoute] int episodeStreamId,
        [FromQuery] bool includePlaybackUrl,
        [FromQuery] string? providerId,
        CancellationToken cancellationToken)
    {
        if (episodeStreamId <= 0)
        {
            return BadRequest(new { Message = "Episode stream ID must be greater than zero." });
        }

        if (!includePlaybackUrl)
        {
            return BadRequest(new { Message = "Set includePlaybackUrl=true to explicitly request authenticated playback URLs." });
        }

        var mediaSources = await _seriesProvider.GetEpisodeMediaSourcesAsync(episodeStreamId, providerId, cancellationToken).ConfigureAwait(false);
        return mediaSources.Count == 0 ? NotFound() : Ok(mediaSources);
    }

    private static PagedResult<T> Page<T>(IReadOnlyList<T> items, int startIndex, int limit)
    {
        var safeStart = Math.Max(0, startIndex);
        var safeLimit = limit <= 0 ? 100 : Math.Min(limit, 500);
        return new PagedResult<T>(
            items.Skip(safeStart).Take(safeLimit).ToList(),
            items.Count,
            safeStart,
            safeLimit);
    }

    private async Task<XtreamCategorySnapshot> GetProviderCategorySnapshotAsync(
        XtreamConnectionSettings settings,
        string providerId,
        CancellationToken cancellationToken)
    {
        var providerKey = XtreamCacheIdentity.NormalizeProviderId(providerId);
        var liveCategories = await _apiClient.GetLiveCategoriesAsync(settings, cancellationToken).ConfigureAwait(false);
        var vodCategories = await _apiClient.GetVodCategoriesAsync(settings, cancellationToken).ConfigureAwait(false);
        var seriesCategories = await _apiClient.GetSeriesCategoriesAsync(settings, cancellationToken).ConfigureAwait(false);

        var seenLive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenVod = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenSeries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return new XtreamCategorySnapshot(
            liveCategories
                .Where(category => seenLive.Add(XtreamCacheIdentity.BuildItemKey(providerKey, category.CategoryId)))
                .Select(category => new CachedCategory
                {
                    ProviderId = providerKey,
                    CategoryId = category.CategoryId,
                    Name = category.CategoryName,
                    Kind = "live"
                })
                .ToList(),
            vodCategories
                .Where(category => seenVod.Add(XtreamCacheIdentity.BuildItemKey(providerKey, category.CategoryId)))
                .Select(category => new CachedCategory
                {
                    ProviderId = providerKey,
                    CategoryId = category.CategoryId,
                    Name = category.CategoryName,
                    Kind = "vod"
                }).ToList(),
            seriesCategories
                .Where(category => seenSeries.Add(XtreamCacheIdentity.BuildItemKey(providerKey, category.CategoryId)))
                .Select(category => new CachedCategory
                {
                    ProviderId = providerKey,
                    CategoryId = category.CategoryId,
                    Name = category.CategoryName,
                    Kind = "series"
                }).ToList());
    }

    private static bool HasInlineCredentials(XtreamConnectionRequest? request)
    {
        if (request is null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(request.ServerUrl)
            || !string.IsNullOrWhiteSpace(request.Username)
            || !string.IsNullOrWhiteSpace(request.Password);
    }

    private bool TryBuildSettings(
        XtreamConnectionRequest? request,
        out XtreamConnectionSettings settings,
        out string validationMessage)
    {
        settings = new XtreamConnectionSettings(string.Empty, string.Empty, string.Empty, TimeSpan.FromMinutes(1));
        validationMessage = string.Empty;

        if (request is null)
        {
            validationMessage = "Connection details are required.";
            return false;
        }

        var providerConfig = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var requestServerUrl = request.ServerUrl?.Trim() ?? string.Empty;
        var requestUsername = request.Username?.Trim() ?? string.Empty;
        var requestPassword = request.Password ?? string.Empty;
        var hasRequestCredentials = !string.IsNullOrWhiteSpace(requestServerUrl)
            || !string.IsNullOrWhiteSpace(requestUsername)
            || !string.IsNullOrWhiteSpace(requestPassword);

        if (hasRequestCredentials)
        {
            if (!XtreamApiClient.TryNormalizeServerUrl(requestServerUrl, out _))
            {
                validationMessage = "Server URL must be an absolute http or https URL.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(requestUsername) || string.IsNullOrWhiteSpace(requestPassword))
            {
                validationMessage = "Username and password are required.";
                return false;
            }

            settings = XtreamConnectionSettings.FromRequest(requestServerUrl, requestUsername, requestPassword);
            return true;
        }

        if (!_credentialStore.TryGetConnectionSettings(
            providerConfig,
            XtreamCacheIdentity.NormalizeProviderId(request.ProviderId),
            out settings))
        {
            validationMessage = string.IsNullOrWhiteSpace(request.ProviderId)
                ? "Provider credentials are required."
                : $"Configured provider not found: {request.ProviderId}.";
            return false;
        }

        return true;
    }
}

public sealed record XtreamConnectionRequest(string ServerUrl, string Username, string Password, string? ProviderId = null);

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int StartIndex,
    int Limit);
