using Jellyxtreme.Api;
using Jellyxtreme.Cache;
using Jellyxtreme.Configuration;
using Jellyxtreme.Controllers;
using Jellyxtreme.Providers;
using Jellyxtreme.Services;
using MediaBrowser.Model.LiveTv;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace Jellyxtreme.Tests;

public sealed class XtreamCoreTests
{
    [Fact]
    public void ConfigDefaultsImportNothing()
    {
        var config = new PluginConfiguration();

        Assert.False(config.EnableLiveTv);
        Assert.False(config.EnableVod);
        Assert.False(config.EnableSeries);
        Assert.Empty(config.SelectedLiveCategoryIds);
        Assert.Empty(config.SelectedVodCategoryIds);
        Assert.Empty(config.SelectedSeriesCategoryIds);
        Assert.Null(config.LastSuccessfulSyncUtc);
        Assert.Null(config.LastSyncDurationMs);
        Assert.Empty(config.LastSyncError);
    }

    [Fact]
    public void ProjectTargetsNet9WithJellyfin1011Packages()
    {
        var repositoryRoot = FindRepositoryRoot();
        var project = XDocument.Load(Path.Combine(repositoryRoot, "Jellyxtreme", "Jellyxtreme.csproj"));
        var packageVersions = project.Descendants("PackageReference")
            .ToDictionary(
                item => item.Attribute("Include")?.Value ?? string.Empty,
                item => item.Attribute("Version")?.Value ?? string.Empty);

        Assert.Equal("net9.0", project.Descendants("TargetFramework").Single().Value);
        Assert.Equal("10.11.11", packageVersions["Jellyfin.Controller"]);
        Assert.Equal("10.11.11", packageVersions["Jellyfin.Model"]);
    }

