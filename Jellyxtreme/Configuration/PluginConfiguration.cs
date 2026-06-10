using MediaBrowser.Model.Plugins;

namespace Jellyxtreme.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public string ServerUrl { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        public bool ImportMovies { get; set; } = false;
        public string MoviesPath { get; set; } = string.Empty;

        public bool ImportSeries { get; set; } = false;
        public string SeriesPath { get; set; } = string.Empty;

        public bool ImportLiveTv { get; set; } = false;
        public string LiveTvM3uPath { get; set; } = string.Empty;

        public PluginConfiguration()
        {
        }
    }
}
