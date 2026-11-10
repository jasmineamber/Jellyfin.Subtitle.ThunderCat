# ThunderCat Subtitles for Jellyfin

English | [中文](./README_CN.md)

ThunderCat is a subtitle plugin for **Jellyfin 10.10.7** that connects Jellyfin to **SubtitleCat** and **Thunder (Xunlei)**.

> ThunderCat does not host media files or subtitles. Search results and subtitle files are supplied by third-party services.

## Features

- Search multilingual subtitles through SubtitleCat and rank results by title similarity.
- Search Thunder and improve matching with local-file CID data.
- Label high-confidence results with compact `[CID]`, `[ID]`, and `[TITLE]` markers.
- Remove exact duplicate results within each provider before they reach Jellyfin.
- Prioritize Thunder results that explicitly match the requested language.
- Fall back from release or catalog identifiers to the original title.
- Use release or catalog identifiers from media filenames even when Jellyfin supplies a display title.
- Normalize common Chinese, English, Japanese, Korean, and other language metadata.
- Support `srt`, `ass`, `ssa`, and `vtt` subtitle formats.
- Enable SubtitleCat and Thunder independently.
- Test provider connectivity and copy privacy-safe diagnostics from the configuration page.

## Security design

- Short-lived opaque download tokens avoid exposing upstream URLs in subtitle IDs.
- Requests are restricted to HTTPS, explicit host allowlists, port 443, and public IP addresses.
- Redirect targets are revalidated on every hop to reduce SSRF risk.
- Network requests use per-attempt timeouts, bounded retries, search budgets, and response-size limits.
- Downloaded content is checked to reject HTML/JSON error pages and disguised archives.
- Diagnostics exclude media paths, search queries, and download URLs.

## Compatibility

| Item | Value |
|---|---|
| Target framework | `net8.0` |
| Jellyfin SDK | `10.10.7` |
| Plugin ABI | `10.10.7.0` |
| Target server | Jellyfin Server `10.10.7` |

This repository is Jellyfin-only. The Emby variant is no longer maintained on this branch.

## Installation

### Install from the plugin repository

1. Open **Dashboard → Plugins → Repositories**.
2. Add this repository URL:

   ```text
   https://raw.githubusercontent.com/jasmineamber/Jellyfin.Subtitle.ThunderCat/dist/manifest.json
   ```

3. Open **Catalog**, select **ThunderCat Subtitles**, and install the latest version.
4. Restart Jellyfin Server when prompted.
5. Open **Dashboard → Plugins → ThunderCat Subtitles**, enable the providers, and save.
6. Enable SubtitleCat and/or Thunder in the library's subtitle downloader settings.

Use the raw manifest URL above. GitHub `blob` pages do not return the JSON document expected by Jellyfin.

### Install from a ZIP

1. Download the ZIP from [Releases](https://github.com/jasmineamber/Jellyfin.Subtitle.ThunderCat/releases).
2. Put `Jellyfin.Plugin.ThunderCat.dll` in Jellyfin's `plugins/thundercat` directory.
3. Restart Jellyfin Server.
4. Complete steps 5 and 6 above.

Common plugin directories:

- Windows: `%LOCALAPPDATA%\jellyfin\plugins`
- Linux: `/var/lib/jellyfin/plugins`
- Docker: `/config/plugins`

## Configuration page

The configuration page provides:

- Global switches for SubtitleCat and Thunder.
- Saved and unsaved state feedback.
- Server-side connectivity tests for both providers.
- Plugin version, Jellyfin Server version, and compatibility baseline.
- A diagnostic summary that excludes sensitive media information.

Connectivity tests run on Jellyfin Server and reuse the plugin's HTTPS, host-allowlist, and public-address validation. A successful test confirms that the upstream is currently reachable; it does not guarantee subtitles for a particular title.

## Build and test

```powershell
dotnet restore Jellyfin.Plugin.ThunderCat.sln
dotnet test Jellyfin.Plugin.ThunderCat.sln -c Release --warnaserror
dotnet build Jellyfin.Plugin.ThunderCat.csproj -c Release -p:JellyfinVersion=10.10.7
```

Output:

```text
bin/Release/net8.0/Jellyfin.Plugin.ThunderCat.dll
```

Tagged releases are built by GitHub Actions. The workflow publishes the plugin ZIP,
creates a GitHub Release, and updates the Jellyfin repository manifest on the `dist`
branch.

## Known boundaries

- SubtitleCat and Thunder are third-party upstreams whose contracts and availability may change.
- Upstream domain migrations require a security review before allowlists are updated.
- The project cannot guarantee subtitle availability and does not bypass upstream access restrictions.

## License

MIT License © 2026 drunkleee