    [Fact]
    public void PluginManifestIsValidAndConfigPageIsEmbedded()
    {
        var repositoryRoot = FindRepositoryRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "Jellyxtreme", "plugin.json")));

        Assert.Equal("Jellyxtreme", document.RootElement.GetProperty("name").GetString());
        Assert.Equal("10.11.0.0", document.RootElement.GetProperty("targetAbi").GetString());
        Assert.Contains(
            "Jellyxtreme.Configuration.configPage.html",
            typeof(Plugin).Assembly.GetManifestResourceNames());
    }

    [Theory]
    [InlineData("http://example.com", true)]
    [InlineData("https://example.com:8080", true)]
    [InlineData("example.com:8080", true)]
    [InlineData("https://example.com/player_api.php?username=user&password=secret", true)]
    [InlineData("http://example.com:8080/get.php?username=user&password=secret&type=m3u", true)]
    [InlineData("ftp://example.com", false)]
    [InlineData("not-a-url", false)]
    public void ServerUrlValidationAllowsOnlyHttpAndHttps(string url, bool expected)
    {
        Assert.Equal(expected, XtreamApiClient.TryNormalizeServerUrl(url, out _));
    }

    [Theory]
    [InlineData("example.com:8080", "http://example.com:8080")]
    [InlineData("https://example.com/player_api.php?username=user&password=secret", "https://example.com")]
    [InlineData("http://example.com:8080/get.php?username=user&password=secret&type=m3u", "http://example.com:8080")]
    [InlineData("https://example.com/iptv/player_api.php", "https://example.com/iptv")]
    public void ServerUrlNormalizationHandlesCommonXtreamInputs(string input, string expected)
    {
        Assert.True(XtreamApiClient.TryNormalizeServerUrl(input, out var uri));
        Assert.Equal(expected, uri.AbsoluteUri.TrimEnd('/'));
    }

    [Fact]
    public void RedactRemovesAuthenticatedQuery()
    {
        var redacted = XtreamApiClient.Redact("https://provider.example/player_api.php?username=user&password=secret");

        Assert.Equal("https://provider.example/player_api.php?redacted=true", redacted);
        Assert.DoesNotContain("user", redacted);
        Assert.DoesNotContain("secret", redacted);

        var streamUrl = XtreamApiClient.Redact("https://provider.example/movie/user/secret/55.mkv");
        Assert.Equal("https://provider.example/movie/redacted/redacted/55.mkv", streamUrl);
    }

    [Fact]
    public void CategoryFilterRequiresExplicitSelection()
    {
        Assert.False(XtreamSelectionFilter.IsCategorySelected("10", []));
        Assert.False(XtreamSelectionFilter.IsCategorySelected(null, ["10"]));
        Assert.True(XtreamSelectionFilter.IsCategorySelected("10", ["9", "10"]));
        Assert.True(XtreamSelectionFilter.IsCategorySelected("abc", ["ABC"]));
    }

    [Fact]
    public void NoSelectedCategoriesDisablesSectionCaching()
    {
        Assert.False(XtreamSelectionFilter.ShouldCacheSection(true, []));
        Assert.False(XtreamSelectionFilter.ShouldCacheSection(false, ["10"]));
        Assert.True(XtreamSelectionFilter.ShouldCacheSection(true, ["10"]));
    }

    [Fact]
    public void StreamUrlsAreResolvedAtPlaybackTime()
    {
        var client = new XtreamApiClient(
            new TestHttpClientFactory(),
            NullLogger<XtreamApiClient>.Instance);
        var settings = new XtreamConnectionSettings(
            "https://provider.example",
            "user name",
            "pass word",
            TimeSpan.FromSeconds(30));

        Assert.Equal("https://provider.example/live/user%20name/pass%20word/123.ts", client.GetLiveStreamUrl(settings, 123));
        Assert.Equal("https://provider.example/movie/user%20name/pass%20word/456.mkv", client.GetVodStreamUrl(settings, 456, "mkv"));
        Assert.Equal("https://provider.example/series/user%20name/pass%20word/789.mp4", client.GetSeriesStreamUrl(settings, 789));
    }

    [Fact]
    public void StreamResolverBuildsExpectedPlaybackPaths()
    {
        var apiClient = new XtreamApiClient(new TestHttpClientFactory(), NullLogger<XtreamApiClient>.Instance);
        var resolver = new StreamResolverService(apiClient);
        var config = new PluginConfiguration
        {
            ServerUrl = "https://provider.example",
            Username = "user",
            Password = "secret"
        };

        Assert.Equal("https://provider.example/live/user/secret/1.ts", resolver.ResolveLiveUrl(config, 1));
        Assert.Equal("https://provider.example/movie/user/secret/2.mkv", resolver.ResolveVodUrl(config, 2, "mkv"));
        Assert.Equal("https://provider.example/series/user/secret/3.mp4", resolver.ResolveEpisodeUrl(config, 3));
    }

    [Theory]
    [InlineData(0, "ts")]
    [InlineData(-1, "ts")]
    [InlineData(1, "")]
    [InlineData(1, "   ")]
    [InlineData(1, ".")]
    public void StreamResolverThrowsControlledExceptionForInvalidStreamInput(int streamId, string extension)
    {
        var apiClient = new XtreamApiClient(new TestHttpClientFactory(), NullLogger<XtreamApiClient>.Instance);
        var resolver = new StreamResolverService(apiClient);
        var config = new PluginConfiguration
        {
            ServerUrl = "https://provider.example",
            Username = "user",
            Password = "secret"
        };

        var exception = Assert.Throws<XtreamValidationException>(() => resolver.ResolveLiveUrl(config, streamId, extension));

        Assert.NotEmpty(exception.Operation);
    }

    [Theory]
    [InlineData("", "user", "secret")]
    [InlineData("ftp://provider.example", "user", "secret")]
    [InlineData("https://provider.example", "", "secret")]
    [InlineData("https://provider.example", "user", "")]
    public void StreamResolverThrowsControlledExceptionForInvalidConfig(string serverUrl, string username, string password)
    {
        var apiClient = new XtreamApiClient(new TestHttpClientFactory(), NullLogger<XtreamApiClient>.Instance);
        var resolver = new StreamResolverService(apiClient);
        var config = new PluginConfiguration
        {
            ServerUrl = serverUrl,
            Username = username,
            Password = password
        };

        Assert.Throws<XtreamValidationException>(() => resolver.ResolveLiveUrl(config, 1));
    }

    [Fact]
    public async Task JellyfinLiveTvProviderMapsCachedChannelsAndResolvesPlaybackUrl()
    {
        var cacheDirectory = Path.Combine(Path.GetTempPath(), "jellyxtreme-tests", Guid.NewGuid().ToString("N"));
        var cache = new XtreamCacheService(NullLogger<XtreamCacheService>.Instance, cacheDirectory);
        await cache.SaveAsync(new XtreamCacheDocument
        {
            LiveChannels =
            [
                new CachedLiveChannel
                {
                    Name = "News HD",
                    StreamId = 42,
                    Logo = "https://logo.example/news.png",
                    EpgChannelId = "news.example",
                    CategoryId = "7",
                    GroupName = "News",
                    StreamExtension = "ts"
                }
            ]
        }, CancellationToken.None);

        var apiClient = new XtreamApiClient(new TestHttpClientFactory(), NullLogger<XtreamApiClient>.Instance);
        var xmlTvCache = new XmlTvCacheService(apiClient, cache, NullLogger<XmlTvCacheService>.Instance);
        var provider = new JellyfinLiveTvProvider(
            cache,
            xmlTvCache,
            new StreamResolverService(apiClient),
            new TestHttpClientFactory(),
            NullLogger<JellyfinLiveTvProvider>.Instance,
            () => new PluginConfiguration
            {
                ServerUrl = "https://provider.example",
                Username = "user",
                Password = "secret"
            });

        var channels = await provider.GetChannels(false, CancellationToken.None);
        var mediaSources = await provider.GetChannelStreamMediaSources("42", CancellationToken.None);

        Assert.Single(channels);
        Assert.Equal("News HD", channels[0].Name);
        Assert.Equal("https://logo.example/news.png", channels[0].ImageUrl);
        Assert.Equal("news.example", channels[0].CallSign);
        Assert.Equal("News", channels[0].ChannelGroup);
        Assert.Equal("42", channels[0].Id);
        Assert.Equal("jellyxtreme", channels[0].TunerHostId);
        Assert.Contains("category:7", channels[0].Tags);
        Assert.Single(mediaSources);
        Assert.Equal("http://127.0.0.1:8096/Jellyxtreme/Live/42.ts", mediaSources[0].Path);
        Assert.False(string.IsNullOrWhiteSpace(mediaSources[0].OpenToken));
        Assert.True(mediaSources[0].RequiresOpening);
        Assert.Null(mediaSources[0].LiveStreamId);
        Assert.NotNull(mediaSources[0].MediaStreams);
        Assert.NotNull(mediaSources[0].MediaAttachments);
        Assert.NotNull(mediaSources[0].Formats);
        Assert.NotNull(mediaSources[0].RequiredHttpHeaders);
        Assert.Equal("mpegts", mediaSources[0].Container);
        Assert.Empty(mediaSources[0].MediaStreams);
        Assert.Null(mediaSources[0].DefaultAudioStreamIndex);
        Assert.True(mediaSources[0].ReadAtNativeFramerate);
        Assert.True(mediaSources[0].IgnoreDts);
    }

    [Fact]
    public async Task VodProviderUsesCachedMetadataAndResolvesStreamsDynamically()
    {
        var cacheDirectory = Path.Combine(Path.GetTempPath(), "jellyxtreme-tests", Guid.NewGuid().ToString("N"));
        var cache = new XtreamCacheService(NullLogger<XtreamCacheService>.Instance, cacheDirectory);
        await cache.SaveAsync(new XtreamCacheDocument
        {
            VodCategories = [new CachedCategory { CategoryId = "11", Name = "Movies", Kind = "vod" }],
            VodItems =
            [
                new CachedVodItem
                {
                    Name = "Cached Movie",
                    StreamId = 55,
                    CategoryId = "11",
                    Poster = "https://poster.example/movie.jpg",
                    Rating = 8.1,
                    ContainerExtension = "mkv",
                    Added = "2026-06-13"
                }
            ]
        }, CancellationToken.None);

        var apiClient = new XtreamApiClient(new TestHttpClientFactory(), NullLogger<XtreamApiClient>.Instance);
        var provider = new VodProvider(
            cache,
            new StreamResolverService(apiClient),
            () => new PluginConfiguration
            {
                ServerUrl = "https://provider.example",
                Username = "user",
                Password = "secret"
            });

        var items = await provider.GetItemsAsync(CancellationToken.None);
        var mediaSources = await provider.GetMediaSourcesAsync(55, CancellationToken.None);

        Assert.Single(items);
        Assert.Equal("Cached Movie", items[0].Name);
        Assert.Equal("Movies", items[0].CategoryName);
        Assert.Equal("https://poster.example/movie.jpg", items[0].Poster);
        Assert.Single(mediaSources);
        Assert.Equal("https://provider.example/movie/user/secret/55.mkv", mediaSources[0].Path);
        Assert.True(mediaSources[0].IsRemote);
    }

    [Fact]
    public async Task ControllerRejectsInvalidConnectionUrl()
    {
        var controller = CreateController(Path.Combine(Path.GetTempPath(), "jellyxtreme-tests", Guid.NewGuid().ToString("N")));

        var result = await controller.TestConnection(
            new XtreamConnectionRequest("ftp://provider.example", "user", "secret"),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task ControllerRequiresExplicitPlaybackUrlForVodMediaSources()
    {
        var cacheDirectory = Path.Combine(Path.GetTempPath(), "jellyxtreme-tests", Guid.NewGuid().ToString("N"));
        var controller = CreateController(cacheDirectory);

        var result = await controller.GetVodMediaSources(1, includePlaybackUrl: false, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task CacheSavePersistsVersionedDocument()
    {
        var cacheDirectory = Path.Combine(Path.GetTempPath(), "jellyxtreme-tests", Guid.NewGuid().ToString("N"));
        var cache = new XtreamCacheService(NullLogger<XtreamCacheService>.Instance, cacheDirectory);

        await cache.SaveAsync(new XtreamCacheDocument { CacheVersion = 0 }, CancellationToken.None);
        var loaded = await cache.LoadAsync(CancellationToken.None);

        Assert.Equal(1, loaded.CacheVersion);
        Assert.False(File.Exists(Path.Combine(cacheDirectory, "xtream-cache.json.tmp")));
    }

    [Fact]
    public async Task CacheLoadFallsBackToBackupWhenPrimaryJsonIsCorrupt()
    {
        var cacheDirectory = Path.Combine(Path.GetTempPath(), "jellyxtreme-tests", Guid.NewGuid().ToString("N"));
        var cache = new XtreamCacheService(NullLogger<XtreamCacheService>.Instance, cacheDirectory);

        await cache.SaveAsync(new XtreamCacheDocument
        {
            LiveChannels = [new CachedLiveChannel { Name = "Known Good", StreamId = 12 }]
        }, CancellationToken.None);
        await cache.SaveAsync(new XtreamCacheDocument
        {
            LiveChannels = [new CachedLiveChannel { Name = "Current", StreamId = 99 }]
        }, CancellationToken.None);

        await File.WriteAllTextAsync(Path.Combine(cacheDirectory, "xtream-cache.json"), "{bad json", CancellationToken.None);
        var loaded = await cache.LoadAsync(CancellationToken.None);

        Assert.Single(loaded.LiveChannels);
        Assert.Equal("Known Good", loaded.LiveChannels[0].Name);
    }

    [Fact]
    public async Task SeriesProviderUsesCachedMetadataAndResolvesEpisodeStreamsDynamically()
    {
        var cacheDirectory = Path.Combine(Path.GetTempPath(), "jellyxtreme-tests", Guid.NewGuid().ToString("N"));
        var cache = new XtreamCacheService(NullLogger<XtreamCacheService>.Instance, cacheDirectory);
        await cache.SaveAsync(new XtreamCacheDocument
        {
            SeriesCategories = [new CachedCategory { CategoryId = "22", Name = "Drama", Kind = "series" }],
            SeriesItems =
            [
                new CachedSeriesItem
                {
                    Name = "Cached Series",
                    SeriesId = 77,
                    CategoryId = "22",
                    Poster = "https://poster.example/series.jpg",
                    Plot = "Cached plot",
                    Rating = 7.5,
                    Seasons =
                    [
                        new CachedSeason
                        {
                            SeasonNumber = 1,
                            Episodes =
                            [
                                new CachedEpisodeItem
                                {
                                    Title = "Pilot",
                                    StreamId = 88,
                                    EpisodeNumber = "1",
                                    ContainerExtension = "mp4",
                                    Plot = "Episode plot"
                                }
                            ]
                        }
                    ]
                }
            ]
        }, CancellationToken.None);

        var apiClient = new XtreamApiClient(new TestHttpClientFactory(), NullLogger<XtreamApiClient>.Instance);
        var provider = new SeriesProvider(
            cache,
            new StreamResolverService(apiClient),
            () => new PluginConfiguration
            {
                ServerUrl = "https://provider.example",
                Username = "user",
                Password = "secret"
            });

        var series = await provider.GetSeriesInfosAsync(CancellationToken.None);
        var episodes = await provider.GetEpisodesAsync(77, CancellationToken.None);
        var mediaSources = await provider.GetEpisodeMediaSourcesAsync(88, CancellationToken.None);

        Assert.Single(series);
        Assert.Equal("Cached Series", series[0].Name);
        Assert.Equal("Drama", series[0].CategoryName);
        Assert.Equal(1, series[0].EpisodeCount);
        Assert.Single(episodes);
        Assert.Equal("Pilot", episodes[0].Title);
        Assert.Single(mediaSources);
        Assert.Equal("https://provider.example/series/user/secret/88.mp4", mediaSources[0].Path);
        Assert.True(mediaSources[0].IsRemote);
    }

    [Fact]
    public async Task XmlTvCacheMapsProgramsToCachedChannelEpgIds()
    {
        var xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <tv>
              <channel id="news.example">
                <display-name>News HD</display-name>
              </channel>
              <programme start="20260613090000 +0000" stop="20260613100000 +0000" channel="news.example">
                <title>Morning News</title>
                <sub-title>Headlines</sub-title>
                <desc>Daily briefing</desc>
                <category>News</category>
                <icon src="https://images.example/news.jpg" />
              </programme>
              <programme start="20260613090000 +0000" stop="20260613100000 +0000" channel="other.example">
                <title>Other</title>
              </programme>
            </tv>
            """;
        var channel = new CachedLiveChannel
        {
            Name = "News HD",
            StreamId = 42,
            EpgChannelId = "news.example",
            GroupName = "News",
            CategoryId = "7"
        };

        var guide = XmlTvCacheService.Parse(xml, [channel]);

        Assert.Single(guide.Channels);
        Assert.Single(guide.Programs);
        Assert.Equal("news.example", guide.Channels[0].XmlTvChannelId);
        Assert.Equal("Morning News", guide.Programs[0].Title);
        Assert.Equal("Daily briefing", guide.Programs[0].Description);
        Assert.Equal("Headlines", guide.Programs[0].EpisodeTitle);

        var cacheDirectory = Path.Combine(Path.GetTempPath(), "jellyxtreme-tests", Guid.NewGuid().ToString("N"));
        var apiClient = new XtreamApiClient(new TestHttpClientFactory(), NullLogger<XtreamApiClient>.Instance);
        var cache = new XtreamCacheService(NullLogger<XtreamCacheService>.Instance, cacheDirectory);
        var xmlTvCache = new XmlTvCacheService(apiClient, cache, NullLogger<XmlTvCacheService>.Instance);
        await cache.SaveAsync(new XtreamCacheDocument { LiveChannels = [channel] }, CancellationToken.None);
        await File.WriteAllTextAsync(cache.GetXmlTvPath(), xml, CancellationToken.None);

        var provider = new JellyfinLiveTvProvider(
            cache,
            xmlTvCache,
            new StreamResolverService(apiClient),
            new TestHttpClientFactory(),
            NullLogger<JellyfinLiveTvProvider>.Instance,
            () => new PluginConfiguration());
        var programs = (await provider.GetProgramsAsync(
            new ListingsProviderInfo { Type = "jellyxtreme" },
            "42",
            new DateTime(2026, 6, 13, 8, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 13, 11, 0, 0, DateTimeKind.Utc),
            CancellationToken.None)).ToList();

        Assert.Single(programs);
        Assert.Equal("Morning News", programs[0].Name);
        Assert.Equal("42", programs[0].ChannelId);
        Assert.True(programs[0].IsNews);
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private static JellyxtremeApiController CreateController(string cacheDirectory)
    {
        var apiClient = new XtreamApiClient(new TestHttpClientFactory(), NullLogger<XtreamApiClient>.Instance);
        var cache = new XtreamCacheService(NullLogger<XtreamCacheService>.Instance, cacheDirectory);
        var resolver = new StreamResolverService(apiClient);
        return new JellyxtremeApiController(
            apiClient,
            new XtreamCacheRefreshService(
                apiClient,
                cache,
                new XmlTvCacheService(apiClient, cache, NullLogger<XmlTvCacheService>.Instance),
                NullLogger<XtreamCacheRefreshService>.Instance),
            cache,
            new VodProvider(cache, resolver, () => new PluginConfiguration()),
            new SeriesProvider(cache, resolver, () => new PluginConfiguration()),
            resolver,
            new TestHttpClientFactory(),
            NullLogger<JellyxtremeApiController>.Instance);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException("Could not locate repository root.");
        }

        return directory.FullName;
    }
}
