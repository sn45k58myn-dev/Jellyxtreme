using Jellyxtreme.Cache;
using Jellyxtreme.Configuration;
using Jellyxtreme.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace Jellyxtreme.Providers;

public sealed class JellyfinLiveTvProvider : ITunerHost, IConfigurableTunerHost, IListingsProvider, ISupportsDirectStreamProvider
{
    private const string ProviderType = "jellyxtreme";
    private readonly XtreamCacheService _cacheService;
    private readonly XmlTvCacheService _xmlTvCacheService;
    private readonly StreamResolverService _streamResolver;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<JellyfinLiveTvProvider> _logger;
    private readonly Func<PluginConfiguration> _configProvider;
    private readonly Func<string> _localBaseUrlProvider;

    public JellyfinLiveTvProvider(
        XtreamCacheService cacheService,
        XmlTvCacheService xmlTvCacheService,
        StreamResolverService streamResolver,
        IHttpClientFactory httpClientFactory,
        IServerApplicationHost applicationHost,
        ILogger<JellyfinLiveTvProvider> logger)
        : this(
            cacheService,
            xmlTvCacheService,
            streamResolver,
            httpClientFactory,
            logger,
            () => Plugin.Instance?.Configuration ?? new PluginConfiguration(),
            () => applicationHost.GetApiUrlForLocalAccess())
    {
    }

    public JellyfinLiveTvProvider(
        XtreamCacheService cacheService,
        XmlTvCacheService xmlTvCacheService,
        StreamResolverService streamResolver,
        IHttpClientFactory httpClientFactory,
        ILogger<JellyfinLiveTvProvider> logger,
        Func<PluginConfiguration> configProvider,
        Func<string>? localBaseUrlProvider = null)
    {
        _cacheService = cacheService;
        _xmlTvCacheService = xmlTvCacheService;
        _streamResolver = streamResolver;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _configProvider = configProvider;
        _localBaseUrlProvider = localBaseUrlProvider ?? (() => "http://127.0.0.1:8096");
    }

    public string Name => "JellyXtreme";
    public string Type => ProviderType;
    public bool IsSupported => true;

    public Task<List<NameIdPair>> GetLineups(ListingsProviderInfo info, string country, string location)
        => Task.FromResult(new List<NameIdPair>
        {
            new()
            {
                Name = "JellyXtreme",
                Id = ProviderType
            }
        });

    public Task Validate(ListingsProviderInfo info, bool validateLogin, bool validateListings)
    {
        info.Type = ProviderType;
        info.Id = string.IsNullOrWhiteSpace(info.Id) ? ProviderType : info.Id;
        info.ListingsId = string.IsNullOrWhiteSpace(info.ListingsId) ? ProviderType : info.ListingsId;
        info.EnableAllTuners = true;
        _logger.LogInformation("JellyXtreme Live TV listings provider validated.");
        return Task.CompletedTask;
    }

    public Task<List<ChannelInfo>> GetChannels(ListingsProviderInfo info, CancellationToken cancellationToken)
        => GetChannels(true, cancellationToken);

