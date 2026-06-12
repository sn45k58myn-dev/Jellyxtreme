using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyxtreme.Cache;

public sealed class XtreamCacheService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly ILogger<XtreamCacheService> _logger;

    public XtreamCacheService(ILogger<XtreamCacheService> logger)
    {
        _logger = logger;
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

    public async Task SaveXmlTvAsync(string xmlTv, CancellationToken cancellationToken)
    {
        var path = GetXmlTvPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, xmlTv, cancellationToken).ConfigureAwait(false);
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

    private static string GetCachePath()
    {
        var dataPath = Plugin.Instance?.DataFolderPath;
        if (string.IsNullOrWhiteSpace(dataPath))
        {
            dataPath = Path.Combine(AppContext.BaseDirectory, "Jellyxtreme");
        }

        return Path.Combine(dataPath, "xtream-cache.json");
    }

    private static string GetXmlTvPath()
    {
        var dataPath = Plugin.Instance?.DataFolderPath;
        if (string.IsNullOrWhiteSpace(dataPath))
        {
            dataPath = Path.Combine(AppContext.BaseDirectory, "Jellyxtreme");
        }

        return Path.Combine(dataPath, "xmltv.xml");
    }
}
