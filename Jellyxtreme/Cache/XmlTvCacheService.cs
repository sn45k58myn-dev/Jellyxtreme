using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Jellyxtreme.Api;
using Microsoft.Extensions.Logging;

namespace Jellyxtreme.Cache;

public sealed class XmlTvCacheService
{
    private const string XmlTvFileName = "xmltv.xml";
    private const string XmlTvCompressedFileName = "xmltv.xml.gz";
    private const string XmlTvMetadataFileName = "xmltv.meta.json";
    private static readonly TimeSpan XmlTvCacheTtl = TimeSpan.FromHours(12);
    private static readonly JsonSerializerOptions MetaJsonOptions = new() { WriteIndented = true };

    private readonly XtreamApiClient _apiClient;
    private readonly XtreamCacheService _cacheService;
    private readonly ILogger<XmlTvCacheService> _logger;

    public XmlTvCacheService(
        XtreamApiClient apiClient,
        XtreamCacheService cacheService,
        ILogger<XmlTvCacheService> logger)
    {
        _apiClient = apiClient;
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task<XmlTvCacheInfo?> DownloadAndCacheAsync(
        XtreamConnectionSettings settings,
        IReadOnlyCollection<CachedLiveChannel> channels,
        CancellationToken cancellationToken)
    {
        if (channels.Count == 0)
        {
            _logger.LogInformation("JellyXtreme XMLTV cache skipped because no cached live channels are available.");
            return null;
        }

        var metadataPath = GetXmlTvMetadataPath();
        try
        {
            var metadata = await ReadMetadataAsync(metadataPath, cancellationToken).ConfigureAwait(false)
                ?? new XmlTvCacheMetadata();

            if (metadata.LastFetchedUtc.HasValue
                && !IsExpired(metadata.LastFetchedUtc.Value)
                && GetActiveXmlTvPath() is not null)
            {
                var cachedGuides = await LoadGuideDataAsync(channels, cancellationToken).ConfigureAwait(false);
                return CreateCacheInfo(metadata, cachedGuides, isIncremental: false);
            }

            var now = DateTimeOffset.UtcNow;
            var response = await _apiClient.GetXmlTvResponseAsync(
                settings,
                metadata.ETag,
                metadata.LastModified,
                cancellationToken).ConfigureAwait(false);

            metadata.LastRefreshAttemptUtc = now;

            if (response.IsNotModified)
            {
                _logger.LogInformation("JellyXtreme XMLTV cache is unchanged; keeping previous cache file.");
                metadata.LastFetchedUtc = now;
                await WriteMetadataAsync(metadataPath, metadata, cancellationToken).ConfigureAwait(false);
                return await GetOrBuildFromExistingCacheAsync(metadata, channels, cancellationToken).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(response.XmlContent))
            {
                _logger.LogWarning("JellyXtreme XMLTV endpoint returned an empty response.");
                return await GetOrBuildFromExistingCacheAsync(metadata, channels, cancellationToken).ConfigureAwait(false);
            }

            var guideData = Parse(response.XmlContent, channels);
            if (guideData.MissingChannelIds.Count > 0)
            {
                _logger.LogWarning(
                    "JellyXtreme XMLTV cache has {MissingCount} selected live channels missing from provider guide data.",
                    guideData.MissingChannelIds.Count);
            }

            await SaveCompressedAsync(response.XmlContent, cancellationToken).ConfigureAwait(false);
            metadata.ETag = response.ETag;
            metadata.LastModified = response.LastModified;
            metadata.LastFetchedUtc = response.RetrievedAtUtc;
            metadata.ChannelReferenceCount = guideData.Channels.Count;
            metadata.MissingChannelCount = guideData.MissingChannelIds.Count;
            metadata.ProgramCount = guideData.Programs.Count;
            await WriteMetadataAsync(metadataPath, metadata, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "JellyXtreme XMLTV cache refreshed with {ChannelCount} mapped channels and {ProgramCount} valid programs (invalid: {InvalidProgramCount}, missing: {MissingChannelCount}).",
                guideData.Channels.Count,
                guideData.Programs.Count,
                guideData.InvalidProgramCount,
                guideData.MissingChannelIds.Count);

            return new XmlTvCacheInfo
            {
                RefreshedAt = response.RetrievedAtUtc,
                LastRefreshAttemptUtc = now,
                FileName = XmlTvCompressedFileName,
                ChannelReferenceCount = guideData.Channels.Count,
                MissingChannelCount = guideData.MissingChannelIds.Count,
                ProgramCount = guideData.Programs.Count,
                IsCompressed = true
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "JellyXtreme XMLTV cache refresh request failed.");
            var fallbackMetadata = await ReadMetadataAsync(metadataPath, cancellationToken).ConfigureAwait(false)
                ?? new XmlTvCacheMetadata();
            return await GetOrBuildFromExistingCacheAsync(fallbackMetadata, channels, cancellationToken).ConfigureAwait(false);
        }
        catch (System.Xml.XmlException ex)
        {
            _logger.LogWarning(ex, "JellyXtreme XMLTV cache received malformed XML.");
            await WriteMetadataAsync(metadataPath, new XmlTvCacheMetadata(), cancellationToken).ConfigureAwait(false);
            return await GetOrBuildFromExistingCacheAsync(new XmlTvCacheMetadata(), channels, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<XmlTvGuideData> LoadGuideDataAsync(
        IReadOnlyCollection<CachedLiveChannel> channels,
        CancellationToken cancellationToken)
    {
        var path = GetActiveXmlTvPath();
        if (path is null)
        {
            return new XmlTvGuideData([], [], [], 0);
        }

        try
        {
            string xmlTv;
            if (string.Equals(Path.GetExtension(path), ".gz", StringComparison.OrdinalIgnoreCase))
            {
                await using (var fileStream = File.OpenRead(path))
                await using (var gzip = new GZipStream(fileStream, CompressionMode.Decompress))
                using (var reader = new StreamReader(gzip, Encoding.UTF8))
                {
                    xmlTv = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                xmlTv = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            }

            if (string.IsNullOrWhiteSpace(xmlTv))
            {
                return new XmlTvGuideData([], [], [], 0);
            }

            return Parse(xmlTv, channels);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            _logger.LogWarning(ex, "Unable to read JellyXtreme XMLTV cache file.");
            return new XmlTvGuideData([], [], [], 0);
        }
    }

    public static XmlTvGuideData Parse(string xmlTv, IReadOnlyCollection<CachedLiveChannel> channels)
    {
        if (string.IsNullOrWhiteSpace(xmlTv) || channels.Count == 0)
        {
            return new XmlTvGuideData([], [], [], 0);
        }

        var channelIds = channels
            .Select(channel => channel.EpgChannelId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var document = XDocument.Parse(xmlTv);
        var channelElements = document
            .Descendants("channel")
            .Select(channel => new
            {
                Id = (string?)channel.Attribute("id"),
                DisplayName = channel.Elements("display-name").Select(element => element.Value).FirstOrDefault()
            })
            .Where(channel => !string.IsNullOrWhiteSpace(channel.Id))
            .ToList();

        var mappedChannels = channelElements
            .Where(channel => channelIds.Contains(channel.Id!))
            .Select(channel => new XmlTvChannelMap(channel.Id!, channel.Id!, channel.DisplayName))
            .ToList();

        var mappedChannelIds = mappedChannels.Select(channel => channel.XmlTvChannelId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missingChannelIds = channelIds.Except(mappedChannelIds, StringComparer.OrdinalIgnoreCase).ToList();

        var programmes = document
            .Descendants("programme")
            .Select(program => ToProgram(program, mappedChannelIds))
            .Where(program => program is not null)
            .Cast<CachedXmlTvProgram>()
            .ToList();

        var totalProgramElementCount = document.Descendants("programme").Count();
        var invalidProgramCount = Math.Max(0, totalProgramElementCount - programmes.Count);

        return new XmlTvGuideData(mappedChannels, programmes, missingChannelIds, invalidProgramCount);
    }

    private static CachedXmlTvProgram? ToProgram(XElement program, ISet<string> mappedChannelIds)
    {
        var channelId = (string?)program.Attribute("channel");
        var start = (string?)program.Attribute("start");
        var stop = (string?)program.Attribute("stop");

        if (string.IsNullOrWhiteSpace(channelId)
            || !mappedChannelIds.Contains(channelId)
            || !TryParseXmlTvDate(start, out var startUtc)
            || !TryParseXmlTvDate(stop, out var endUtc)
            || startUtc >= endUtc)
        {
            return null;
        }

        var title = program.Elements("title").Select(element => element.Value).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(title))
        {
            title = "Untitled";
        }

        var description = program.Elements("desc").Select(element => element.Value).FirstOrDefault();
        var categories = program.Elements("category").Select(element => element.Value).Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
        var iconUrl = program.Elements("icon").Select(element => (string?)element.Attribute("src")).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        var episodeTitle = program.Elements("sub-title").Select(element => element.Value).FirstOrDefault();

        return new CachedXmlTvProgram(
            CreateProgramId(channelId, startUtc, title),
            channelId,
            title,
            description,
            startUtc,
            endUtc,
            categories,
            episodeTitle,
            iconUrl);
    }

    private static bool TryParseXmlTvDate(string? value, out DateTimeOffset date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        var spaceIndex = normalized.IndexOf(' ', StringComparison.Ordinal);
        var format = spaceIndex > 0 ? "yyyyMMddHHmmss zzz" : "yyyyMMddHHmmss";

        if (spaceIndex > 0 && normalized.Length >= spaceIndex + 6)
        {
            var offset = normalized[(spaceIndex + 1)..];
            if (offset.Length == 5 && (offset[0] == '+' || offset[0] == '-'))
            {
                normalized = normalized[..spaceIndex] + " " + offset[..3] + ":" + offset[3..];
            }
        }

        return DateTimeOffset.TryParseExact(
            normalized,
            format,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out date);
    }

    private static string CreateProgramId(string? channelId, DateTimeOffset startUtc, string title)
    {
        var input = $"{channelId}|{startUtc.UtcDateTime:O}|{title}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private async Task<XmlTvCacheInfo?> GetOrBuildFromExistingCacheAsync(
        XmlTvCacheMetadata metadata,
        IReadOnlyCollection<CachedLiveChannel> channels,
        CancellationToken cancellationToken)
    {
        var guideData = await LoadGuideDataAsync(channels, cancellationToken).ConfigureAwait(false);
        return CreateCacheInfo(metadata, guideData, isIncremental: true);
    }

    private XmlTvCacheInfo? CreateCacheInfo(XmlTvCacheMetadata metadata, XmlTvGuideData guideData, bool isIncremental)
    {
        if (guideData.Channels.Count == 0 && guideData.Programs.Count == 0 && !metadata.LastFetchedUtc.HasValue)
        {
            return null;
        }

        return new XmlTvCacheInfo
        {
            RefreshedAt = metadata.LastFetchedUtc ?? DateTimeOffset.UtcNow,
            LastRefreshAttemptUtc = metadata.LastRefreshAttemptUtc,
            FileName = isIncremental ? (File.Exists(GetXmlTvCompressedPath()) ? XmlTvCompressedFileName : XmlTvFileName) : XmlTvCompressedFileName,
            ChannelReferenceCount = metadata.ChannelReferenceCount ?? guideData.Channels.Count,
            MissingChannelCount = metadata.MissingChannelCount ?? guideData.MissingChannelIds.Count,
            ProgramCount = metadata.ProgramCount ?? guideData.Programs.Count,
            IsCompressed = File.Exists(GetXmlTvCompressedPath())
        };
    }

    private async Task SaveCompressedAsync(string xmlTv, CancellationToken cancellationToken)
    {
        var targetPath = GetXmlTvCompressedPath();
        Directory.CreateDirectory(GetXmlTvDirectory());

        var tempPath = targetPath + ".tmp";
        await using (var fileStream = File.Create(tempPath))
        await using (var gzip = new GZipStream(fileStream, CompressionLevel.Optimal, leaveOpen: false))
        using (var writer = new StreamWriter(gzip, Encoding.UTF8))
        {
            await writer.WriteAsync(xmlTv.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(targetPath))
        {
            File.Replace(tempPath, targetPath, targetPath + ".bak");
        }
        else
        {
            File.Move(tempPath, targetPath);
        }
    }

    private async Task<XmlTvCacheMetadata?> ReadMetadataAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<XmlTvCacheMetadata>(stream, MetaJsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "JellyXtreme XMLTV metadata cache could not be read.");
            return null;
        }
    }

    private async Task WriteMetadataAsync(string path, XmlTvCacheMetadata metadata, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(GetXmlTvDirectory());
        var tempPath = path + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, metadata, MetaJsonOptions, cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(path))
        {
            File.Replace(tempPath, path, path + ".bak");
        }
        else
        {
            File.Move(tempPath, path);
        }
    }

    private static bool IsExpired(DateTimeOffset lastFetchedUtc)
        => DateTimeOffset.UtcNow - lastFetchedUtc >= XmlTvCacheTtl;

    private string? GetActiveXmlTvPath()
    {
        var compressedPath = GetXmlTvCompressedPath();
        if (File.Exists(compressedPath))
        {
            return compressedPath;
        }

        var xmlPath = GetXmlTvPath();
        return File.Exists(xmlPath) ? xmlPath : null;
    }

    public string GetXmlTvPath()
        => Path.Combine(GetXmlTvDirectory(), XmlTvFileName);

    private string GetXmlTvCompressedPath()
        => Path.Combine(GetXmlTvDirectory(), XmlTvCompressedFileName);

    private string GetXmlTvMetadataPath()
        => Path.Combine(GetXmlTvDirectory(), XmlTvMetadataFileName);

    private string GetXmlTvDirectory()
    {
        var cachePath = _cacheService.GetXmlTvPath();
        var directory = Path.GetDirectoryName(cachePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            return directory;
        }

        return Path.Combine(AppContext.BaseDirectory, "Jellyxtreme");
    }
}
