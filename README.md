# Jellyxtreme

Jellyxtreme is a Jellyfin plugin for connecting to authorised Xtream-compatible providers. The plugin is being refactored toward a native Jellyfin provider/cache architecture for Jellyfin 10.11.x and .NET 8.

Jellyxtreme now caches only explicitly selected import sections and categories:

- **Live TV:** selected Xtream live categories are cached as channel metadata with stream IDs, logos, EPG IDs, and group names.
- **VOD:** selected VOD categories are cached as movie metadata with posters, ratings, container extensions, and added dates.
- **Series:** selected series categories are cached with series metadata, seasons, episodes, episode stream IDs, and container extensions.

Default mode does not generate `.strm` files or `.m3u` playlists.

## Compatibility

- Jellyfin 10.11.x
- .NET 8 target framework
- Jellyfin package references: 10.10.7, the latest stable Jellyfin package line that restores against `net8.0`. Current Jellyfin 10.11.x NuGet packages target `net9.0`, so they cannot be referenced while keeping `TargetFramework` at `net8.0`.

## Legal Use

Only connect Jellyxtreme to lawful and authorised Xtream providers. You are responsible for ensuring that your provider, credentials, and streams are licensed for your use.

## Build

Install the .NET 8 SDK, then run:

```powershell
dotnet restore .\Jellyxtreme\Jellyxtreme.csproj
dotnet build .\Jellyxtreme\Jellyxtreme.csproj
```

The plugin assembly is produced under `Jellyxtreme/bin/<Configuration>/net8.0/`.

## Install

1. Build the project.
2. Create a Jellyfin plugin directory, for example `plugins/Jellyxtreme`.
3. Copy `Jellyxtreme.dll` and `plugin.json` into that directory.
4. Restart Jellyfin.
5. Open Dashboard -> Plugins -> Jellyxtreme.

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
- `Cache/` contains the provider cache document and persisted cache store.
- `Services/XtreamCacheRefreshService.cs` refreshes selected categories only.
- `Providers/` contains Live TV, VOD, and Series provider foundations plus stream URL resolution.
- `Configuration/configPage.html` is the embedded admin page.
- `Tasks/XtreamSyncTask.cs` exposes the manual scheduled cache refresh task.

Sensitive values are kept out of logs. Server URLs are validated as absolute `http` or `https` URLs, and authenticated stream URLs are resolved only when playback/provider code requests them.

## Roadmap

- Wire the Live TV foundation into Jellyfin's current 10.11 provider/tuner APIs where plugin support allows.
- Expose cached VOD and Series entries through native Jellyfin provider/library integration.
- Add optional M3U export as a separate opt-in feature, not as the default flow.
- Add encrypted or Jellyfin-managed secret storage for provider passwords when a stable plugin API is available.
- Add automated tests around category filtering, cache refresh, and credential redaction.
