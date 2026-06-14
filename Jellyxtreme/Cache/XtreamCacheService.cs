using System.Text.Json;
using Jellyxtreme.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyxtreme.Cache;

public sealed class XtreamCacheService
{
    private const int CurrentCacheVersion = 2;
    private const string MetadataImageSubDirectory = "Metadata";
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
        document.CacheVersion = CurrentCacheVersion;
        document.LastFailureReason = null;
        document.LastFailureUtc = null;
        await PersistCacheDocumentAsync(document, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("JellyXtreme cache refreshed with {LiveCount} live channels, {VodCount} VOD items, and {SeriesCount} series.",
            document.LiveChannels.Count,
            document.VodItems.Count,
            document.SeriesItems.Count);
    }

    public async Task MarkRefreshFailureAsync(string? reason, CancellationToken cancellationToken)
    {
        var document = await LoadAsync(cancellationToken).ConfigureAwait(false);
        document.LastFailureReason = string.IsNullOrWhiteSpace(reason) ? "Unknown error." : reason;
        document.LastFailureUtc = DateTimeOffset.UtcNow;
        await PersistCacheDocumentAsync(document, cancellationToken).ConfigureAwait(false);
    }

    public async Task RollbackToBackupAsync(CancellationToken cancellationToken)
    {
        var path = GetCachePath();
        var backupPath = GetBackupCachePath();
        if (!File.Exists(backupPath))
        {
            return;
        }

        var tempPath = path + ".rollback";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var source = File.OpenRead(backupPath))
            await using (var target = File.Create(tempPath))
            {
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }

            if (File.Exists(path))
            {
                File.Replace(tempPath, path, null);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private async Task PersistCacheDocumentAsync(XtreamCacheDocument document, CancellationToken cancellationToken)
    {
        var path = GetCachePath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
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
    }

    public async Task<XtreamCacheSummary> GetSummaryAsync(CancellationToken cancellationToken)
    {
        var document = await LoadAsync(cancellationToken).ConfigureAwait(false);
        var xmlTv = document.XmlTv;
        var xmlTvPath = GetXmlTvPath();
        var hasXmlTv = xmlTv is not null || File.Exists(xmlTvPath) || File.Exists(xmlTvPath + ".gz");
        var lastXmlTvRefreshUtc = xmlTv?.RefreshedAt;
        var guideAgeMs = xmlTv?.RefreshedAt is DateTimeOffset refreshedAt && refreshedAt != default
            ? (long)(DateTimeOffset.UtcNow - refreshedAt).TotalMilliseconds
            : null;

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
            hasXmlTv,
            lastXmlTvRefreshUtc,
            guideAgeMs,
            xmlTv?.ChannelReferenceCount ?? 0,
            xmlTv?.MissingChannelCount ?? 0,
            xmlTv?.ProgramCount ?? 0,
            Plugin.Instance?.Configuration.LastSyncDurationMs,
            Plugin.Instance?.Configuration.LastSyncError,
            document.LastFailureReason,
            document.LastFailureUtc);
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

    public string GetMetadataImagePath(string fileName)
        => Path.Combine(GetMetadataImageDirectory(), fileName);

    public string GetMetadataImageDirectory()
        => Path.Combine(GetCacheDirectory(), "metadata", MetadataImageSubDirectory);

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

        foreach (var category in document.LiveCategories)
        {
            if (string.IsNullOrWhiteSpace(category.ProviderId))
            {
                category.ProviderId = PluginConfiguration.NormalizeProviderId(string.Empty);
            }
        }

        foreach (var category in document.VodCategories)
        {
            if (string.IsNullOrWhiteSpace(category.ProviderId))
            {
                category.ProviderId = PluginConfiguration.NormalizeProviderId(string.Empty);
            }
        }

        foreach (var category in document.SeriesCategories)
        {
            if (string.IsNullOrWhiteSpace(category.ProviderId))
            {
                category.ProviderId = PluginConfiguration.NormalizeProviderId(string.Empty);
            }
        }

        foreach (var channel in document.LiveChannels)
        {
            if (string.IsNullOrWhiteSpace(channel.ProviderId))
            {
                channel.ProviderId = PluginConfiguration.NormalizeProviderId(string.Empty);
            }
        }

        foreach (var item in document.VodItems)
        {
            if (string.IsNullOrWhiteSpace(item.ProviderId))
            {
                item.ProviderId = PluginConfiguration.NormalizeProviderId(string.Empty);
            }
        }

        foreach (var item in document.SeriesItems)
        {
            if (string.IsNullOrWhiteSpace(item.ProviderId))
            {
                item.ProviderId = PluginConfiguration.NormalizeProviderId(string.Empty);
            }

            foreach (var season in item.Seasons)
            {
                foreach (var episode in season.Episodes)
                {
                    if (string.IsNullOrWhiteSpace(episode.ProviderId))
                    {
                        episode.ProviderId = item.ProviderId;
                    }
                }
            }
        }

        return document;
    }
}
