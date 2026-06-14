using Jellyxtreme.Api;
using Jellyxtreme.Cache;
using Jellyxtreme.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;

namespace Jellyxtreme.Services;

public sealed class XtreamCacheRefreshService
{
    private const int SeriesInfoConcurrency = 5;
    private const int SeriesInfoBatchSize = 25;
    private const int MetadataEnrichmentConcurrency = 4;

    private readonly XtreamApiClient _apiClient;
    private readonly XtreamCacheService _cacheService;
    private readonly XmlTvCacheService _xmlTvCacheService;
    private readonly MetadataEnrichmentService _metadataEnrichmentService;
    private readonly ICredentialStore _credentialStore;
    private readonly ILogger<XtreamCacheRefreshService> _logger;

    public XtreamCacheRefreshService(
        XtreamApiClient apiClient,
        XtreamCacheService cacheService,
        XmlTvCacheService xmlTvCacheService,
        MetadataEnrichmentService metadataEnrichmentService,
        ICredentialStore credentialStore,
        ILogger<XtreamCacheRefreshService> logger)
    {
        _apiClient = apiClient;
        _cacheService = cacheService;
        _xmlTvCacheService = xmlTvCacheService;
        _metadataEnrichmentService = metadataEnrichmentService;
        _credentialStore = credentialStore;
        _logger = logger;
    }

