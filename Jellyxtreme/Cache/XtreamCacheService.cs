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
        _logger.LogInformation("JellyXtreme cache refreshed with {LiveCount} live channels, {VodCount} VOD movies, and {SeriesCount} series.",
            document.LiveChannels.Count,
            document.VodItems.Count,
            document.SeriesItems.Count);
    }

    public async Task<XtreamCacheSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var document = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return new XtreamCacheSummary(
            document.RefreshedAt,
            document.LiveCategories.Count,
            document.VodCategories.Count,
            document.SeriesCategories.Count,
            document.LiveChannels.Count,
            document.VodItems.Count,
            document.SeriesItems.Count,
            document.SeriesItems.Sum(series => series.Seasons.Sum(season => season.Episodes.Count)));
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
}
