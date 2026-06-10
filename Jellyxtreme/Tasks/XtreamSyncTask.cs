using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyxtreme.Api;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyxtreme.Tasks
{
    public class XtreamSyncTask : IScheduledTask
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<XtreamSyncTask> _logger;

        public string Name => "Xtream Codes Sync";
        public string Key => "JellyxtremeSync";
        public string Description => "Syncs Movies, Series, and Live TV from Xtream Codes API";
        public string Category => "Jellyxtreme";

        public XtreamSyncTask(IHttpClientFactory httpClientFactory, ILogger<XtreamSyncTask> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        {
            return new[]
            {
                new TaskTriggerInfo
                {
                    Type = TaskTriggerInfo.TriggerInterval,
                    IntervalTicks = TimeSpan.FromHours(12).Ticks
                }
            };
        }

        public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        {
            var config = Plugin.Instance?.Configuration;
            if (config == null || string.IsNullOrWhiteSpace(config.ServerUrl) || string.IsNullOrWhiteSpace(config.Username) || string.IsNullOrWhiteSpace(config.Password))
            {
                _logger.LogWarning("Jellyxtreme plugin is not configured. Skipping sync.");
                return;
            }

            var apiClient = new XtreamApiClient(_httpClientFactory, config.ServerUrl, config.Username, config.Password);

            double currentProgress = 0;
            progress.Report(currentProgress);

            if (config.ImportMovies && !string.IsNullOrWhiteSpace(config.MoviesPath))
            {
                _logger.LogInformation("Starting Movies Sync...");
                await SyncMovies(apiClient, config.MoviesPath, cancellationToken);
            }
            currentProgress += 33.3;
            progress.Report(currentProgress);

            if (config.ImportSeries && !string.IsNullOrWhiteSpace(config.SeriesPath))
            {
                _logger.LogInformation("Starting Series Sync...");
                await SyncSeries(apiClient, config.SeriesPath, cancellationToken);
            }
            currentProgress += 33.3;
            progress.Report(currentProgress);

            if (config.ImportLiveTv && !string.IsNullOrWhiteSpace(config.LiveTvM3uPath))
            {
                _logger.LogInformation("Starting Live TV Sync...");
                await SyncLiveTv(apiClient, config.LiveTvM3uPath, cancellationToken);
            }

            progress.Report(100);
            _logger.LogInformation("Jellyxtreme sync completed.");
        }

        private async Task SyncMovies(XtreamApiClient apiClient, string moviesPath, CancellationToken cancellationToken)
        {
            try
            {
                var movies = await apiClient.GetVodStreamsAsync(cancellationToken);
                if (movies == null) return;

                Directory.CreateDirectory(moviesPath);

                foreach (var movie in movies)
                {
                    if (string.IsNullOrWhiteSpace(movie.name)) continue;

                    string safeName = SanitizeFileName(movie.name);
                    string movieDir = Path.Combine(moviesPath, safeName);
                    Directory.CreateDirectory(movieDir);

                    string ext = string.IsNullOrWhiteSpace(movie.container_extension) ? "mp4" : movie.container_extension;
                    string filePath = Path.Combine(movieDir, $"{safeName}.strm");

                    if (!File.Exists(filePath))
                    {
                        string streamUrl = apiClient.GetVodStreamUrl(movie.stream_id, ext);
                        await File.WriteAllTextAsync(filePath, streamUrl, cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing movies.");
            }
        }

        private async Task SyncSeries(XtreamApiClient apiClient, string seriesPath, CancellationToken cancellationToken)
        {
            try
            {
                var seriesList = await apiClient.GetSeriesAsync(cancellationToken);
                if (seriesList == null) return;

                Directory.CreateDirectory(seriesPath);

                foreach (var series in seriesList)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(series.name)) continue;

                    string safeSeriesName = SanitizeFileName(series.name);
                    string seriesDir = Path.Combine(seriesPath, safeSeriesName);

                    try
                    {
                        var seriesInfo = await apiClient.GetSeriesInfoAsync(series.series_id, cancellationToken);
                        if (seriesInfo?.episodes == null) continue;

                        Directory.CreateDirectory(seriesDir);

                        foreach (var seasonKvp in seriesInfo.episodes)
                        {
                            if (!int.TryParse(seasonKvp.Key, out int seasonNum)) continue;

                            string seasonDir = Path.Combine(seriesDir, $"Season {seasonNum:D2}");
                            Directory.CreateDirectory(seasonDir);

                            foreach (var episode in seasonKvp.Value)
                            {
                                if (string.IsNullOrWhiteSpace(episode.id)) continue;
                                if (!int.TryParse(episode.id, out int streamId)) continue;

                                string ext = string.IsNullOrWhiteSpace(episode.container_extension) ? "mp4" : episode.container_extension;
                                string safeEpTitle = SanitizeFileName(episode.title ?? $"Episode {episode.episode_num}");

                                string filePath = Path.Combine(seasonDir, $"{safeSeriesName} S{seasonNum:D2}E{episode.episode_num} - {safeEpTitle}.strm");

                                if (!File.Exists(filePath))
                                {
                                    string streamUrl = apiClient.GetSeriesStreamUrl(streamId, ext);
                                    await File.WriteAllTextAsync(filePath, streamUrl, cancellationToken);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error syncing series {SeriesName}.", series.name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing series.");
            }
        }

        private async Task SyncLiveTv(XtreamApiClient apiClient, string m3uPath, CancellationToken cancellationToken)
        {
            try
            {
                var liveStreams = await apiClient.GetLiveStreamsAsync(cancellationToken);
                if (liveStreams == null) return;

                var sb = new StringBuilder();
                sb.AppendLine("#EXTM3U");

                foreach (var stream in liveStreams)
                {
                    if (string.IsNullOrWhiteSpace(stream.name)) continue;

                    string logo = string.IsNullOrWhiteSpace(stream.stream_icon) ? "" : $" tvg-logo=\"{stream.stream_icon}\"";
                    string group = string.IsNullOrWhiteSpace(stream.category_id) ? "" : $" group-title=\"{stream.category_id}\"";

                    sb.AppendLine($"#EXTINF:-1{logo}{group},{stream.name}");
                    sb.AppendLine(apiClient.GetLiveStreamUrl(stream.stream_id, "ts"));
                }

                string? dir = Path.GetDirectoryName(m3uPath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                await File.WriteAllTextAsync(m3uPath, sb.ToString(), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing live TV.");
            }
        }

        private string SanitizeFileName(string name)
        {
            string invalidChars = new string(Path.GetInvalidFileNameChars()) + new string(Path.GetInvalidPathChars());
            string regexPattern = string.Format("[{0}]", Regex.Escape(invalidChars));
            return Regex.Replace(name, regexPattern, "_").Trim();
        }
    }
}
