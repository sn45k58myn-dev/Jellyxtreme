using Jellyxtreme.Api;
using Jellyxtreme.Cache;
using Jellyxtreme.Configuration;
using Jellyxtreme.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyxtreme.Controllers;

[ApiController]
[Authorize]
[Route("Jellyxtreme")]
public sealed class JellyxtremeApiController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly XtreamCacheRefreshService _cacheRefreshService;
    private readonly XtreamCacheService _cacheService;
    private readonly ILogger<XtreamApiClient> _apiLogger;

    public JellyxtremeApiController(
        IHttpClientFactory httpClientFactory,
        XtreamCacheRefreshService cacheRefreshService,
        XtreamCacheService cacheService,
        ILogger<XtreamApiClient> apiLogger)
    {
        _httpClientFactory = httpClientFactory;
        _cacheRefreshService = cacheRefreshService;
        _cacheService = cacheService;
        _apiLogger = apiLogger;
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

        var client = new XtreamApiClient(
            _httpClientFactory,
            _apiLogger,
            request.ServerUrl,
            request.Username,
            request.Password);

        return Ok(await client.TestConnectionAsync(cancellationToken).ConfigureAwait(false));
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
}

public sealed record XtreamConnectionRequest(string ServerUrl, string Username, string Password);
