namespace Jellyxtreme.Cache;

public sealed class XtreamCacheDocument
{
    public DateTimeOffset RefreshedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<CachedCategory> LiveCategories { get; set; } = [];
    public List<CachedCategory> VodCategories { get; set; } = [];
    public List<CachedCategory> SeriesCategories { get; set; } = [];
    public List<CachedLiveChannel> LiveChannels { get; set; } = [];
    public List<CachedVodMovie> VodMovies { get; set; } = [];
    public List<CachedSeries> Series { get; set; } = [];
}

public sealed class CachedCategory
{
    public string CategoryId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
}

public sealed class CachedLiveChannel
{
    public string Name { get; set; } = string.Empty;
    public int StreamId { get; set; }
    public string? Logo { get; set; }
    public string? EpgChannelId { get; set; }
    public string CategoryId { get; set; } = string.Empty;
    public string GroupName { get; set; } = string.Empty;
    public string StreamExtension { get; set; } = "ts";
}

public sealed class CachedVodMovie
{
    public string Name { get; set; } = string.Empty;
    public int StreamId { get; set; }
    public string CategoryId { get; set; } = string.Empty;
    public string? Poster { get; set; }
    public double? Rating { get; set; }
    public string ContainerExtension { get; set; } = "mp4";
    public string? Added { get; set; }
}

public sealed class CachedSeries
{
    public string Name { get; set; } = string.Empty;
    public int SeriesId { get; set; }
    public string CategoryId { get; set; } = string.Empty;
    public string? Poster { get; set; }
    public string? Plot { get; set; }
    public double? Rating { get; set; }
    public List<CachedSeason> Seasons { get; set; } = [];
}

public sealed class CachedSeason
{
    public int SeasonNumber { get; set; }
    public List<CachedEpisode> Episodes { get; set; } = [];
}

public sealed class CachedEpisode
{
    public string Title { get; set; } = string.Empty;
    public int StreamId { get; set; }
    public string? EpisodeNumber { get; set; }
    public string ContainerExtension { get; set; } = "mp4";
    public string? Poster { get; set; }
    public string? Plot { get; set; }
    public string? ReleaseDate { get; set; }
}
