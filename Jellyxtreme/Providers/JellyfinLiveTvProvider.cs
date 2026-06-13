using Jellyxtreme.Cache;
using Jellyxtreme.Configuration;
using Jellyxtreme.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.MediaInfo;

namespace Jellyxtreme.Providers;

public sealed class JellyfinLiveTvProvider : ITunerHost, IConfigurableTunerHost
{
    private const string ProviderType = "jellyxtreme";
    private readonly XtreamCacheService _cacheService;
    private readonly XmlTvCacheService _xmlTvCacheService;
    private readonly StreamResolverService _streamResolver;
    private readonly Func<PluginConfiguration> _configProvider;

    public JellyfinLiveTvProvider(
        XtreamCacheService cacheService,
        XmlTvCacheService xmlTvCacheService,
        StreamResolverService streamResolver)
        : this(cacheService, xmlTvCacheService, streamResolver, () => Plugin.Instance?.Configuration ?? new PluginConfiguration())
    {
    }

    public JellyfinLiveTvProvider(
        XtreamCacheService cacheService,
        XmlTvCacheService xmlTvCacheService,
        StreamResolverService streamResolver,
        Func<PluginConfiguration> configProvider)
    {
        _cacheService = cacheService;
        _xmlTvCacheService = xmlTvCacheService;
        _streamResolver = streamResolver;
        _configProvider = configProvider;
    }

    public string Name => "JellyXtreme";
    public string Type => ProviderType;
    public bool IsSupported => true;

    public async Task<List<ChannelInfo>> GetChannels(bool enableCache, CancellationToken cancellationToken)
    {
        var cache = await _cacheService.LoadAsync(cancellationToken).ConfigureAwait(false);

        return cache.LiveChannels
            .Select(channel => new ChannelInfo
            {
                Name = channel.Name,
                Id = channel.StreamId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Number = channel.StreamId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                TunerChannelId = channel.StreamId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                CallSign = channel.EpgChannelId ?? channel.Name,
                ChannelType = ChannelType.TV,
                ChannelGroup = channel.GroupName,
                ImageUrl = channel.Logo,
                HasImage = !string.IsNullOrWhiteSpace(channel.Logo),
                Tags = BuildTags(channel)
            })
            .ToList();
    }

    public async Task<ILiveStream> GetChannelStream(
        string channelId,
        string streamId,
        IList<ILiveStream> currentLiveStreams,
        CancellationToken cancellationToken)
    {
        var mediaSources = await GetChannelStreamMediaSources(channelId, cancellationToken).ConfigureAwait(false);
        var mediaSource = mediaSources.First();

        return new JellyxtremeLiveStream(mediaSource, channelId);
    }

    public async Task<List<MediaSourceInfo>> GetChannelStreamMediaSources(string channelId, CancellationToken cancellationToken)
    {
        var channel = await GetCachedChannelAsync(channelId, cancellationToken).ConfigureAwait(false);
        if (channel is null)
        {
            return [];
        }

        var config = _configProvider();
        var streamUrl = _streamResolver.ResolveLiveUrl(config, channel.StreamId, channel.StreamExtension);

        return
        [
            new MediaSourceInfo
            {
                Id = channel.StreamId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Name = channel.Name,
                Path = streamUrl,
                Protocol = MediaProtocol.Http,
                Type = MediaSourceType.Default,
                Container = channel.StreamExtension,
                IsRemote = true,
                SupportsDirectPlay = true,
                SupportsDirectStream = true,
                SupportsTranscoding = true,
                IsInfiniteStream = true,
                RequiresOpening = false,
                RequiresClosing = false,
                LiveStreamId = channel.StreamId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            }
        ];
    }

    public Task<List<TunerHostInfo>> DiscoverDevices(int discoveryDurationMs, CancellationToken cancellationToken)
    {
        var info = new TunerHostInfo
        {
            Id = ProviderType,
            Type = ProviderType,
            FriendlyName = "JellyXtreme Cached Live TV",
            DeviceId = ProviderType,
            Source = "JellyXtreme",
            TunerCount = 1,
            AllowStreamSharing = true
        };

        return Task.FromResult(new List<TunerHostInfo> { info });
    }

