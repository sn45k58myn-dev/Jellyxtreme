using Jellyxtreme.Cache;
using Jellyxtreme.Controllers;
using Jellyxtreme.Providers;
using Jellyxtreme.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyxtreme;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<XtreamCacheService>();
        serviceCollection.AddSingleton<XtreamCacheRefreshService>();
        serviceCollection.AddSingleton<StreamResolverService>();
        serviceCollection.AddSingleton<XtreamLiveTvProvider>();
        serviceCollection.AddSingleton<XtreamVodProvider>();
        serviceCollection.AddSingleton<XtreamSeriesProvider>();
        serviceCollection.AddTransient<JellyxtremeApiController>();
    }
}
