using Jellyxtreme.Cache;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace Jellyxtreme.Providers;

public sealed class XtreamSeriesLibraryProvider : IChannel, IRequiresMediaInfoCallback
{
    private const string CategoryPrefix = "jellyxtreme-series-category:";
    private const string SeriesPrefix = "jellyxtreme-series:";
    private const string SeasonSeparator = ":season:";
    private const string EpisodePrefix = "jellyxtreme-episode:";

    private readonly XtreamCacheService _cacheService;
    private readonly SeriesProvider _seriesProvider;

    public XtreamSeriesLibraryProvider(XtreamCacheService cacheService, SeriesProvider seriesProvider)
    {
        _cacheService = cacheService;
        _seriesProvider = seriesProvider;
    }

    public string Name => "JellyXtreme Series";
    public string Description => "Cached Xtream series, seasons, and episodes from selected JellyXtreme categories.";
    public string HomePageUrl => "https://github.com/sn45k58myn-dev/JellyXtreme";
    public string DataVersion => "1";
    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

    public InternalChannelFeatures GetChannelFeatures()
        => new()
        {
            ContentTypes = [ChannelMediaContentType.Episode],
            MediaTypes = [ChannelMediaType.Video],
            SupportsContentDownloading = false,
            SupportsSortOrderToggle = false
        };

    public bool IsEnabledFor(string userId) => true;

    public IEnumerable<ImageType> GetSupportedChannelImages() => [];

    public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        => Task.FromResult<DynamicImageResponse>(null!);

    public async Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
    {
        var cache = await _cacheService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var items = GetItems(cache, query.FolderId);
        return Page(items, query.StartIndex, query.Limit);
    }

    public async Task<IEnumerable<MediaSourceInfo>> GetChannelItemMediaInfo(string id, CancellationToken cancellationToken)
    {
        if (!TryParseEpisodeId(id, out var providerId, out var streamId))
        {
            return [];
        }

        var mediaSources = await _seriesProvider.GetEpisodeMediaSourcesAsync(streamId, providerId, cancellationToken).ConfigureAwait(false);
        return mediaSources;
    }

