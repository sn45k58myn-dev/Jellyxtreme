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
public sealed class JellyxtremeController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILoggerFactory _loggerFactory;

    public JellyxtremeController(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    {
        _httpClientFactory = httpClientFactory;
        _loggerFactory = loggerFactory;
    }

    [HttpPost("TestConnection")]
    public async Task<ActionResult<XtreamConnectionResult>> TestConnection(
        [FromBody] XtreamConnectionRequest request,
        CancellationToken cancellationToken)
    {
        if (!XtreamApiClient.TryNormalizeServerUrl(request.ServerUrl, out _))
        {
            return BadRequest(new XtreamConnectionResult(false, "Server URL must be an absolute http or https URL."));
        }

        var client = new XtreamApiClient(
            _httpClientFactory,
            _loggerFactory.CreateLogger<XtreamApiClient>(),
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
        var config = new PluginConfiguration
        {
            ServerUrl = request.ServerUrl,
            Username = request.Username,
            Password = request.Password
        };

        var service = new XtreamCacheRefreshService(
            _httpClientFactory,
            new XtreamCacheService(_loggerFactory.CreateLogger<XtreamCacheService>()),
            _loggerFactory);

        return Ok(await service.GetCategoriesAsync(config, cancellationToken).ConfigureAwait(false));
    }

    [HttpGet("CacheSummary")]
    public async Task<ActionResult<XtreamCacheSummary>> GetCacheSummary(CancellationToken cancellationToken)
    {
        var cache = new XtreamCacheService(_loggerFactory.CreateLogger<XtreamCacheService>());
        return Ok(await cache.GetSummaryAsync(cancellationToken).ConfigureAwait(false));
    }
}

public sealed record XtreamConnectionRequest(string ServerUrl, string Username, string Password);
