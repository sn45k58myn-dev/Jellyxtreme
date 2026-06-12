using Jellyxtreme.Api;
using Jellyxtreme.Configuration;
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
            NullLogger<XtreamApiClient>.Instance,
            "https://provider.example",
            "user name",
            "pass word");

        Assert.Equal("https://provider.example/live/user%20name/pass%20word/123.ts", client.GetLiveStreamUrl(123));
        Assert.Equal("https://provider.example/movie/user%20name/pass%20word/456.mkv", client.GetVodStreamUrl(456, "mkv"));
        Assert.Equal("https://provider.example/series/user%20name/pass%20word/789.mp4", client.GetSeriesStreamUrl(789));
    }

    [Fact]
    public void StreamResolverBuildsExpectedPlaybackPaths()
    {
        var resolver = new StreamResolverService(new TestHttpClientFactory(), NullLoggerFactory.Instance);
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

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
