using Jellyxtreme.Api;
using Jellyxtreme.Configuration;

namespace Jellyxtreme.Services;

public sealed class EncryptedCredentialStore : ICredentialStore
{
    private readonly ConfigCredentialStore _fallbackStore;

    // Compatibility layer until encrypted or Jellyfin-managed secret storage is available.
    public EncryptedCredentialStore(ConfigCredentialStore fallbackStore)
    {
        _fallbackStore = fallbackStore;
    }

    public IReadOnlyList<string> GetConfiguredProviderIds(PluginConfiguration config)
        => _fallbackStore.GetConfiguredProviderIds(config);

    public bool TryGetConnectionSettings(
        PluginConfiguration config,
        string? providerId,
        out XtreamConnectionSettings settings)
        => _fallbackStore.TryGetConnectionSettings(config, providerId, out settings);

    public XtreamConnectionSettings GetConnectionSettings(PluginConfiguration config, string? providerId)
        => _fallbackStore.GetConnectionSettings(config, providerId);
}
