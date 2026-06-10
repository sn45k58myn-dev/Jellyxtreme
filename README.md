# Jellyxtreme

Jellyxtreme is a Jellyfin plugin that connects to an Xtream Codes API provider to grab Live TV, Movies, and Series, and imports them seamlessly into your Jellyfin server.

It gives you full control over what is imported:
- **Live TV:** Generates a local `.m3u` file that you can add as a tuner device in Jellyfin's Live TV settings.
- **Movies:** Creates `.strm` files pointing to the VOD streams in a designated "Movies" folder, which Jellyfin can scan natively.
- **Series:** Creates structured folders and `.strm` files for your series and episodes in a designated "Series" folder.

## Compatibility

- Jellyfin version 10.9.x
- Built against .NET 8.0

## Setup & Configuration

1. **Build the plugin:**
   Ensure you have the .NET 8 SDK installed. Run `dotnet build` inside the `Jellyxtreme` folder to compile the plugin `Jellyxtreme.dll`.

2. **Install to Jellyfin:**
   - Copy the compiled `Jellyxtreme.dll` to your Jellyfin's `plugins` folder inside a new directory (e.g., `plugins/Jellyxtreme/Jellyxtreme.dll`).
   - Restart the Jellyfin server.

3. **Configure the Plugin:**
   - Open your Jellyfin Dashboard.
   - Go to `Plugins` and click on `Jellyxtreme`.
   - Enter your **Xtream Codes Server URL**, **Username**, and **Password**.
   - Enable the categories you want to import (Movies, Series, Live TV).
   - For Movies and Series, provide the output paths where `.strm` files will be saved. You should add these output paths as Libraries in your Jellyfin server (e.g. Media Type: Movies or TV Shows).
   - For Live TV, provide the path to output the `livetv.m3u` file (e.g., `/config/livetv.m3u`), and add this M3U Tuner in Jellyfin's Live TV settings.

4. **Sync Task:**
   - Go to `Dashboard` -> `Scheduled Tasks`.
   - Under the `Jellyxtreme` category, run the `Xtream Codes Sync` task. It will automatically populate the paths you defined.
