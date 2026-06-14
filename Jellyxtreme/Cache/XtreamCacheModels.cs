namespace Jellyxtreme.Cache;

public sealed class XtreamCacheDocument
{
    public int CacheVersion { get; set; } = 2;
    public DateTimeOffset RefreshedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? LastFailureReason { get; set; }
    public DateTimeOffset? LastFailureUtc { get; set; }
    public List<CachedCategory> LiveCategories { get; set; } = [];
    public List<CachedCategory> VodCategories { get; set; } = [];
    public List<CachedCategory> SeriesCategories { get; set; } = [];
    public List<CachedLiveChannel> LiveChannels { get; set; } = [];
    public List<CachedVodItem> VodItems { get; set; } = [];
    public List<CachedSeriesItem> SeriesItems { get; set; } = [];
    public XmlTvCacheInfo? XmlTv { get; set; }
}

public sealed class CachedCategory
{
    public string ProviderId { get; set; } = string.Empty;
    public string CategoryId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
}

public sealed class CachedLiveChannel
{
    public string ProviderId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int StreamId { get; set; }
    public string? Logo { get; set; }
    public string? EpgChannelId { get; set; }
    public string CategoryId { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string StreamExtension { get; set; } = "ts";
}

public sealed class CachedVodItem
{
    public string ProviderId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int StreamId { get; set; }
    public string CategoryId { get; set; } = string.Empty;
    public string? Poster { get; set; }
    public string? PosterImagePath { get; set; }
    public string? FanartImagePath { get; set; }
    public string? BackdropImagePath { get; set; }
    public double? Rating { get; set; }
    public string ContainerExtension { get; set; } = "mp4";
    public string? Added { get; set; }

    public string? MetadataSource { get; set; }
    public string? TmdbId { get; set; }
    public string? TvdbId { get; set; }
    public string? Fanart { get; set; }
    public string? Backdrop { get; set; }
}

public sealed class CachedSeriesItem
{
    public string ProviderId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SeriesId { get; set; }
    public string CategoryId { get; set; } = string.Empty;
    public string? Poster { get; set; }
    public string? PosterImagePath { get; set; }
    public string? FanartImagePath { get; set; }
    public string? BackdropImagePath { get; set; }
    public string? Plot { get; set; }
    public double? Rating { get; set; }
    public string? MetadataSource { get; set; }
    public string? TmdbId { get; set; }
    public string? TvdbId { get; set; }
    public string? Fanart { get; set; }
    public string? Backdrop { get; set; }
    public string? BaseFingerprint { get; set; }
    public string? EpisodeFingerprint { get; set; }
    public DateTimeOffset? LastInfoModifiedUtc { get; set; }
    public DateTimeOffset? LastInfoFetchedUtc { get; set; }
    public List<CachedSeason> Seasons { get; set; } = [];
}

public sealed class CachedSeason
{
    public int SeasonNumber { get; set; }
    public List<CachedEpisodeItem> Episodes { get; set; } = [];
}

public sealed class CachedEpisodeItem
{
    public string ProviderId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int StreamId { get; set; }
    public string? EpisodeNumber { get; set; }
    public string ContainerExtension { get; set; } = "mp4";
    public string? Poster { get; set; }
    public string? Plot { get; set; }
    public string? ReleaseDate { get; set; }
}

public sealed record XtreamCacheSummary(
    DateTimeOffset? RefreshedAt,
    DateTimeOffset? LastSuccessfulSyncUtc,
    int LiveCategoryCount,
    int VodCategoryCount,
    int SeriesCategoryCount,
    int CacheVersion,
    int LiveChannelCount,
    int VodItemCount,
    int SeriesItemCount,
    int EpisodeItemCount,
    bool HasXmlTv,
    DateTimeOffset? LastXmlTvRefreshUtc,
    long? GuideAgeMs,
    int GuideChannelCount,
    int MissingGuideChannelCount,
    int GuideProgramCount,
    long? LastSyncDurationMs,
    string? LastSyncError,
    string? LastFailureReason,
    DateTimeOffset? LastFailureUtc);

public sealed record XtreamCategoryCache(
    IReadOnlyList<CachedCategory> Live,
    IReadOnlyList<CachedCategory> Vod,
    IReadOnlyList<CachedCategory> Series);

public sealed record XmlTvChannelMap(
    string EpgChannelId,
    string XmlTvChannelId,
    string? DisplayName);

public sealed record CachedXmlTvProgram(
    string Id,
    string XmlTvChannelId,
    string Title,
    string? Description,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    IReadOnlyList<string> Categories,
    string? EpisodeTitle,
    string? IconUrl);

public sealed record XmlTvGuideData(
    IReadOnlyList<XmlTvChannelMap> Channels,
    IReadOnlyList<CachedXmlTvProgram> Programs,
    IReadOnlyList<string> MissingChannelIds,
    int InvalidProgramCount);

public sealed class XmlTvCacheInfo
{
    public DateTimeOffset? LastRefreshAttemptUtc { get; set; }
    public DateTimeOffset RefreshedAt { get; set; }
    public string FileName { get; set; } = "xmltv.xml";
    public int ChannelReferenceCount { get; set; }
    public int MissingChannelCount { get; set; }
    public int ProgramCount { get; set; }
    public bool IsCompressed { get; set; }
}

public sealed class XmlTvCacheMetadata
{
    public string? ETag { get; set; }
    public DateTimeOffset? LastModified { get; set; }
    public DateTimeOffset? LastFetchedUtc { get; set; }
    public DateTimeOffset? LastRefreshAttemptUtc { get; set; }
    public int? ProgramCount { get; set; }
    public int? ChannelReferenceCount { get; set; }
    public int? MissingChannelCount { get; set; }
}

public sealed record XtreamXmlTvResponse(
    bool IsNotModified,
    string? XmlContent,
    string? ETag,
    DateTimeOffset? LastModified,
    DateTimeOffset RetrievedAtUtc);

public static class XtreamCacheIdentity
{
    public const string LegacyProviderId = "legacy";

    public static string NormalizeProviderId(string? providerId)
        => string.IsNullOrWhiteSpace(providerId) ? LegacyProviderId : providerId;

    public static string BuildItemKey(string? providerId, int streamId)
        => $"{NormalizeProviderId(providerId)}:{streamId.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    public static string BuildItemKey(string? providerId, string? itemId)
        => $"{NormalizeProviderId(providerId)}:{itemId?.Trim() ?? string.Empty}";

    public static bool TryParseCategoryKey(string? value, out string providerId, out string categoryId)
    {
        providerId = LegacyProviderId;
        categoryId = string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var separatorIndex = value.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex >= value.Length - 1)
        {
            return false;
        }

        providerId = NormalizeProviderId(value[..separatorIndex].Trim());
        categoryId = value[(separatorIndex + 1)..].Trim();
        return !string.IsNullOrWhiteSpace(categoryId);
    }

    public static bool TryParseItemKey(string? value, out string providerId, out int streamId)
    {
        providerId = LegacyProviderId;
        streamId = 0;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var separatorIndex = value.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex >= value.Length - 1)
        {
            return false;
        }

        var rawProviderId = value[..separatorIndex].Trim();
        var rawStreamId = value[(separatorIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(rawProviderId)
            || string.IsNullOrWhiteSpace(rawStreamId)
            || !int.TryParse(rawStreamId, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out streamId))
        {
            return false;
        }

        providerId = NormalizeProviderId(rawProviderId);
        return streamId > 0;
    }
}
