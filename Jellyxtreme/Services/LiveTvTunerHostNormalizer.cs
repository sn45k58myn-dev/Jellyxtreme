using System.Xml.Linq;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyxtreme.Services;

public sealed class LiveTvTunerHostNormalizer : IHostedService
{
    private const string ProviderType = "jellyxtreme";
    private readonly IApplicationPaths _applicationPaths;
    private readonly ILogger<LiveTvTunerHostNormalizer> _logger;

    public LiveTvTunerHostNormalizer(IApplicationPaths applicationPaths, ILogger<LiveTvTunerHostNormalizer> logger)
    {
        _applicationPaths = applicationPaths;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var path = Path.Combine(_applicationPaths.ConfigurationDirectoryPath, "livetv.xml");
            if (!File.Exists(path))
            {
                return Task.CompletedTask;
            }

            var document = XDocument.Load(path);
            var changed = false;
            var jellyxtremeTunerIds = new List<string>();
            foreach (var host in document.Descendants("TunerHostInfo"))
            {
                var type = (string?)host.Element("Type");
                if (!string.Equals(type, ProviderType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var tunerId = (string?)host.Element("Id");
                if (!string.IsNullOrWhiteSpace(tunerId))
                {
                    jellyxtremeTunerIds.Add(tunerId);
                }

                var tunerCountElement = host.Element("TunerCount");
                if (tunerCountElement is null)
                {
                    host.Add(new XElement("TunerCount", "1"));
                    changed = true;
                    continue;
                }

                if (!int.TryParse(tunerCountElement.Value, out var tunerCount) || tunerCount <= 0)
                {
                    tunerCountElement.Value = "1";
                    changed = true;
                }
            }

            if (jellyxtremeTunerIds.Count > 0)
            {
                changed |= EnsureListingsProvider(document, jellyxtremeTunerIds);
            }

            if (changed)
            {
                document.Save(path);
                _logger.LogInformation("Normalized JellyXtreme Live TV tuner and listings provider configuration.");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Unable to normalize JellyXtreme Live TV tuner host configuration.");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool EnsureListingsProvider(XDocument document, IReadOnlyCollection<string> tunerIds)
    {
        var root = document.Root;
        if (root is null)
        {
            return false;
        }

        var changed = false;
        var providers = root.Element("ListingProviders");
        if (providers is null)
        {
            providers = new XElement("ListingProviders");
            root.Add(providers);
            changed = true;
        }

        var provider = providers
            .Elements("ListingsProviderInfo")
            .FirstOrDefault(item => string.Equals((string?)item.Element("Type"), ProviderType, StringComparison.OrdinalIgnoreCase));

        if (provider is null)
        {
            provider = new XElement(
                "ListingsProviderInfo",
                new XElement("Id", ProviderType),
                new XElement("Type", ProviderType),
                new XElement("ListingsId", ProviderType),
                new XElement("EnableAllTuners", "true"));
            providers.Add(provider);
            changed = true;
        }

        changed |= SetElement(provider, "Id", ProviderType);
        changed |= SetElement(provider, "Type", ProviderType);
        changed |= SetElement(provider, "ListingsId", ProviderType);
        changed |= SetElement(provider, "EnableAllTuners", "true");

        var enabledTuners = provider.Element("EnabledTuners");
        if (enabledTuners is null)
        {
            enabledTuners = new XElement("EnabledTuners");
            provider.Add(enabledTuners);
            changed = true;
        }

        var existingTuners = enabledTuners.Elements("string").Select(element => element.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var tunerId in tunerIds)
        {
            if (existingTuners.Contains(tunerId))
            {
                continue;
            }

            enabledTuners.Add(new XElement("string", tunerId));
            changed = true;
        }

        return changed;
    }

    private static bool SetElement(XElement parent, string name, string value)
    {
        var element = parent.Element(name);
        if (element is null)
        {
            parent.Add(new XElement(name, value));
            return true;
        }

        if (string.Equals(element.Value, value, StringComparison.Ordinal))
        {
            return false;
        }

        element.Value = value;
        return true;
    }
}
