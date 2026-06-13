using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyxtreme.Cache;

public sealed class XtreamCacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly ILogger<XtreamCacheService> _logger;
    private readonly string? _cacheDirectory;

    public XtreamCacheService(ILogger<XtreamCacheService> logger)
        : this(logger, null)
    {
    }

    public XtreamCacheService(ILogger<XtreamCacheService> logger, string? cacheDirectory)
    {
        _logger = logger;
        _cacheDirectory = cacheDirectory;
    }

    public async Task<XtreamCacheDocument> LoadAsync(CancellationToken cancellationToken)
    {
        var path = GetCachePath();
        if (!File.Exists(path))
        {
            return new XtreamCacheDocument();
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<XtreamCacheDocument>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? new XtreamCacheDocument();
    }

    public async Task SaveAsync(XtreamCacheDocument document, CancellationToken cancellationToken)
    {
        var path = GetCachePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("JellyXtreme cache refreshed with {LiveCount} live channels, {VodCount} VOD items, and {SeriesCount} series.",
            document.LiveChannels.Count,
            document.VodItems.Count,
            document.SeriesItems.Count);
    }

    public async Task<XtreamCacheSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var document = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return new XtreamCacheSummary(
            document.RefreshedAt,
            Plugin.Instance?.Configuration.LastSuccessfulSyncUtc,
            document.LiveCategories.Count,
            document.VodCategories.Count,
            document.SeriesCategories.Count,
            document.LiveChannels.Count,
            document.VodItems.Count,
            document.SeriesItems.Count,
            document.SeriesItems.Sum(series => series.Seasons.Sum(season => season.Episodes.Count)),
            document.XmlTv is not null && File.Exists(GetXmlTvPath()));
    }

    public async Task<XtreamCategoryCache> GetCategoryCacheAsync(CancellationToken cancellationToken)
    {
        var document = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return new XtreamCategoryCache(document.LiveCategories, document.VodCategories, document.SeriesCategories);
    }

    private string GetCachePath()
        => Path.Combine(GetCacheDirectory(), "xtream-cache.json");

    public string GetXmlTvPath()
        => Path.Combine(GetCacheDirectory(), "xmltv.xml");

    private string GetCacheDirectory()
    {
        var dataPath = _cacheDirectory ?? Plugin.Instance?.DataFolderPath;
        if (string.IsNullOrWhiteSpace(dataPath))
        {
            dataPath = Path.Combine(AppContext.BaseDirectory, "Jellyxtreme");
        }

        return dataPath;
    }
}
