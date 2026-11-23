# Mertviy

Standalone Windows x64 app. The executable embeds runtime files from Flowseal `zapret-discord-youtube` 1.9.9d, extracts them under `%LOCALAPPDATA%\Mertviy`, tests service profiles from `Config/service-profiles.json`, and starts its own `winws.exe` process. The user does not need to install or manually start a separate zapret installation. Mertviy's application and profiles are maintained separately; its packet-processing engine is derived from upstream projects, not an original Mertviy engine.

## Build

Requires the .NET 8 SDK and Windows Desktop targeting pack. The embedded files are in `EngineAssets`.

```powershell
dotnet publish .\Mertviy.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish
```

To build the MSI after publishing the executable, install WiX Toolset 5.0.2 into a local tools folder and run:

```powershell
dotnet tool install --tool-path .\tools\wix wix --version 5.0.2
.\tools\wix\wix.exe build .\packaging\Mertviy.wxs -arch x64 -d Version=0.1.0 -d PublishDir=publish -o .\publish\Mertviy-0.1.0-win-x64.msi
```

The MSI installs Mertviy per-machine under `Program Files` and creates a Start Menu shortcut for the installing user. It supports major-version upgrades and uninstall through Windows Settings.

## Notes

- Mertviy requests Administrator privileges because WinDivert needs them.
- On launch, the app tests HTTPS probes for YouTube, Discord and Telegram using several profiles, saves the fastest successful profile per service, and starts its own engine process.
- HTTPS probes do not reproduce every browser or native app connection. Domain profiles do not bypass IP blocking; Telegram may use direct IP connections.
- QUIC is off by default. The checkbox can enable it; Discord voice UDP is included in the Discord profile.
- If a separate `winws.exe` is already running, stop it before starting Mertviy to avoid driver conflicts.
- The app and included binaries are not digitally signed. Windows SmartScreen or antivirus may warn about packet interception components.

## Updates

At startup Mertviy checks the latest stable release in `angelsky33rus-ai/mertviy`. The check is non-blocking for the UI and network/API errors do not prevent normal use. A newer version is downloaded only after the user accepts the prompt. Mertviy verifies the exact release asset against its `.sha256` sidecar and keeps it as a separate versioned executable under `%LOCALAPPDATA%\Mertviy\updates`. The user must explicitly press **Запустить обновление**; the current executable is never replaced silently. A manual check is available in the header.

## Releases

Push a tag in the form `vX.Y.Z` to run `.github/workflows/release.yml`. The workflow validates the bundled runtime files against the Flowseal 1.9.9d checksum manifest, publishes a self-contained `win-x64` executable, builds a per-machine MSI installer, and attaches both `Mertviy-X.Y.Z-win-x64.exe` and `Mertviy-X.Y.Z-win-x64.msi` with SHA-256 sidecars to the GitHub release. The repo must be public for installed builds to read release metadata without a credential.

For a new app release, bump `VersionPrefix` in `Mertviy.csproj` to the same version before creating the tag. The engine version is tracked separately in `EngineAssets/ENGINE_VERSION.txt`.

## Third-party notices

The runtime payload is Flowseal `1.9.9d`; its tagged source and checksum manifest are preserved in `third_party/sources`. WinDivert 2.2.2 and Cygwin 3.4.10 source snapshots and license texts are included there as well. The exact bol-van commit used to build the included `winws.exe` is not identified, and Mertviy does not claim to have rebuilt that binary. See [`third_party/UPSTREAM.md`](third_party/UPSTREAM.md) and [`EngineAssets/THIRD-PARTY-NOTICES.txt`](EngineAssets/THIRD-PARTY-NOTICES.txt) for details.

The included WinDivert binaries are unchanged. Its dual-license text and v2.2.2 source are included in `third_party/sources/WinDivert-2.2.2`. Cygwin 3.4.10 source, LGPL notices, and linking exception are included in `third_party/sources/Cygwin-3.4.10`.
