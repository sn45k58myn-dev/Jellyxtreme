using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyxtreme.Cache;

public sealed class XtreamCacheStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly ILogger<XtreamCacheStore> _logger;

    public XtreamCacheStore(ILogger<XtreamCacheStore> logger)
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
            document.VodMovies.Count,
            document.Series.Count);
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
