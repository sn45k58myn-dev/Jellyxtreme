using Jellyxtreme.Api;
using Jellyxtreme.Configuration;

namespace Jellyxtreme.Services;

public interface ICredentialStore
{
    IReadOnlyList<string> GetConfiguredProviderIds(PluginConfiguration config);

    bool TryGetConnectionSettings(
        PluginConfiguration config,
        string? providerId,
        out XtreamConnectionSettings settings);

    XtreamConnectionSettings GetConnectionSettings(PluginConfiguration config, string? providerId);
}