    private static List<ChannelItemInfo> GetItems(XtreamCacheDocument cache, string? folderId)
    {
        if (string.IsNullOrWhiteSpace(folderId))
        {
            return GetCategoryFolders(cache);
        }

        if (folderId.StartsWith(CategoryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return GetSeriesForCategory(cache, folderId[CategoryPrefix.Length..]);
        }

        if (TryParseSeasonFolderId(folderId, out var providerId, out var seriesId, out var seasonNumber))
        {
            return GetEpisodesForSeason(cache, providerId, seriesId, seasonNumber);
        }

        if (TryParseSeriesFolderId(folderId, out providerId, out seriesId))
        {
            return GetSeasonsForSeries(cache, providerId, seriesId);
        }

        return [];
    }

    private static List<ChannelItemInfo> GetCategoryFolders(XtreamCacheDocument cache)
    {
        var seriesCounts = cache.SeriesItems
            .GroupBy(item => XtreamCacheIdentity.BuildItemKey(item.ProviderId, item.CategoryId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return cache.SeriesCategories
            .Where(category => seriesCounts.ContainsKey(XtreamCacheIdentity.BuildItemKey(category.ProviderId, category.CategoryId)))
            .OrderBy(category => category.Name, StringComparer.OrdinalIgnoreCase)
            .Select(category =>
            {
                var categoryKey = XtreamCacheIdentity.BuildItemKey(category.ProviderId, category.CategoryId);
                return new ChannelItemInfo
                {
                    Id = CategoryPrefix + categoryKey,
                    Name = category.Name,
                    Type = ChannelItemType.Folder,
                    FolderType = ChannelFolderType.Container,
                    MediaType = ChannelMediaType.Video,
                    ContentType = ChannelMediaContentType.Episode,
                    Tags = BuildTags(
                        "JellyXtreme",
                        "Series",
                        $"category:{category.CategoryId}",
                        $"provider:{category.ProviderId}"),
                    Overview = $"{seriesCounts[categoryKey]} cached JellyXtreme series item(s).",
                    ProviderIds = new Dictionary<string, string> { ["JellyXtremeCategory"] = category.CategoryId, ["JellyXtremeProvider"] = category.ProviderId }
                };
            })
            .ToList();
    }

    private static List<ChannelItemInfo> GetSeriesForCategory(XtreamCacheDocument cache, string categoryKey)
    {
        if (!XtreamCacheIdentity.TryParseCategoryKey(categoryKey, out var providerId, out var categoryId))
        {
            return [];
        }

        var categoryName = cache.SeriesCategories.FirstOrDefault(category =>
            string.Equals(category.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(category.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase))?.Name;

        return cache.SeriesItems
            .Where(series =>
                string.Equals(series.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(series.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(series => series.Name, StringComparer.OrdinalIgnoreCase)
            .Select(series => ToSeriesFolder(series, categoryName))
            .ToList();
    }

    private static List<ChannelItemInfo> GetSeasonsForSeries(XtreamCacheDocument cache, string providerId, int seriesId)
    {
        var series = cache.SeriesItems.FirstOrDefault(item =>
            item.ProviderId == providerId
            && item.SeriesId == seriesId);
        if (series is null)
        {
            return [];
        }

        return series.Seasons
            .OrderBy(season => season.SeasonNumber)
            .Select(season => ToSeasonFolder(series, season))
            .ToList();
    }

    private static List<ChannelItemInfo> GetEpisodesForSeason(
        XtreamCacheDocument cache,
        string providerId,
        int seriesId,
        int seasonNumber)
    {
        var series = cache.SeriesItems.FirstOrDefault(item =>
            item.ProviderId == providerId
            && item.SeriesId == seriesId);
        var season = series?.Seasons.FirstOrDefault(item => item.SeasonNumber == seasonNumber);
        if (series is null || season is null)
        {
            return [];
        }

        return season.Episodes
            .OrderBy(episode => ParseEpisodeNumber(episode.EpisodeNumber) ?? int.MaxValue)
            .ThenBy(episode => episode.Title, StringComparer.OrdinalIgnoreCase)
            .Select(episode => ToEpisodeItem(series, season, episode))
            .ToList();
    }

    private static ChannelItemInfo ToSeriesFolder(CachedSeriesItem series, string? categoryName)
        => new()
        {
            Id = SeriesPrefix + XtreamCacheIdentity.BuildItemKey(series.ProviderId, series.SeriesId),
            Name = series.Name,
            Type = ChannelItemType.Folder,
            FolderType = ChannelFolderType.Series,
            MediaType = ChannelMediaType.Video,
            ContentType = ChannelMediaContentType.Episode,
            ImageUrl = series.Poster,
            Overview = series.Plot,
            CommunityRating = series.Rating.HasValue ? (float)series.Rating.Value : null,
            Tags = BuildTags(
                "JellyXtreme",
                "Series",
                categoryName,
                $"provider:{series.ProviderId}",
                $"category:{series.CategoryId}",
                $"series:{series.SeriesId}"),
            ProviderIds = new Dictionary<string, string>
            {
                ["JellyXtremeSeriesId"] = XtreamCacheIdentity.BuildItemKey(series.ProviderId, series.SeriesId),
                ["JellyXtremeCategory"] = series.CategoryId,
                ["JellyXtremeProvider"] = series.ProviderId
            }
        };

    private static ChannelItemInfo ToSeasonFolder(CachedSeriesItem series, CachedSeason season)
        => new()
        {
            Id = $"{SeriesPrefix}{XtreamCacheIdentity.BuildItemKey(series.ProviderId, series.SeriesId)}{SeasonSeparator}{season.SeasonNumber}",
            Name = season.SeasonNumber <= 0 ? "Specials" : $"Season {season.SeasonNumber}",
            Type = ChannelItemType.Folder,
            FolderType = ChannelFolderType.Season,
            MediaType = ChannelMediaType.Video,
            ContentType = ChannelMediaContentType.Episode,
            ImageUrl = series.Poster,
            SeriesName = series.Name,
            IndexNumber = season.SeasonNumber,
            Tags = BuildTags(
                "JellyXtreme",
                "Series",
                $"provider:{series.ProviderId}",
                $"series:{series.SeriesId}",
                $"season:{season.SeasonNumber}"),
            ProviderIds = new Dictionary<string, string>
            {
                ["JellyXtremeSeriesId"] = XtreamCacheIdentity.BuildItemKey(series.ProviderId, series.SeriesId),
                ["JellyXtremeSeasonNumber"] = season.SeasonNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["JellyXtremeProvider"] = series.ProviderId
            }
        };

    private static ChannelItemInfo ToEpisodeItem(CachedSeriesItem series, CachedSeason season, CachedEpisodeItem episode)
        => new()
        {
            Id = EpisodePrefix + XtreamCacheIdentity.BuildItemKey(series.ProviderId, episode.StreamId),
            Name = episode.Title,
            Type = ChannelItemType.Media,
            MediaType = ChannelMediaType.Video,
            ContentType = ChannelMediaContentType.Episode,
            ImageUrl = episode.Poster ?? series.Poster,
            Overview = episode.Plot,
            SeriesName = series.Name,
            ParentIndexNumber = season.SeasonNumber,
            IndexNumber = ParseEpisodeNumber(episode.EpisodeNumber),
            PremiereDate = ParseDate(episode.ReleaseDate),
            DateCreated = ParseDate(episode.ReleaseDate),
            DateModified = DateTime.UtcNow,
            Tags = BuildTags(
                "JellyXtreme",
                "Series",
                $"provider:{series.ProviderId}",
                $"series:{series.SeriesId}",
                $"season:{season.SeasonNumber}",
                $"stream:{episode.StreamId}"),
            ProviderIds = new Dictionary<string, string>
            {
                ["JellyXtremeSeriesId"] = XtreamCacheIdentity.BuildItemKey(series.ProviderId, series.SeriesId),
                ["JellyXtremeEpisodeStreamId"] = XtreamCacheIdentity.BuildItemKey(series.ProviderId, episode.StreamId),
                ["JellyXtremeProvider"] = series.ProviderId
            }
        };

    private static ChannelItemResult Page(IReadOnlyList<ChannelItemInfo> items, int? startIndex, int? limit)
    {
        var start = Math.Max(0, startIndex ?? 0);
        var count = Math.Max(0, limit ?? items.Count);
        return new ChannelItemResult
        {
            TotalRecordCount = items.Count,
            Items = items.Skip(start).Take(count).ToList()
        };
    }

    private static bool TryParseSeriesFolderId(string id, out string providerId, out int seriesId)
    {
        providerId = string.Empty;
        seriesId = 0;

        if (!id.StartsWith(SeriesPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return XtreamCacheIdentity.TryParseItemKey(id[SeriesPrefix.Length..], out providerId, out seriesId)
            && seriesId > 0;
    }

    private static bool TryParseSeasonFolderId(string id, out string providerId, out int seriesId, out int seasonNumber)
    {
        providerId = string.Empty;
        seriesId = 0;
        seasonNumber = 0;

        if (!id.StartsWith(SeriesPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var markerIndex = id.IndexOf(SeasonSeparator, StringComparison.OrdinalIgnoreCase);
        if (markerIndex <= SeriesPrefix.Length)
        {
            return false;
        }

        if (!XtreamCacheIdentity.TryParseItemKey(id[SeriesPrefix.Length..markerIndex], out providerId, out seriesId)
            || seriesId <= 0)
        {
            return false;
        }

        return int.TryParse(id[(markerIndex + SeasonSeparator.Length)..], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out seasonNumber)
            && seasonNumber > 0;
    }

    private static bool TryParseEpisodeId(string id, out string providerId, out int streamId)
    {
        providerId = string.Empty;
        streamId = 0;

        if (!id.StartsWith(EpisodePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return XtreamCacheIdentity.TryParseItemKey(id[EpisodePrefix.Length..], out providerId, out streamId)
            && streamId > 0;
    }

    private static int? ParseEpisodeNumber(string? value)
        => int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    private static DateTime? ParseDate(string? value)
        => DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;

    private static List<string> BuildTags(params string?[] tags)
        => tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
