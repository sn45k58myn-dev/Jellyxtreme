using Jellyxtreme.Api;
using Jellyxtreme.Cache;
using Jellyxtreme.Configuration;
using Jellyxtreme.Providers;
using Jellyxtreme.Services;
using Microsoft.Extensions.Logging.Abstractions;
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

    [Theory]
    [InlineData("http://example.com", true)]
    [InlineData("https://example.com:8080", true)]
    [InlineData("ftp://example.com", false)]
    [InlineData("not-a-url", false)]
    public void ServerUrlValidationAllowsOnlyHttpAndHttps(string url, bool expected)
    {
        Assert.Equal(expected, XtreamApiClient.TryNormalizeServerUrl(url, out _));
    }

    [Fact]
    public void RedactRemovesAuthenticatedQuery()
    {
        var redacted = XtreamApiClient.Redact("https://provider.example/player_api.php?username=user&password=secret");

        Assert.Equal("https://provider.example/player_api.php?redacted=true", redacted);
        Assert.DoesNotContain("user", redacted);
        Assert.DoesNotContain("secret", redacted);
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
        Assert.Contains("category:7", channels[0].Tags);
        Assert.Single(mediaSources);
        Assert.Equal("https://provider.example/live/user/secret/42.ts", mediaSources[0].Path);
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
            () => new PluginConfiguration());
        var programs = (await provider.GetProgramsAsync("42", new DateTime(2026, 6, 13, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 6, 13, 11, 0, 0, DateTimeKind.Utc), CancellationToken.None)).ToList();

        Assert.Single(programs);
        Assert.Equal("Morning News", programs[0].Name);
        Assert.Equal("42", programs[0].ChannelId);
        Assert.True(programs[0].IsNews);
    }

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