    public Task Validate(TunerHostInfo info)
    {
        if (!string.Equals(info.Type, ProviderType, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(info.Type))
        {
            throw new ArgumentException("Unsupported JellyXtreme tuner host type.", nameof(info));
        }

        return Task.CompletedTask;
    }

    public async Task<IEnumerable<ProgramInfo>> GetProgramsAsync(
        string channelId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken)
    {
        var cache = await _cacheService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var channel = cache.LiveChannels.FirstOrDefault(item =>
            string.Equals(item.StreamId.ToString(System.Globalization.CultureInfo.InvariantCulture), channelId, StringComparison.OrdinalIgnoreCase));

        if (channel?.EpgChannelId is null)
        {
            return [];
        }

        var guide = await _xmlTvCacheService.LoadGuideDataAsync(cache.LiveChannels, cancellationToken).ConfigureAwait(false);
        var start = new DateTimeOffset(DateTime.SpecifyKind(startDateUtc, DateTimeKind.Utc));
        var end = new DateTimeOffset(DateTime.SpecifyKind(endDateUtc, DateTimeKind.Utc));

        return guide.Programs
            .Where(program => string.Equals(program.XmlTvChannelId, channel.EpgChannelId, StringComparison.OrdinalIgnoreCase))
            .Where(program => program.EndUtc > start && program.StartUtc < end)
            .Select(program => new ProgramInfo
            {
                Id = program.Id,
                ChannelId = channel.StreamId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Name = program.Title,
                Overview = program.Description,
                ShortOverview = program.Description,
                StartDate = program.StartUtc.UtcDateTime,
                EndDate = program.EndUtc.UtcDateTime,
                Genres = program.Categories.ToList(),
                EpisodeTitle = program.EpisodeTitle,
                ImageUrl = program.IconUrl,
                HasImage = !string.IsNullOrWhiteSpace(program.IconUrl),
                IsLive = program.StartUtc <= DateTimeOffset.UtcNow && program.EndUtc > DateTimeOffset.UtcNow,
                IsNews = program.Categories.Any(category => category.Contains("news", StringComparison.OrdinalIgnoreCase)),
                IsSports = program.Categories.Any(category => category.Contains("sport", StringComparison.OrdinalIgnoreCase)),
                IsKids = program.Categories.Any(category => category.Contains("kids", StringComparison.OrdinalIgnoreCase) || category.Contains("children", StringComparison.OrdinalIgnoreCase))
            })
            .ToList();
    }

    private async Task<CachedLiveChannel?> GetCachedChannelAsync(string channelId, CancellationToken cancellationToken)
    {
        var cache = await _cacheService.LoadAsync(cancellationToken).ConfigureAwait(false);
        return cache.LiveChannels.FirstOrDefault(channel =>
            string.Equals(channel.StreamId.ToString(System.Globalization.CultureInfo.InvariantCulture), channelId, StringComparison.OrdinalIgnoreCase));
    }

    private static string[] BuildTags(CachedLiveChannel channel)
    {
        var tags = new List<string>();

        if (!string.IsNullOrWhiteSpace(channel.CategoryId))
        {
            tags.Add($"category:{channel.CategoryId}");
        }

        if (!string.IsNullOrWhiteSpace(channel.GroupName))
        {
            tags.Add(channel.GroupName);
        }

        if (!string.IsNullOrWhiteSpace(channel.EpgChannelId))
        {
            tags.Add($"epg:{channel.EpgChannelId}");
        }

        return tags.ToArray();
    }

    private sealed class JellyxtremeLiveStream : ILiveStream
    {
        public JellyxtremeLiveStream(MediaSourceInfo mediaSource, string originalStreamId)
        {
            MediaSource = mediaSource;
            OriginalStreamId = originalStreamId;
            UniqueId = $"jellyxtreme-{originalStreamId}";
        }

        public int ConsumerCount { get; set; }
        public string OriginalStreamId { get; set; }
        public string TunerHostId => ProviderType;
        public bool EnableStreamSharing => true;
        public MediaSourceInfo MediaSource { get; set; }
        public string UniqueId { get; }

        public Task Open(CancellationToken openCancellationToken) => Task.CompletedTask;

        public Task Close() => Task.CompletedTask;

        public void Dispose()
        {
        }

        public Stream GetStream()
            => throw new NotSupportedException("JellyXtreme exposes remote Xtream streams by URL through MediaSourceInfo.");
    }
}
