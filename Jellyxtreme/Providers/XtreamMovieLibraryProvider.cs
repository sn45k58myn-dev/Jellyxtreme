using Jellyxtreme.Cache;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace Jellyxtreme.Providers;

public sealed class XtreamMovieLibraryProvider : IChannel, IRequiresMediaInfoCallback
{
    private const string CategoryPrefix = "jellyxtreme-vod-category:";
    private const string MoviePrefix = "jellyxtreme-vod:";
    private readonly XtreamCacheService _cacheService;
    private readonly VodProvider _vodProvider;

    public XtreamMovieLibraryProvider(XtreamCacheService cacheService, VodProvider vodProvider)
    {
        _cacheService = cacheService;
        _vodProvider = vodProvider;
    }

    public string Name => "JellyXtreme Movies";
    public string Description => "Cached Xtream VOD movies from selected JellyXtreme categories.";
    public string HomePageUrl => "https://github.com/sn45k58myn-dev/Jellyxtreme";
    public string DataVersion => "1";
    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

    public InternalChannelFeatures GetChannelFeatures()
        => new()
        {
            ContentTypes = [ChannelMediaContentType.Movie],
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
        var items = string.IsNullOrWhiteSpace(query.FolderId)
            ? GetCategoryFolders(cache)
            : GetMoviesForFolder(cache, query.FolderId);

        return Page(items, query.StartIndex, query.Limit);
    }

    public async Task<IEnumerable<MediaSourceInfo>> GetChannelItemMediaInfo(string id, CancellationToken cancellationToken)
    {
        if (!TryParseMovieId(id, out var providerId, out var streamId))
        {
            return [];
        }

        return await _vodProvider.GetMediaSourcesAsync(streamId, providerId, cancellationToken).ConfigureAwait(false);
    }

    private static List<ChannelItemInfo> GetCategoryFolders(XtreamCacheDocument cache)
    {
        var movieCounts = cache.VodItems
            .GroupBy(item => XtreamCacheIdentity.BuildItemKey(item.ProviderId, item.CategoryId), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        return cache.VodCategories
            .Where(category => movieCounts.ContainsKey(XtreamCacheIdentity.BuildItemKey(category.ProviderId, category.CategoryId)))
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
                    ContentType = ChannelMediaContentType.Movie,
                    Tags = ["JellyXtreme", "VOD", $"category:{categoryKey}"],
                    Overview = $"{movieCounts[categoryKey]} cached JellyXtreme movie item(s).",
                    ProviderIds = new Dictionary<string, string>
                    {
                        ["JellyXtremeCategory"] = category.CategoryId,
                        ["JellyXtremeProvider"] = category.ProviderId
                    }
                };
            })
            .ToList();
    }

    private static List<ChannelItemInfo> GetMoviesForFolder(XtreamCacheDocument cache, string folderId)
    {
        if (!folderId.StartsWith(CategoryPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var categoryKey = folderId[CategoryPrefix.Length..];
        if (!XtreamCacheIdentity.TryParseCategoryKey(categoryKey, out var providerId, out var categoryId))
        {
            return [];
        }

        var categoryName = cache.VodCategories.FirstOrDefault(category =>
            string.Equals(category.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(category.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase))?.Name;

        return cache.VodItems
            .Where(item =>
                string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.CategoryId, categoryId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => ToMovieItem(item, categoryName))
            .ToList();
    }

    private static ChannelItemInfo ToMovieItem(CachedVodItem item, string? categoryName)
        => new()
        {
            Id = MoviePrefix + XtreamCacheIdentity.BuildItemKey(item.ProviderId, item.StreamId),
            Name = item.Name,
            Type = ChannelItemType.Media,
            MediaType = ChannelMediaType.Video,
            ContentType = ChannelMediaContentType.Movie,
            ImageUrl = item.Poster,
            CommunityRating = item.Rating.HasValue ? (float)item.Rating.Value : null,
            DateCreated = ParseDate(item.Added),
            DateModified = DateTime.UtcNow,
            Tags = BuildTags("JellyXtreme", "VOD", categoryName, $"category:{item.CategoryId}", $"stream:{item.StreamId}"),
            ProviderIds = new Dictionary<string, string>
            {
                ["JellyXtremeVodStreamId"] = item.StreamId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["JellyXtremeCategory"] = item.CategoryId,
                ["JellyXtremeProvider"] = item.ProviderId
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

    private static bool TryParseMovieId(string id, out string? providerId, out int streamId)
    {
        providerId = null;
        streamId = 0;

        if (!id.StartsWith(MoviePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var key = id[MoviePrefix.Length..];
        if (!XtreamCacheIdentity.TryParseItemKey(key, out var parsedProviderId, out var parsedStreamId))
        {
            return false;
        }

        providerId = parsedProviderId;
        streamId = parsedStreamId;
        return true;
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (long.TryParse(value, out var epochSeconds) && epochSeconds > 0)
        {
            return DateTimeOffset.FromUnixTimeSeconds(epochSeconds).UtcDateTime;
        }

        return DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    private static List<string> BuildTags(params string?[] tags)
        => tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}
