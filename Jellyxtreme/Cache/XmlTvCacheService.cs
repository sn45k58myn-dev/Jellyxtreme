using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Jellyxtreme.Api;
using Microsoft.Extensions.Logging;

namespace Jellyxtreme.Cache;

public sealed class XmlTvCacheService
{
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
        try
        {
            var xmlTv = await _apiClient.GetXmlTvAsync(settings, cancellationToken).ConfigureAwait(false);
            var guideData = Parse(xmlTv, channels);

            var path = _cacheService.GetXmlTvPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, xmlTv, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "JellyXtreme XMLTV cache refreshed with {ChannelCount} mapped channels and {ProgramCount} programs.",
                guideData.Channels.Count,
                guideData.Programs.Count);

            return new XmlTvCacheInfo
            {
                RefreshedAt = DateTimeOffset.UtcNow,
                ChannelReferenceCount = guideData.Channels.Count
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "JellyXtreme XMLTV cache skipped after a fetch or parse failure.");
            return null;
        }
    }

    public async Task<XmlTvGuideData> LoadGuideDataAsync(
        IReadOnlyCollection<CachedLiveChannel> channels,
        CancellationToken cancellationToken)
    {
        var path = _cacheService.GetXmlTvPath();
        if (!File.Exists(path))
        {
            return new XmlTvGuideData([], []);
        }

        var xmlTv = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        return Parse(xmlTv, channels);
    }

    public static XmlTvGuideData Parse(string xmlTv, IReadOnlyCollection<CachedLiveChannel> channels)
    {
        if (string.IsNullOrWhiteSpace(xmlTv) || channels.Count == 0)
        {
            return new XmlTvGuideData([], []);
        }

        var document = XDocument.Parse(xmlTv);
        var epgIds = channels
            .Select(channel => channel.EpgChannelId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var channelMaps = document
            .Descendants("channel")
            .Select(channel => new
            {
                Id = (string?)channel.Attribute("id"),
                DisplayName = channel.Elements("display-name").Select(element => element.Value).FirstOrDefault()
            })
            .Where(channel => !string.IsNullOrWhiteSpace(channel.Id) && epgIds.Contains(channel.Id))
            .Select(channel => new XmlTvChannelMap(channel.Id!, channel.Id!, channel.DisplayName))
            .ToList();

        var mappedIds = channelMaps.Select(channel => channel.XmlTvChannelId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var programs = document
            .Descendants("programme")
            .Select(program => ToProgram(program, mappedIds))
            .Where(program => program is not null)
            .Cast<CachedXmlTvProgram>()
            .ToList();

        return new XmlTvGuideData(channelMaps, programs);
    }

    private static CachedXmlTvProgram? ToProgram(XElement program, ISet<string> mappedChannelIds)
    {
        var channelId = (string?)program.Attribute("channel");
        var start = (string?)program.Attribute("start");
        var stop = (string?)program.Attribute("stop");

        if (string.IsNullOrWhiteSpace(channelId)
            || !mappedChannelIds.Contains(channelId)
            || !TryParseXmlTvDate(start, out var startUtc)
            || !TryParseXmlTvDate(stop, out var endUtc))
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

    private static string CreateProgramId(string channelId, DateTimeOffset startUtc, string title)
    {
        var input = $"{channelId}|{startUtc.UtcDateTime:O}|{title}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
