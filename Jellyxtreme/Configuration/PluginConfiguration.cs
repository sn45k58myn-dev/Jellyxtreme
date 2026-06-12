using MediaBrowser.Model.Plugins;

namespace Jellyxtreme.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string ServerUrl { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        public bool EnableLiveTv { get; set; }
        public bool EnableVod { get; set; }
        public bool EnableSeries { get; set; }

        public string[] SelectedLiveCategoryIds { get; set; } = [];
        public string[] SelectedVodCategoryIds { get; set; } = [];
        public string[] SelectedSeriesCategoryIds { get; set; } = [];

        public int SyncIntervalHours { get; set; } = 12;
        public int CacheMinutes { get; set; } = 60;
        public DateTimeOffset? LastSuccessfulSyncUtc { get; set; }

        public PluginConfiguration()
        {
        }
    }
}