    public async Task<List<ChannelInfo>> GetChannels(bool enableCache, CancellationToken cancellationToken)
    {
        var cache = await _cacheService.LoadAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("JellyXtreme Live TV provider returning {ChannelCount} cached channels.", cache.LiveChannels.Count);

        return cache.LiveChannels
            .Select(channel => new ChannelInfo
            {
                Name = channel.Name,
                Id = XtreamCacheIdentity.BuildItemKey(channel.ProviderId, channel.StreamId),
                Number = channel.Name,
                TunerChannelId = XtreamCacheIdentity.BuildItemKey(channel.ProviderId, channel.StreamId),
                TunerHostId = ProviderType,
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
        var channel = await GetCachedChannelAsync(channelId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("JellyXtreme live channel was not found in cache.");
        var streamUrl = _streamResolver.ResolveLiveUrl(_configProvider(), channel.ProviderId, channel.StreamId, channel.StreamExtension);
        mediaSource.LiveStreamId = channelId;
        mediaSource.RequiresOpening = false;

        return new JellyxtremeLiveStream(mediaSource, channelId, streamUrl, _httpClientFactory, _logger);
    }

    public async Task<ILiveStream> GetChannelStreamWithDirectStreamProvider(
        string channelId,
        string streamId,
        List<ILiveStream> currentLiveStreams,
        CancellationToken cancellationToken)
    {
        var existingStream = currentLiveStreams.FirstOrDefault(stream =>
            string.Equals(stream.OriginalStreamId, channelId, StringComparison.OrdinalIgnoreCase));
        if (existingStream is not null)
        {
            return existingStream;
        }

        var mediaSources = await GetChannelStreamMediaSources(channelId, cancellationToken).ConfigureAwait(false);
        var mediaSource = mediaSources.First();
        var channel = await GetCachedChannelAsync(channelId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("JellyXtreme live channel was not found in cache.");
        var streamUrl = _streamResolver.ResolveLiveUrl(_configProvider(), channel.ProviderId, channel.StreamId, channel.StreamExtension);
        mediaSource.LiveStreamId = channelId;
        mediaSource.RequiresOpening = false;

        return new JellyxtremeLiveStream(mediaSource, channelId, streamUrl, _httpClientFactory, _logger);
    }

    public async Task<List<MediaSourceInfo>> GetChannelStreamMediaSources(string channelId, CancellationToken cancellationToken)
    {
        var channel = await GetCachedChannelAsync(channelId, cancellationToken).ConfigureAwait(false);
        if (channel is null)
        {
            return [];
        }

        var config = _configProvider();
        _ = _streamResolver.ResolveLiveUrl(config, channel.ProviderId, channel.StreamId, channel.StreamExtension);
        _logger.LogInformation("JellyXtreme resolved playback media source for live channel {ChannelId}.", channel.StreamId);
        var safePath = BuildSafeLivePath(_localBaseUrlProvider(), channel.ProviderId, channel.StreamId, channel.StreamExtension);

        return
        [
            new MediaSourceInfo
            {
                Id = $"jellyxtreme-live-{XtreamCacheIdentity.BuildItemKey(channel.ProviderId, channel.StreamId)}",
                Name = channel.Name,
                Path = safePath,
                Protocol = MediaProtocol.Http,
                Type = MediaSourceType.Default,
                Container = NormalizeLiveContainer(channel.StreamExtension),
                IsRemote = true,
                SupportsDirectPlay = false,
                SupportsDirectStream = false,
                SupportsTranscoding = true,
                IsInfiniteStream = true,
                RequiresOpening = true,
                RequiresClosing = true,
                OpenToken = XtreamCacheIdentity.BuildItemKey(channel.ProviderId, channel.StreamId),
                ReadAtNativeFramerate = true,
                IgnoreDts = true,
                GenPtsInput = true,
                Timestamp = MediaBrowser.Model.MediaInfo.TransportStreamTimestamp.Valid,
                SupportsProbing = true,
                VideoType = VideoType.VideoFile,
                MediaStreams = [],
                MediaAttachments = [],
                Formats = [],
                RequiredHttpHeaders = new Dictionary<string, string>(),
                DefaultAudioStreamIndex = null
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

        _logger.LogInformation("JellyXtreme Live TV provider discovered cached tuner host with {TunerCount} tuner.", info.TunerCount);
        return Task.FromResult(new List<TunerHostInfo> { info });
    }

    public Task Validate(TunerHostInfo info)
    {
        if (!string.Equals(info.Type, ProviderType, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(info.Type))
        {
            throw new ArgumentException("Unsupported JellyXtreme tuner host type.", nameof(info));
        }

        info.Type = ProviderType;
        info.FriendlyName = string.IsNullOrWhiteSpace(info.FriendlyName) ? "JellyXtreme Cached Live TV" : info.FriendlyName;
        info.DeviceId = string.IsNullOrWhiteSpace(info.DeviceId) ? ProviderType : info.DeviceId;
        info.Source = string.IsNullOrWhiteSpace(info.Source) ? "JellyXtreme" : info.Source;
        info.AllowStreamSharing = true;
        if (info.TunerCount <= 0)
        {
            info.TunerCount = 1;
        }

        _logger.LogInformation("JellyXtreme Live TV tuner host validated with {TunerCount} tuner.", info.TunerCount);
        return Task.CompletedTask;
    }

    public async Task<IEnumerable<ProgramInfo>> GetProgramsAsync(
        ListingsProviderInfo info,
        string channelId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken)
        => await GetProgramsForChannelAsync(channelId, startDateUtc, endDateUtc, cancellationToken).ConfigureAwait(false);

    private async Task<IEnumerable<ProgramInfo>> GetProgramsForChannelAsync(
        string channelId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken)
    {
        var cache = await _cacheService.LoadAsync(cancellationToken).ConfigureAwait(false);
        var (providerId, streamId) = ParseChannelId(channelId);
        var channel = cache.LiveChannels.FirstOrDefault(item =>
            item.StreamId == streamId
            && (string.IsNullOrWhiteSpace(providerId)
                || string.Equals(item.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)));

        if (channel?.EpgChannelId is null)
        {
            _logger.LogDebug("JellyXtreme Live TV guide requested for channel {ChannelId}, but no cached EPG channel id was found.", channelId);
            return [];
        }

        var guide = await _xmlTvCacheService.LoadGuideDataAsync(cache.LiveChannels, cancellationToken).ConfigureAwait(false);
        var start = new DateTimeOffset(DateTime.SpecifyKind(startDateUtc, DateTimeKind.Utc));
        var end = new DateTimeOffset(DateTime.SpecifyKind(endDateUtc, DateTimeKind.Utc));

        var programs = guide.Programs
            .Where(program => string.Equals(program.XmlTvChannelId, channel.EpgChannelId, StringComparison.OrdinalIgnoreCase))
            .Where(program => program.EndUtc > start && program.StartUtc < end)
            .Select(program => new ProgramInfo
            {
                Id = program.Id,
                ChannelId = XtreamCacheIdentity.BuildItemKey(channel.ProviderId, channel.StreamId),
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

        _logger.LogDebug("JellyXtreme Live TV listings provider returning {ProgramCount} guide programs for channel {ChannelId}.", programs.Count, channelId);
        return programs;
    }

    private async Task<CachedLiveChannel?> GetCachedChannelAsync(string channelId, CancellationToken cancellationToken)
    {
        var (providerId, streamId) = ParseChannelId(channelId);
        var cache = await _cacheService.LoadAsync(cancellationToken).ConfigureAwait(false);
        return cache.LiveChannels.FirstOrDefault(channel =>
            channel.StreamId == streamId
            && (string.IsNullOrWhiteSpace(providerId)
                || string.Equals(channel.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)));
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

    private static string NormalizeLiveContainer(string? extension)
    {
        if (string.Equals(extension, "ts", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, "m2ts", StringComparison.OrdinalIgnoreCase))
        {
            return "mpegts";
        }

        return string.IsNullOrWhiteSpace(extension) ? "mpegts" : extension.TrimStart('.');
    }

    private static string BuildSafeLivePath(string localBaseUrl, string providerId, int streamId, string? extension)
    {
        var safeExtension = string.IsNullOrWhiteSpace(extension)
            ? "ts"
            : extension.Trim().TrimStart('.');
        if (string.IsNullOrWhiteSpace(safeExtension))
        {
            safeExtension = "ts";
        }

        return $"{localBaseUrl.TrimEnd('/')}/Jellyxtreme/Live/{Uri.EscapeDataString(providerId)}/{streamId.ToString(System.Globalization.CultureInfo.InvariantCulture)}.{safeExtension}";
    }

    private static (string? providerId, int streamId) ParseChannelId(string channelId)
    {
        if (XtreamCacheIdentity.TryParseItemKey(channelId, out var providerId, out var streamId))
        {
            return (providerId, streamId);
        }

        return (null, int.TryParse(channelId, out var parsedStreamId) ? parsedStreamId : 0);
    }

    private sealed class JellyxtremeLiveStream : ILiveStream, IDirectStreamProvider
    {
        private readonly string _streamUrl;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger _logger;
        private HttpResponseMessage? _response;
        private Stream? _stream;

        public JellyxtremeLiveStream(
            MediaSourceInfo mediaSource,
            string originalStreamId,
            string streamUrl,
            IHttpClientFactory httpClientFactory,
            ILogger logger)
        {
            MediaSource = mediaSource;
            OriginalStreamId = originalStreamId;
            UniqueId = $"jellyxtreme-{originalStreamId}";
            _streamUrl = streamUrl;
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public int ConsumerCount { get; set; }
        public string OriginalStreamId { get; set; }
        public string TunerHostId => ProviderType;
        public bool EnableStreamSharing => true;
        public MediaSourceInfo MediaSource { get; set; }
        public string UniqueId { get; }

        public async Task Open(CancellationToken openCancellationToken)
        {
            if (_stream is not null)
            {
                return;
            }

            _stream = await OpenRemoteStreamAsync(openCancellationToken).ConfigureAwait(false);
        }

        public Task Close()
        {
            Dispose();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _stream?.Dispose();
            _response?.Dispose();
            _stream = null;
            _response = null;
        }

        public Stream GetStream()
        {
            if (_stream is not null)
            {
                return _stream;
            }

            _stream = OpenRemoteStreamAsync(CancellationToken.None).GetAwaiter().GetResult();
            return _stream;
        }

        private async Task<Stream> OpenRemoteStreamAsync(CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(_streamUrl)
                || !Uri.TryCreate(_streamUrl, UriKind.Absolute, out var streamUri)
                || (streamUri.Scheme != Uri.UriSchemeHttp && streamUri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("JellyXtreme live stream URL is invalid.");
            }

            try
            {
                var client = _httpClientFactory.CreateClient(nameof(JellyfinLiveTvProvider));
                var request = new HttpRequestMessage(HttpMethod.Get, streamUri);
                ApplyStreamRequestHeaders(request);
                _response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                _response.EnsureSuccessStatusCode();
                _logger.LogDebug("JellyXtreme opened direct live stream for channel {ChannelId}.", OriginalStreamId);
                return await _response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                _response?.Dispose();
                _response = null;
                throw new InvalidOperationException("Unable to open JellyXtreme live stream.", exception);
            }
        }

        private static void ApplyStreamRequestHeaders(HttpRequestMessage request)
        {
            request.Headers.UserAgent.TryParseAdd("VLC/3.0.20 LibVLC/3.0.20");
            request.Headers.Accept.TryParseAdd("*/*");
        }
    }
}
