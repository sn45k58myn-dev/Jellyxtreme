using MediaBrowser.Model.Plugins;

namespace Jellyxtreme.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public const string LegacyProviderId = "legacy";

        public string ServerUrl { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        public List<XtreamProviderConfig> Providers { get; set; } = [];

        public string Provider1ServerUrl
        {
            get => Providers.Count > 0 ? Providers[0].ServerUrl : string.Empty;
            set
            {
                EnsureProviders();
                Providers[0].ServerUrl = value;
            }
        }

        public string Provider1Username
        {
            get => Providers.Count > 0 ? Providers[0].Username : string.Empty;
            set
            {
                EnsureProviders();
                Providers[0].Username = value;
            }
        }

        public string Provider1Password
        {
            get => Providers.Count > 0 ? Providers[0].Password : string.Empty;
            set
            {
                EnsureProviders();
                Providers[0].Password = value;
            }
        }

        public string Provider2ServerUrl
        {
            get => Providers.Count > 1 ? Providers[1].ServerUrl : string.Empty;
            set
            {
                EnsureProviders();
                Providers[1].ServerUrl = value;
            }
        }

        public string Provider2Username
        {
            get => Providers.Count > 1 ? Providers[1].Username : string.Empty;
            set
            {
                EnsureProviders();
                Providers[1].Username = value;
            }
        }

        public string Provider2Password
        {
            get => Providers.Count > 1 ? Providers[1].Password : string.Empty;
            set
            {
                EnsureProviders();
                Providers[1].Password = value;
            }
        }

        public string Provider3ServerUrl
        {
            get => Providers.Count > 2 ? Providers[2].ServerUrl : string.Empty;
            set
            {
                EnsureProviders();
                Providers[2].ServerUrl = value;
            }
        }

        public string Provider3Username
        {
            get => Providers.Count > 2 ? Providers[2].Username : string.Empty;
            set
            {
                EnsureProviders();
                Providers[2].Username = value;
            }
        }

        public string Provider3Password
        {
            get => Providers.Count > 2 ? Providers[2].Password : string.Empty;
            set
            {
                EnsureProviders();
                Providers[2].Password = value;
            }
        }

        public bool EnableLiveTv { get; set; }
        public bool EnableVod { get; set; }
        public bool EnableSeries { get; set; }
        public bool EnableMetadataEnrichment { get; set; }
        public string TmdbApiKey { get; set; } = string.Empty;
        public string TvdbApiKey { get; set; } = string.Empty;

        public string[] SelectedLiveCategoryIds { get; set; } = [];
        public string[] SelectedVodCategoryIds { get; set; } = [];
        public string[] SelectedSeriesCategoryIds { get; set; } = [];

        public int SyncIntervalHours { get; set; } = 12;
        public int CacheMinutes { get; set; } = 60;
        public DateTimeOffset? LastSuccessfulSyncUtc { get; set; }
        public long? LastSyncDurationMs { get; set; }
        public string LastSyncError { get; set; } = string.Empty;

        public PluginConfiguration()
        {
            EnsureProviders();
            MigrateLegacyCredentials();
        }

        public IReadOnlyList<(string ProviderId, XtreamProviderConfig Provider)> GetConfiguredProviders()
        {
            EnsureProviders();
            MigrateLegacyCredentials();

            var providers = new List<(string, XtreamProviderConfig)>();
            for (var i = 0; i < Providers.Count; i++)
            {
                var provider = Providers[i];
                if (!IsConfigured(provider))
                {
                    continue;
                }

                providers.Add(("provider" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), provider));
            }

            return providers;
        }

        public XtreamProviderConfig? GetProviderById(string? providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                return GetConfiguredProviders().FirstOrDefault().Provider;
            }

            if (string.Equals(providerId, LegacyProviderId, StringComparison.OrdinalIgnoreCase))
            {
                return GetConfiguredProviders().FirstOrDefault().Provider;
            }

            EnsureProviders();
            if (providerId.StartsWith("provider", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(providerId.AsSpan("provider".Length), out var index)
                && index >= 1 && index <= Providers.Count)
            {
                var provider = Providers[index - 1];
                if (IsConfigured(provider))
                {
                    return provider;
                }
            }

            return null;
        }

        public static string NormalizeProviderId(string? providerId)
            => string.IsNullOrWhiteSpace(providerId) ? LegacyProviderId : providerId;

        private static bool IsConfigured(XtreamProviderConfig provider)
            => !string.IsNullOrWhiteSpace(provider.ServerUrl)
                && !string.IsNullOrWhiteSpace(provider.Username)
                && !string.IsNullOrWhiteSpace(provider.Password);

        private void MigrateLegacyCredentials()
        {
            if (!string.IsNullOrWhiteSpace(ServerUrl)
                || !string.IsNullOrWhiteSpace(Username)
                || !string.IsNullOrWhiteSpace(Password))
            {
                EnsureProviders();
                if (!IsConfigured(Providers[0]))
                {
                    Providers[0].ServerUrl = ServerUrl;
                    Providers[0].Username = Username;
                    Providers[0].Password = Password;
                }

                ServerUrl = string.Empty;
                Username = string.Empty;
                Password = string.Empty;
            }
        }

        private void EnsureProviders()
        {
            while (Providers.Count < 3)
            {
                Providers.Add(new XtreamProviderConfig());
            }

            if (Providers.Count > 3)
            {
                Providers.RemoveRange(3, Providers.Count - 3);
            }
        }
    }
}

public sealed class XtreamProviderConfig
{
    public string ServerUrl { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
