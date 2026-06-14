using Jellyxtreme.Api;
using Jellyxtreme.Configuration;

namespace Jellyxtreme.Services;

public sealed class ConfigCredentialStore : ICredentialStore
{
    public IReadOnlyList<string> GetConfiguredProviderIds(PluginConfiguration config)
        => config.GetConfiguredProviders()
            .Select(provider => provider.ProviderId)
            .ToList();

    public bool TryGetConnectionSettings(
        PluginConfiguration config,
        string? providerId,
        out XtreamConnectionSettings settings)
    {
        settings = new XtreamConnectionSettings(string.Empty, string.Empty, string.Empty, TimeSpan.FromMinutes(1));

        var provider = config.GetProviderById(providerId);
        if (provider is null)
        {
            return false;
        }

        settings = new XtreamConnectionSettings(
            provider.ServerUrl,
            provider.Username,
            provider.Password,
            TimeSpan.FromMinutes(Math.Max(1, config.CacheMinutes)));
        return true;
    }

    public XtreamConnectionSettings GetConnectionSettings(PluginConfiguration config, string? providerId)
    {
        if (TryGetConnectionSettings(config, providerId, out var settings))
        {
            return settings;
        }

        throw new XtreamValidationException("Configured provider credentials are required.", nameof(ICredentialStore));
    }
}

