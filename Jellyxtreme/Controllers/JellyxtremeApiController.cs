using Jellyxtreme.Api;
using Jellyxtreme.Cache;
using Jellyxtreme.Configuration;
using Jellyxtreme.Providers;
using Jellyxtreme.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MediaBrowser.Model.Dto;

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

    public JellyxtremeApiController(
        XtreamApiClient apiClient,
        XtreamCacheRefreshService cacheRefreshService,
        XtreamCacheService cacheService,
        VodProvider vodProvider,
        SeriesProvider seriesProvider)
    {
        _apiClient = apiClient;
        _cacheRefreshService = cacheRefreshService;
        _cacheService = cacheService;
        _vodProvider = vodProvider;
        _seriesProvider = seriesProvider;
    }

    [HttpPost("TestConnection")]
    public async Task<ActionResult<XtreamConnectionResult>> TestConnection(
        [FromBody] XtreamConnectionRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsValidRequest(request, out var validationMessage))
        {
            return BadRequest(new XtreamConnectionResult(false, validationMessage));
        }

        return Ok(await _apiClient.TestConnectionAsync(
            XtreamConnectionSettings.FromRequest(request.ServerUrl, request.Username, request.Password),
            cancellationToken).ConfigureAwait(false));
    }

    [HttpPost("Categories")]
    public async Task<ActionResult<XtreamCategorySnapshot>> GetCategories(
        [FromBody] XtreamConnectionRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsValidRequest(request, out var validationMessage))
        {
            return BadRequest(new { Message = validationMessage });
        }

        var config = new PluginConfiguration
        {
            ServerUrl = request.ServerUrl,
            Username = request.Username,
            Password = request.Password
        };

        return Ok(await _cacheRefreshService.GetCategoriesAsync(config, cancellationToken).ConfigureAwait(false));
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

        var mediaSources = await _vodProvider.GetMediaSourcesAsync(streamId, cancellationToken).ConfigureAwait(false);
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
        CancellationToken cancellationToken)
    {
        if (seriesId <= 0)
        {
            return BadRequest(new { Message = "Series ID must be greater than zero." });
        }

        var episodes = await _seriesProvider.GetEpisodesAsync(seriesId, cancellationToken).ConfigureAwait(false);
        return Ok(Page(episodes, startIndex, limit));
    }

    [HttpGet("Series/Episodes/{episodeStreamId:int}/MediaSources")]
    public async Task<ActionResult<IReadOnlyList<MediaSourceInfo>>> GetSeriesEpisodeMediaSources(
        [FromRoute] int episodeStreamId,
        [FromQuery] bool includePlaybackUrl,
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

        var mediaSources = await _seriesProvider.GetEpisodeMediaSourcesAsync(episodeStreamId, cancellationToken).ConfigureAwait(false);
        return mediaSources.Count == 0 ? NotFound() : Ok(mediaSources);
    }

    private static bool IsValidRequest(XtreamConnectionRequest? request, out string validationMessage)
    {
        if (request is null)
        {
            validationMessage = "Connection details are required.";
            return false;
        }

        if (!XtreamApiClient.TryNormalizeServerUrl(request.ServerUrl, out _))
        {
            validationMessage = "Server URL must be an absolute http or https URL.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            validationMessage = "Username and password are required.";
            return false;
        }

        validationMessage = string.Empty;
        return true;
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
}

public sealed record XtreamConnectionRequest(string ServerUrl, string Username, string Password);

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int StartIndex,
    int Limit);
