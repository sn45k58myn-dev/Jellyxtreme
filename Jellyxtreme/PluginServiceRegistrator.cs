using Jellyxtreme.Cache;
using Jellyxtreme.Controllers;
using Jellyxtreme.Api;
using Jellyxtreme.Providers;
using Jellyxtreme.Services;
using Jellyxtreme.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyxtreme;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<ConfigCredentialStore>();
        serviceCollection.AddSingleton<EncryptedCredentialStore>();
        serviceCollection.AddSingleton<ICredentialStore>(provider =>
        {
            if (Plugin.Instance?.Configuration is not null && Plugin.Instance.Configuration.UseEncryptedCredentials)
            {
                return provider.GetRequiredService<EncryptedCredentialStore>();
            }

            return provider.GetRequiredService<ConfigCredentialStore>();
        });
        serviceCollection.AddSingleton<XtreamApiClient>();
        serviceCollection.AddSingleton<XtreamCacheService>();
        serviceCollection.AddSingleton<XmlTvCacheService>();
        serviceCollection.AddSingleton<XtreamCacheRefreshService>();
        serviceCollection.AddSingleton<MetadataEnrichmentService>();
        serviceCollection.AddSingleton<StreamResolverService>();
        serviceCollection.AddSingleton<XtreamLiveTvProvider>();
        serviceCollection.AddSingleton<VodProvider>();
        serviceCollection.AddSingleton<SeriesProvider>();
        serviceCollection.AddSingleton<XtreamVodProvider>();
        serviceCollection.AddSingleton<XtreamSeriesProvider>();
        serviceCollection.AddSingleton<XtreamMovieLibraryProvider>();
        serviceCollection.AddSingleton<XtreamSeriesLibraryProvider>();
        serviceCollection.AddSingleton<JellyfinLiveTvProvider>();
        serviceCollection.AddSingleton<IHostedService, LiveTvTunerHostNormalizer>();
        serviceCollection.AddSingleton<IChannel>(provider => provider.GetRequiredService<XtreamMovieLibraryProvider>());
        serviceCollection.AddSingleton<IChannel>(provider => provider.GetRequiredService<XtreamSeriesLibraryProvider>());
        serviceCollection.AddSingleton<ITunerHost>(provider => provider.GetRequiredService<JellyfinLiveTvProvider>());
        serviceCollection.AddSingleton<IConfigurableTunerHost>(provider => provider.GetRequiredService<JellyfinLiveTvProvider>());
        serviceCollection.AddSingleton<IListingsProvider>(provider => provider.GetRequiredService<JellyfinLiveTvProvider>());
        serviceCollection.AddTransient<JellyxtremeApiController>();
    }
}
