using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyxtreme.Api
{
    public class XtreamApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _serverUrl;
        private readonly string _username;
        private readonly string _password;

        public XtreamApiClient(IHttpClientFactory httpClientFactory, string serverUrl, string username, string password)
        {
            _httpClient = httpClientFactory.CreateClient();
            _serverUrl = serverUrl.TrimEnd('/');
            _username = username;
            _password = password;
        }

        private string GetBaseApiUrl()
        {
            return $"{_serverUrl}/player_api.php?username={Uri.EscapeDataString(_username)}&password={Uri.EscapeDataString(_password)}";
        }

        public async Task<List<LiveStreamItem>?> GetLiveStreamsAsync(CancellationToken cancellationToken)
        {
            var url = $"{GetBaseApiUrl()}&action=get_live_streams";
            return await _httpClient.GetFromJsonAsync<List<LiveStreamItem>>(url, cancellationToken);
        }

        public async Task<List<VodStreamItem>?> GetVodStreamsAsync(CancellationToken cancellationToken)
        {
            var url = $"{GetBaseApiUrl()}&action=get_vod_streams";
            return await _httpClient.GetFromJsonAsync<List<VodStreamItem>>(url, cancellationToken);
        }

        public async Task<List<SeriesItem>?> GetSeriesAsync(CancellationToken cancellationToken)
        {
            var url = $"{GetBaseApiUrl()}&action=get_series";
            return await _httpClient.GetFromJsonAsync<List<SeriesItem>>(url, cancellationToken);
        }

        public async Task<SeriesInfoResponse?> GetSeriesInfoAsync(int seriesId, CancellationToken cancellationToken)
        {
            var url = $"{GetBaseApiUrl()}&action=get_series_info&series_id={seriesId}";
            return await _httpClient.GetFromJsonAsync<SeriesInfoResponse>(url, cancellationToken);
        }

        public string GetLiveStreamUrl(int streamId, string extension = "ts")
        {
            return $"{_serverUrl}/live/{_username}/{_password}/{streamId}.{extension}";
        }

        public string GetVodStreamUrl(int streamId, string extension = "mp4")
        {
            return $"{_serverUrl}/movie/{_username}/{_password}/{streamId}.{extension}";
        }

        public string GetSeriesStreamUrl(int streamId, string extension = "mp4")
        {
            return $"{_serverUrl}/series/{_username}/{_password}/{streamId}.{extension}";
        }
    }

    public class LiveStreamItem
    {
        public int num { get; set; }
        public string? name { get; set; }
        public int stream_id { get; set; }
        public string? stream_icon { get; set; }
        public string? epg_channel_id { get; set; }
        public string? added { get; set; }
        public string? category_id { get; set; }
        public string? custom_sid { get; set; }
        public int tv_archive { get; set; }
        public string? direct_source { get; set; }
        public int tv_archive_duration { get; set; }
    }

    public class VodStreamItem
    {
        public int num { get; set; }
        public string? name { get; set; }
        public int stream_id { get; set; }
        public string? stream_icon { get; set; }
        public double rating { get; set; }
        public string? rating_5based { get; set; }
        public string? added { get; set; }
        public string? category_id { get; set; }
        public string? container_extension { get; set; }
        public string? custom_sid { get; set; }
        public string? direct_source { get; set; }
    }

    public class SeriesItem
    {
        public int num { get; set; }
        public string? name { get; set; }
        public int series_id { get; set; }
        public string? cover { get; set; }
        public string? plot { get; set; }
        public string? cast { get; set; }
        public string? director { get; set; }
        public string? genre { get; set; }
        public string? releaseDate { get; set; }
        public string? last_modified { get; set; }
        public double rating { get; set; }
        public string? rating_5based { get; set; }
        public List<string>? backdrop_path { get; set; }
        public string? youtube_trailer { get; set; }
        public string? episode_run_time { get; set; }
        public string? category_id { get; set; }
    }

    public class SeriesInfoResponse
    {
        public SeriesInfo? info { get; set; }
        public Dictionary<string, List<EpisodeItem>>? episodes { get; set; }
    }

    public class SeriesInfo
    {
        public string? name { get; set; }
        public string? cover { get; set; }
        public string? plot { get; set; }
        public string? cast { get; set; }
        public string? director { get; set; }
        public string? genre { get; set; }
        public string? releaseDate { get; set; }
        public string? last_modified { get; set; }
        public string? rating { get; set; }
        public List<string>? backdrop_path { get; set; }
        public string? youtube_trailer { get; set; }
        public string? episode_run_time { get; set; }
        public string? category_id { get; set; }
    }

    public class EpisodeItem
    {
        public string? id { get; set; }
        public string? episode_num { get; set; }
        public string? title { get; set; }
        public string? container_extension { get; set; }
        public EpisodeInfo? info { get; set; }
        public string? custom_sid { get; set; }
        public string? added { get; set; }
        public int season { get; set; }
        public string? direct_source { get; set; }
    }

    public class EpisodeInfo
    {
        public string? movie_image { get; set; }
        public string? plot { get; set; }
        public string? releasedate { get; set; }
        public string? rating { get; set; }
    }
}
