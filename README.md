# Jellyxtreme

Jellyxtreme is a Jellyfin plugin for connecting to authorised Xtream-compatible providers. The plugin uses a selectable provider/cache architecture for Jellyfin 10.11.x and .NET 9.

Jellyxtreme now caches only explicitly selected import sections and categories:

- **Live TV:** selected Xtream live categories are cached as channel metadata with stream IDs, logos, EPG IDs, and group names.
- **VOD:** selected VOD categories are cached as movie metadata with posters, ratings, container extensions, and added dates.
- **Series:** selected series categories are cached with series metadata, seasons, episodes, episode stream IDs, and container extensions.
- **XMLTV:** guide data is cached under the Jellyfin plugin data folder when Live TV is enabled and categories are selected.

Default mode does not generate `.strm` files or `.m3u` playlists.

## Compatibility

- Jellyfin 10.11.x
- .NET 9 target framework
- Jellyfin package references: `Jellyfin.Controller` `10.11.11` and `Jellyfin.Model` `10.11.11`

Jellyfin `10.11.11` NuGet packages target `net9.0`, so Jellyxtreme now targets `net9.0` to stay aligned with the current Jellyfin 10.11 package line.

## Legal Use

Only connect Jellyxtreme to lawful and authorised Xtream providers. You are responsible for ensuring that your provider, credentials, and streams are licensed for your use.

## Build

Install the .NET 9 SDK, then run:

```powershell
dotnet restore .\Jellyxtreme\Jellyxtreme.csproj
dotnet build .\Jellyxtreme\Jellyxtreme.csproj
dotnet test .\Jellyxtreme.Tests\Jellyxtreme.Tests.csproj
```

The plugin assembly is produced under `Jellyxtreme/bin/<Configuration>/net9.0/`. For a debug build, copy from `Jellyxtreme/bin/Debug/net9.0/`; for a release build, copy from `Jellyxtreme/bin/Release/net9.0/`.

## Install

1. Build the project.
2. Create a Jellyfin plugin directory, for example `plugins/Jellyxtreme`.
3. Copy `Jellyxtreme.dll` and `plugin.json` into that directory.
4. Restart Jellyfin.
5. Open Dashboard -> Plugins -> Jellyxtreme.

`plugin.json` is copied to the build output by the project file. `Configuration/configPage.html` is embedded as `Jellyxtreme.Configuration.configPage.html` and is loaded by the plugin configuration page.

## Configuration

1. Enter the Xtream server URL, username, and password.
2. Use **Test Connection** to validate the endpoint.
3. Use **Refresh Categories** to load Live TV, VOD, and Series categories.
4. Enable only the sections you want to cache.
5. Select category checkboxes for each enabled section.
6. Save the configuration.
7. Run Dashboard -> Scheduled Tasks -> **JellyXtreme: Refresh Xtream Cache**.

If no categories are selected, Jellyxtreme imports nothing. If only one section is enabled and selected, the other sections are skipped.

## Architecture

- `Api/XtreamApiClient.cs` contains authenticated Xtream API calls for categories, streams, series info, and XMLTV.
- `Cache/` contains `XtreamCacheService`, the provider cache document, category caches, cached live channels, VOD items, series items, and episode items. Cache files are versioned and written atomically by writing a temporary file before replacing the previous cache.
- `Services/XtreamCacheRefreshService.cs` refreshes selected categories only.
- Series refresh calls `get_series` first, then calls `get_series_info` for every valid series returned before caching only selected category results. Series info requests are throttled to five concurrent requests.
- `Services/StreamResolverService.cs` resolves authenticated Xtream URLs only at playback time.
- `Providers/` contains Live TV, VOD, and Series provider foundations over the cache.
- `Controllers/JellyxtremeApiController.cs` exposes authenticated admin endpoints used by the config page: test connection, categories, cache summary, and beta VOD/Series test listing endpoints.
- `PluginServiceRegistrator.cs` registers the cache, refresh, resolver, and provider services with Jellyfin dependency injection.
- `Configuration/configPage.html` is the embedded admin page.
- `Tasks/XtreamSyncTask.cs` exposes the manual scheduled cache refresh task.
- `Jellyxtreme.Tests/` covers URL building, category filtering, section cache policy, credential redaction, resolver paths, and config defaults.

Sensitive values are kept out of logs. Server URLs are validated as absolute `http` or `https` URLs, and authenticated stream URLs are resolved only when playback/provider code requests them. Passwords are currently stored in plugin configuration; future work should move them to encrypted or Jellyfin-managed secret storage when a stable plugin API is available.

The Live TV foundation is registered as a Jellyfin `ITunerHost`/`IConfigurableTunerHost` and exposes cached channels with playback-time stream resolution. VOD and Series currently expose cached metadata and playback media sources through provider services plus beta admin/test endpoints; full virtual library integration remains a follow-up.

## Beta Admin/Test Endpoints

These endpoints require Jellyfin authentication:

- `POST /Jellyxtreme/TestConnection`
- `POST /Jellyxtreme/Categories`
- `GET /Jellyxtreme/Categories`
- `GET /Jellyxtreme/CacheSummary`
- `GET /Jellyxtreme/Vod?startIndex=0&limit=100`
- `GET /Jellyxtreme/Vod/{streamId}/MediaSources?includePlaybackUrl=true`
- `GET /Jellyxtreme/Series?startIndex=0&limit=100`
- `GET /Jellyxtreme/Series/{seriesId}/Episodes?startIndex=0&limit=100`
- `GET /Jellyxtreme/Series/Episodes/{episodeStreamId}/MediaSources?includePlaybackUrl=true`

The media-source endpoints return authenticated playback URLs only when `includePlaybackUrl=true` is explicitly supplied.

## Roadmap

- Expose cached VOD and Series entries through native Jellyfin provider/library integration.
- Add optional M3U export as a separate opt-in feature, not as the default flow.
- Add encrypted or Jellyfin-managed secret storage for provider passwords when a stable plugin API is available.
- Expand integration tests against a live Jellyfin 10.11 server.