    public async Task<XtreamCacheDocument> RefreshAsync(
        PluginConfiguration config,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        progress?.Report(0);

        try
        {
            var providers = _credentialStore.GetConfiguredProviderIds(config).ToList();
            if (providers.Count == 0)
            {
                stopwatch.Stop();
                config.LastSyncDurationMs = stopwatch.ElapsedMilliseconds;
                config.LastSyncError = "JellyXtreme plugin is not configured.";
                Plugin.Instance?.SaveConfiguration();
                _logger.LogWarning("JellyXtreme is not configured. Skipping cache refresh.");
                return new XtreamCacheDocument();
            }

            var existingDocument = await _cacheService.LoadAsync(cancellationToken).ConfigureAwait(false);
            var document = new XtreamCacheDocument { RefreshedAt = DateTimeOffset.UtcNow };
            var skippedSections = new List<string>();

            var liveCategoryMap = new Dictionary<string, CachedCategory>(StringComparer.OrdinalIgnoreCase);
            var vodCategoryMap = new Dictionary<string, CachedCategory>(StringComparer.OrdinalIgnoreCase);
            var seriesCategoryMap = new Dictionary<string, CachedCategory>(StringComparer.OrdinalIgnoreCase);
            var liveChannelMap = new Dictionary<string, CachedLiveChannel>(StringComparer.OrdinalIgnoreCase);
            var vodItemMap = new Dictionary<string, CachedVodItem>(StringComparer.OrdinalIgnoreCase);
            var seriesItemMap = new Dictionary<string, CachedSeriesItem>(StringComparer.OrdinalIgnoreCase);

            var selectedLive = NormalizeSelectionSet(config.SelectedLiveCategoryIds);
            var selectedVod = NormalizeSelectionSet(config.SelectedVodCategoryIds);
            var selectedSeries = NormalizeSelectionSet(config.SelectedSeriesCategoryIds);

            var sectionProgressStep = providers.Count > 0 ? 90 / providers.Count : 90;

            for (var providerIndex = 0; providerIndex < providers.Count; providerIndex++)
            {
                var providerId = providers[providerIndex];
                var normalizedProviderId = XtreamCacheIdentity.NormalizeProviderId(providerId);
                var settings = _credentialStore.GetConnectionSettings(config, normalizedProviderId);

                cancellationToken.ThrowIfCancellationRequested();

                var providerLive = await _apiClient.GetLiveCategoriesAsync(settings, cancellationToken).ConfigureAwait(false);
                var providerVod = await _apiClient.GetVodCategoriesAsync(settings, cancellationToken).ConfigureAwait(false);
                var providerSeries = await _apiClient.GetSeriesCategoriesAsync(settings, cancellationToken).ConfigureAwait(false);

                AddCachedCategories(liveCategoryMap, ToCachedCategories(providerLive, normalizedProviderId, "live"));
                AddCachedCategories(vodCategoryMap, ToCachedCategories(providerVod, normalizedProviderId, "vod"));
                AddCachedCategories(seriesCategoryMap, ToCachedCategories(providerSeries, normalizedProviderId, "series"));

                if (XtreamSelectionFilter.ShouldCacheSection(config.EnableLiveTv, selectedLive))
                {
                    var providerLiveChannels = await RefreshLiveAsync(
                        _apiClient,
                        settings,
                        normalizedProviderId,
                        liveCategoryMap.Values.Where(category => string.Equals(category.ProviderId, normalizedProviderId, StringComparison.OrdinalIgnoreCase)).ToList(),
                        selectedLive,
                        cancellationToken).ConfigureAwait(false);

                    AddCachedItems(liveChannelMap, providerLiveChannels, channel => XtreamCacheIdentity.BuildItemKey(channel.ProviderId, channel.StreamId));
                }
                else if (providerIndex == 0)
                {
                    if (selectedLive.Count == 0)
                    {
                        skippedSections.Add("Live TV (all providers skipped - no category selection)");
                    }
                    else
                    {
                        skippedSections.Add("Live TV (disabled)");
                    }
                }

                if (XtreamSelectionFilter.ShouldCacheSection(config.EnableVod, selectedVod))
                {
                    var providerVodItems = await RefreshVodAsync(
                        _apiClient,
                        settings,
                        normalizedProviderId,
                        selectedVod,
                        cancellationToken).ConfigureAwait(false);
                    await EnrichVodMetadataAsync(providerVodItems, config, cancellationToken).ConfigureAwait(false);

                    AddCachedItems(vodItemMap, providerVodItems, item => XtreamCacheIdentity.BuildItemKey(item.ProviderId, item.StreamId));
                }
                else if (providerIndex == 0)
                {
                    if (selectedVod.Count == 0)
                    {
                        skippedSections.Add("VOD (all providers skipped - no category selection)");
                    }
                    else
                    {
                        skippedSections.Add("VOD (disabled)");
                    }
                }

                if (XtreamSelectionFilter.ShouldCacheSection(config.EnableSeries, selectedSeries))
                {
                    var existingSeriesByProvider = existingDocument.SeriesItems
                    .Where(item => string.Equals(item.ProviderId, normalizedProviderId, StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(item => XtreamCacheIdentity.BuildItemKey(item.ProviderId, item.SeriesId), item => item, StringComparer.OrdinalIgnoreCase);

                    var providerSeriesItems = await RefreshSeriesAsync(
                        _apiClient,
                        settings,
                        normalizedProviderId,
                        selectedSeries,
                        existingSeriesByProvider,
                        cancellationToken).ConfigureAwait(false);
                    await EnrichSeriesMetadataAsync(providerSeriesItems, config, cancellationToken).ConfigureAwait(false);

                    AddCachedItems(seriesItemMap, providerSeriesItems, item => XtreamCacheIdentity.BuildItemKey(item.ProviderId, item.SeriesId));
                }
                else if (providerIndex == 0)
                {
                    if (selectedSeries.Count == 0)
                    {
                        skippedSections.Add("Series (all providers skipped - no category selection)");
                    }
                    else
                    {
                        skippedSections.Add("Series (disabled)");
                    }
                }

                progress?.Report(15 + (providerIndex + 1) * sectionProgressStep);
            }

            document.LiveCategories = liveCategoryMap.Values.OrderBy(category => category.Name, StringComparer.OrdinalIgnoreCase).ToList();
            document.VodCategories = vodCategoryMap.Values.OrderBy(category => category.Name, StringComparer.OrdinalIgnoreCase).ToList();
            document.SeriesCategories = seriesCategoryMap.Values.OrderBy(category => category.Name, StringComparer.OrdinalIgnoreCase).ToList();
            document.LiveChannels = liveChannelMap.Values.ToList();
            document.VodItems = vodItemMap.Values.ToList();
            document.SeriesItems = seriesItemMap.Values.ToList();

            if (document.LiveChannels.Count > 0)
            {
                document.XmlTv = await _xmlTvCacheService.DownloadAndCacheAsync(
                    _credentialStore.GetConnectionSettings(config, null),
                    document.LiveChannels,
                    cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(95);
            await _cacheService.SaveAsync(document, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            config.LastSuccessfulSyncUtc = document.RefreshedAt;
            config.LastSyncDurationMs = stopwatch.ElapsedMilliseconds;
            config.LastSyncError = string.Empty;
            Plugin.Instance?.SaveConfiguration();
            _logger.LogInformation(
                "JellyXtreme refresh imported {LiveCount} live channels, {VodCount} VOD items, {SeriesCount} series, and {EpisodeCount} episodes in {DurationMs} ms. Skipped sections: {SkippedSections}.",
                document.LiveChannels.Count,
                document.VodItems.Count,
                document.SeriesItems.Count,
                document.SeriesItems.Sum(series => series.Seasons.Sum(season => season.Episodes.Count)),
                stopwatch.ElapsedMilliseconds,
                skippedSections.Count == 0 ? "none" : string.Join(", ", skippedSections));
            progress?.Report(100);
            return document;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            config.LastSyncDurationMs = stopwatch.ElapsedMilliseconds;
            config.LastSyncError = SanitizeError(ex);
            try
            {
                await _cacheService.RollbackToBackupAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception rollbackEx)
            {
                _logger.LogWarning(rollbackEx, "JellyXtreme cache rollback to backup failed.");
            }

            try
            {
                await _cacheService.MarkRefreshFailureAsync(config.LastSyncError, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception markFailureEx)
            {
                _logger.LogWarning(markFailureEx, "JellyXtreme cache failure metadata update failed.");
            }

            Plugin.Instance?.SaveConfiguration();
            _logger.LogError("JellyXtreme cache refresh failed after {DurationMs} ms: {Error}", stopwatch.ElapsedMilliseconds, config.LastSyncError);
            throw;
        }
    }

    public async Task<XtreamCategorySnapshot> GetCategoriesAsync(PluginConfiguration config, CancellationToken cancellationToken)
    {
        var providers = _credentialStore.GetConfiguredProviderIds(config).ToList();
        if (providers.Count == 0)
        {
            return new XtreamCategorySnapshot([], [], []);
        }

        var liveCategories = new List<CachedCategory>();
        var vodCategories = new List<CachedCategory>();
        var seriesCategories = new List<CachedCategory>();

        foreach (var providerId in providers)
        {
            var settings = _credentialStore.GetConnectionSettings(config, providerId);

            var live = await _apiClient.GetLiveCategoriesAsync(settings, cancellationToken).ConfigureAwait(false);
            var vod = await _apiClient.GetVodCategoriesAsync(settings, cancellationToken).ConfigureAwait(false);
            var series = await _apiClient.GetSeriesCategoriesAsync(settings, cancellationToken).ConfigureAwait(false);

            AddCachedCategories(liveCategories, ToCachedCategories(live, XtreamCacheIdentity.NormalizeProviderId(providerId), "live"));
            AddCachedCategories(vodCategories, ToCachedCategories(vod, XtreamCacheIdentity.NormalizeProviderId(providerId), "vod"));
            AddCachedCategories(seriesCategories, ToCachedCategories(series, XtreamCacheIdentity.NormalizeProviderId(providerId), "series"));
        }

        return new XtreamCategorySnapshot(
            liveCategories.OrderBy(category => category.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            vodCategories.OrderBy(category => category.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            seriesCategories.OrderBy(category => category.Name, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static void AddCachedCategories(List<CachedCategory> destination, IReadOnlyList<CachedCategory> categories)
    {
        foreach (var category in categories)
        {
            var key = XtreamCacheIdentity.BuildItemKey(category.ProviderId, category.CategoryId);
            var existingIndex = destination.FindIndex(item => XtreamCacheIdentity.BuildItemKey(item.ProviderId, item.CategoryId).Equals(key, StringComparison.OrdinalIgnoreCase));
            if (existingIndex < 0)
            {
                destination.Add(category);
            }
        }
    }

    private static void AddCachedItems<TItem>(Dictionary<string, TItem> destination, IReadOnlyList<TItem> items, Func<TItem, string> keyFactory)
    {
        foreach (var item in items)
        {
            destination[keyFactory(item)] = item;
        }
    }

    private static IReadOnlyCollection<string> NormalizeSelectionSet(IEnumerable<string>? selected)
    {
        if (selected is null)
        {
            return [];
        }

        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in selected)
        {
            var normalizedValue = XtreamSelectionFilter.NormalizeSelectionId(value);
            if (!string.IsNullOrWhiteSpace(normalizedValue))
            {
                normalized.Add(normalizedValue);
            }
        }

        return normalized.ToList();
    }

    private static string SanitizeError(Exception exception)
    {
        var message = XtreamApiClient.Redact(exception.Message);
        const int maxLength = 500;
        if (message.Length > maxLength)
        {
            message = message[..maxLength];
        }

        return $"{exception.GetType().Name}: {message}";
    }

    private static List<CachedCategory> ToCachedCategories(IEnumerable<XtreamCategory> categories, string providerId, string kind)
        => categories
            .Where(category => !string.IsNullOrWhiteSpace(category.CategoryId))
            .Select(category => new CachedCategory
            {
                ProviderId = providerId,
                CategoryId = category.CategoryId,
                Name = category.CategoryName,
                Kind = kind
            })
            .ToList();

    private static async Task<List<CachedLiveChannel>> RefreshLiveAsync(
        XtreamApiClient client,
        XtreamConnectionSettings settings,
        string providerId,
        IReadOnlyCollection<CachedCategory> categories,
        IReadOnlyCollection<string> selectedCategoryIds,
        CancellationToken cancellationToken)
    {
        var categoryNames = categories.ToDictionary(category => category.CategoryId, category => category.Name, StringComparer.OrdinalIgnoreCase);
        var streams = await client.GetLiveStreamsAsync(settings, cancellationToken).ConfigureAwait(false);

        return streams
            .Where(stream => XtreamSelectionFilter.IsCategorySelected(providerId, stream.CategoryId, selectedCategoryIds))
            .Where(stream => stream.StreamId > 0 && !string.IsNullOrWhiteSpace(stream.Name))
            .Select(stream => new CachedLiveChannel
            {
                ProviderId = providerId,
                Name = stream.Name!,
                StreamId = stream.StreamId,
                Logo = stream.StreamIcon,
                EpgChannelId = stream.EpgChannelId,
                CategoryId = stream.CategoryId!,
                GroupName = categoryNames.GetValueOrDefault(stream.CategoryId!, stream.CategoryId!),
                StreamExtension = "ts"
            })
            .ToList();
    }

    private static async Task<List<CachedVodItem>> RefreshVodAsync(
        XtreamApiClient client,
        XtreamConnectionSettings settings,
        string providerId,
        IReadOnlyCollection<string> selectedCategoryIds,
        CancellationToken cancellationToken)
    {
        var streams = await client.GetVodStreamsAsync(settings, cancellationToken).ConfigureAwait(false);

        return streams
            .Where(stream => XtreamSelectionFilter.IsCategorySelected(providerId, stream.CategoryId, selectedCategoryIds))
            .Where(stream => stream.StreamId > 0 && !string.IsNullOrWhiteSpace(stream.Name))
            .Select(stream => new CachedVodItem
            {
                ProviderId = providerId,
                Name = stream.Name!,
                StreamId = stream.StreamId,
                CategoryId = stream.CategoryId!,
                Poster = stream.StreamIcon,
                Rating = stream.Rating,
                ContainerExtension = string.IsNullOrWhiteSpace(stream.ContainerExtension) ? "mp4" : stream.ContainerExtension,
                Added = stream.Added
            })
            .ToList();
    }

    private async Task EnrichVodMetadataAsync(
        IReadOnlyCollection<CachedVodItem> vodItems,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        if (!config.EnableMetadataEnrichment || vodItems.Count == 0)
        {
            return;
        }

        using var throttle = new SemaphoreSlim(MetadataEnrichmentConcurrency, MetadataEnrichmentConcurrency);
        var tasks = vodItems.Select(async vod =>
        {
            await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _metadataEnrichmentService.EnrichVodMetadataAsync(vod, config, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                throttle.Release();
            }
        }).ToList();

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("JellyXtreme metadata enrichment for VOD items was canceled.");
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning("JellyXtreme metadata enrichment for VOD items encountered an error: {Message}", XtreamApiClient.Redact(exception.Message));
        }
    }

    private async Task EnrichSeriesMetadataAsync(
        IReadOnlyCollection<CachedSeriesItem> seriesItems,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        if (!config.EnableMetadataEnrichment || seriesItems.Count == 0)
        {
            return;
        }

        using var throttle = new SemaphoreSlim(MetadataEnrichmentConcurrency, MetadataEnrichmentConcurrency);
        var tasks = seriesItems.Select(async series =>
        {
            await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _metadataEnrichmentService.EnrichSeriesMetadataAsync(series, config, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                throttle.Release();
            }
        }).ToList();

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("JellyXtreme metadata enrichment for series items was canceled.");
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogWarning("JellyXtreme metadata enrichment for series items encountered an error: {Message}", XtreamApiClient.Redact(exception.Message));
        }
    }

    private async Task<List<CachedSeriesItem>> RefreshSeriesAsync(
        XtreamApiClient client,
        XtreamConnectionSettings settings,
        string providerId,
        IReadOnlyCollection<string> selectedCategoryIds,
        Dictionary<string, CachedSeriesItem> existingSeriesByProvider,
        CancellationToken cancellationToken)
    {
        var seriesList = await client.GetSeriesAsync(settings, cancellationToken).ConfigureAwait(false);
        var validSeries = seriesList
            .Where(series => series.SeriesId > 0 && !string.IsNullOrWhiteSpace(series.Name))
            .Where(series => XtreamSelectionFilter.IsCategorySelected(providerId, series.CategoryId, selectedCategoryIds))
            .ToList();

        if (validSeries.Count == 0)
        {
            _logger.LogInformation("JellyXtreme series sync skipped because no series were selected for provider {ProviderId}.", providerId);
            return [];
        }

        var changedSeries = new List<XtreamSeries>();
        var unchangedSeries = 0;

        foreach (var series in validSeries)
        {
            var key = XtreamCacheIdentity.BuildItemKey(providerId, series.SeriesId);
            var existing = existingSeriesByProvider.GetValueOrDefault(key);
            var baseFingerprint = BuildSeriesBaseFingerprint(series);
            var hasChangeFromBase = existing is null
                || !string.Equals(existing.BaseFingerprint, baseFingerprint, StringComparison.Ordinal);

            var hasChangeFromLastModified = !HasMatchingSeriesLastModified(series, existing);
            if (existing is null
                || existing.Seasons.Count == 0
                || string.IsNullOrWhiteSpace(existing.EpisodeFingerprint)
                || hasChangeFromBase
                || hasChangeFromLastModified)
            {
                changedSeries.Add(series);
            }
            else
            {
                unchangedSeries++;
            }
        }

        var refreshedSeries = await RefreshSeriesInfoBatchAsync(
            client,
            settings,
            providerId,
            changedSeries,
            cancellationToken).ConfigureAwait(false);

        var results = new List<CachedSeriesItem>(capacity: validSeries.Count);
        var importedSeries = 0;
        var skippedSeries = 0;
        var droppedSeries = 0;

        foreach (var series in validSeries)
        {
            var key = XtreamCacheIdentity.BuildItemKey(providerId, series.SeriesId);
            if (refreshedSeries.TryGetValue(key, out var cachedSeries))
            {
                cachedSeries.BaseFingerprint = BuildSeriesBaseFingerprint(series);
                cachedSeries.ProviderId = providerId;
                importedSeries++;
                results.Add(cachedSeries);
                continue;
            }

            var existingSeries = existingSeriesByProvider.GetValueOrDefault(key);
            if (existingSeries is not null)
            {
                skippedSeries++;
                results.Add(existingSeries);
                continue;
            }

            droppedSeries++;
            _logger.LogWarning("JellyXtreme dropped series {SeriesId} from provider {ProviderId} because series info could not be refreshed.", series.SeriesId, providerId);
        }

        if (droppedSeries > 0)
        {
            _logger.LogWarning(
                "JellyXtreme skipped {DroppedCount} selected series after metadata fetch failure because no cache copy existed for provider {ProviderId}.",
                droppedSeries,
                providerId);
        }

        _logger.LogInformation(
            "JellyXtreme series sync summary for provider {ProviderId}: {ImportedCount} imported/updated, {UnchangedCount} unchanged, {SkippedCount} restored from cache.",
            providerId,
            importedSeries,
            unchangedSeries,
            skippedSeries);

        return results;
    }

    private async Task<Dictionary<string, CachedSeriesItem>> RefreshSeriesInfoBatchAsync(
        XtreamApiClient client,
        XtreamConnectionSettings settings,
        string providerId,
        List<XtreamSeries> changedSeries,
        CancellationToken cancellationToken)
    {
        if (changedSeries.Count == 0)
        {
            return [];
        }

        _logger.LogInformation("JellyXtreme refreshing {ChangedCount} series from Xtream metadata endpoint for provider {ProviderId}.", changedSeries.Count, providerId);

        var refreshedSeries = new Dictionary<string, CachedSeriesItem>(changedSeries.Count);
        var totalFetched = 0;
        for (var startIndex = 0; startIndex < changedSeries.Count; startIndex += SeriesInfoBatchSize)
        {
            var batch = changedSeries
                .Skip(startIndex)
                .Take(SeriesInfoBatchSize)
                .Select(series => series)
                .ToList();

            using var throttle = new SemaphoreSlim(SeriesInfoConcurrency, SeriesInfoConcurrency);
            var completed = 0;
            var tasks = batch.Select(series => FetchSeriesInfoAsync(
                client,
                settings,
                providerId,
                series,
                throttle,
                batch.Count,
                () => Interlocked.Increment(ref completed),
                cancellationToken)).ToList();

            var seriesItems = await Task.WhenAll(tasks).ConfigureAwait(false);
            var fetchedInBatch = seriesItems.Count(item => item is not null);
            totalFetched += fetchedInBatch;
            foreach (var item in seriesItems.Where(item => item is not null))
            {
                refreshedSeries[XtreamCacheIdentity.BuildItemKey(providerId, item.SeriesId)] = item;
            }

            _logger.LogInformation(
                "JellyXtreme completed batch {Batch} for series metadata for provider {ProviderId}. Total completed: {Completed}/{Total}",
                (startIndex / SeriesInfoBatchSize) + 1,
                providerId,
                totalFetched,
                changedSeries.Count);
        }

        return refreshedSeries;
    }

    private async Task<CachedSeriesItem?> FetchSeriesInfoAsync(
        XtreamApiClient client,
        XtreamConnectionSettings settings,
        string providerId,
        XtreamSeries series,
        SemaphoreSlim throttle,
        int batchCount,
        Func<int> incrementCompleted,
        CancellationToken cancellationToken)
    {
        await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = await client.GetSeriesInfoAsync(settings, series.SeriesId, cancellationToken).ConfigureAwait(false);
            var completed = incrementCompleted();

            if (completed % 25 == 0 || completed == batchCount)
            {
                _logger.LogInformation("JellyXtreme fetched series metadata for {CompletedCount}/{TotalCount} in current batch for provider {ProviderId}.", completed, batchCount, providerId);
            }

            return new CachedSeriesItem
            {
                ProviderId = providerId,
                Name = series.Name!,
                SeriesId = series.SeriesId,
                CategoryId = series.CategoryId!,
                Poster = series.Cover,
                Plot = info?.Info?.Plot ?? series.Plot,
                Rating = series.Rating,
                BaseFingerprint = BuildSeriesBaseFingerprint(series),
                EpisodeFingerprint = BuildEpisodeFingerprint(info),
                LastInfoModifiedUtc = ParseLastModified(info?.LastModified) ?? ParseLastModified(series.LastModified),
                LastInfoFetchedUtc = DateTimeOffset.UtcNow,
                Seasons = ToCachedSeasons(info, providerId)
            };
        }
        catch (Exception ex) when (IsSeriesMetadataFailure(ex, cancellationToken))
        {
            _logger.LogWarning("Skipping Xtream series {SeriesId} from provider {ProviderId} after metadata fetch failure: {Error}", series.SeriesId, providerId, SanitizeError(ex));
            return null;
        }
        finally
        {
            throttle.Release();
        }
    }

    private static bool HasMatchingSeriesLastModified(XtreamSeries series, CachedSeriesItem? existingSeries)
    {
        var latestModified = ParseLastModified(series.LastModified);
        if (latestModified is null || existingSeries?.LastInfoModifiedUtc is null)
        {
            return false;
        }

        return latestModified == existingSeries.LastInfoModifiedUtc;
    }

    private static string BuildSeriesBaseFingerprint(XtreamSeries series)
    {
        return string.Join("|", new[]
        {
            series.SeriesId.ToString(CultureInfo.InvariantCulture),
            series.CategoryId ?? string.Empty,
            series.Name ?? string.Empty,
            series.Cover ?? string.Empty,
            series.Plot ?? string.Empty,
            series.Rating?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
        });
    }

    private static string BuildEpisodeFingerprint(XtreamSeriesInfoResponse? info)
    {
        if (info?.Episodes is null)
        {
            return string.Empty;
        }

        var seasonEntries = info.Episodes
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry =>
            {
                var episodes = entry.Value
                    .Where(episode => int.TryParse(episode.Id, out _))
                    .OrderBy(episode => episode.Id, StringComparer.Ordinal)
                    .Select(episode => string.Join("|", new[]
                    {
                        episode.Id ?? string.Empty,
                        episode.Season.ToString(CultureInfo.InvariantCulture),
                        episode.EpisodeNumber ?? string.Empty,
                        episode.Title ?? string.Empty,
                        episode.ContainerExtension ?? string.Empty,
                        episode.Info?.MovieImage ?? string.Empty,
                        episode.Info?.Plot ?? string.Empty,
                        episode.Info?.ReleaseDate ?? string.Empty
                    }));

                return $"{entry.Key}:{string.Join(",", episodes)}";
            });

        return string.Join(";", seasonEntries);
    }

    private static DateTimeOffset? ParseLastModified(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds))
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool IsSeriesMetadataFailure(Exception exception, CancellationToken cancellationToken)
        => exception is HttpRequestException
            || exception is System.Text.Json.JsonException
            || exception is TaskCanceledException && !cancellationToken.IsCancellationRequested;

    private static List<CachedSeason> ToCachedSeasons(XtreamSeriesInfoResponse? info, string providerId)
    {
        if (info?.Episodes is null)
        {
            return [];
        }

        return info.Episodes
            .Select(season =>
            {
                _ = int.TryParse(season.Key, out var seasonNumber);
                return new CachedSeason
                {
                    SeasonNumber = seasonNumber,
                    Episodes = season.Value
                        .Where(episode => int.TryParse(episode.Id, out _))
                        .Select(episode =>
                        {
                            _ = int.TryParse(episode.Id, out var streamId);
                            return new CachedEpisodeItem
                            {
                                ProviderId = providerId,
                                Title = string.IsNullOrWhiteSpace(episode.Title) ? $"Episode {episode.EpisodeNumber}" : episode.Title!,
                                StreamId = streamId,
                                EpisodeNumber = episode.EpisodeNumber,
                                ContainerExtension = string.IsNullOrWhiteSpace(episode.ContainerExtension) ? "mp4" : episode.ContainerExtension,
                                Poster = episode.Info?.MovieImage,
                                Plot = episode.Info?.Plot,
                                ReleaseDate = episode.Info?.ReleaseDate
                            };
                        })
                        .ToList()
                };
            })
            .ToList();
    }
}

public sealed record XtreamCategorySnapshot(
    IReadOnlyList<CachedCategory> Live,
    IReadOnlyList<CachedCategory> Vod,
    IReadOnlyList<CachedCategory> Series);
