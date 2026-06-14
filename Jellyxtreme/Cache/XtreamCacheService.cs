using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyxtreme.Cache;

public sealed class XtreamCacheService
{
    private const int CurrentCacheVersion = 1;
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

        try
        {
            return NormalizeCacheDocument(await LoadFromPathAsync(path, cancellationToken).ConfigureAwait(false));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "JellyXtreme cache could not be loaded; trying backup cache.");
            var backupPath = GetBackupCachePath();
            if (!File.Exists(backupPath))
            {
                return new XtreamCacheDocument();
            }

            try
            {
                return NormalizeCacheDocument(await LoadFromPathAsync(backupPath, cancellationToken).ConfigureAwait(false));
            }
            catch (Exception backupEx) when (backupEx is JsonException or IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(backupEx, "JellyXtreme backup cache could not be loaded.");
                return new XtreamCacheDocument();
            }
        }
    }

    public async Task SaveAsync(XtreamCacheDocument document, CancellationToken cancellationToken)
    {
        var path = GetCachePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        document.CacheVersion = CurrentCacheVersion;

        var tempPath = path + ".tmp";
        var backupPath = GetBackupCachePath();
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(path))
        {
            File.Replace(tempPath, path, backupPath);
        }
        else
        {
            File.Move(tempPath, path);
        }

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
            document.CacheVersion,
            document.LiveChannels.Count,
            document.VodItems.Count,
            document.SeriesItems.Count,
            document.SeriesItems.Sum(series => series.Seasons.Sum(season => season.Episodes.Count)),
            document.XmlTv is not null && File.Exists(GetXmlTvPath()),
            Plugin.Instance?.Configuration.LastSyncDurationMs,
            Plugin.Instance?.Configuration.LastSyncError);
    }

    public async Task<XtreamCategoryCache> GetCategoryCacheAsync(CancellationToken cancellationToken)
    {
        var document = await LoadAsync(cancellationToken).ConfigureAwait(false);
        return new XtreamCategoryCache(document.LiveCategories, document.VodCategories, document.SeriesCategories);
    }

    private string GetCachePath()
        => Path.Combine(GetCacheDirectory(), "xtream-cache.json");

    private string GetBackupCachePath()
        => Path.Combine(GetCacheDirectory(), "xtream-cache.backup.json");

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

    private static async Task<XtreamCacheDocument?> LoadFromPathAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<XtreamCacheDocument>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static XtreamCacheDocument NormalizeCacheDocument(XtreamCacheDocument? document)
    {
        document ??= new XtreamCacheDocument();
        if (document.CacheVersion <= 0 || document.CacheVersion > CurrentCacheVersion)
        {
            document.CacheVersion = CurrentCacheVersion;
        }

        return document;
    }
}
