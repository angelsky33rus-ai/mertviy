# Mertviy Release and Updater Implementation Plan

> **For agentic workers:** Use the executing-plans skill to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the current standalone WinForms app into an independently versioned Mertviy release with service profiles in config, a bundled licensed engine, GitHub update downloads, and a clearer control panel.

**Architecture:** Keep .NET 8 WinForms and the app-managed engine process. Move service/probe/strategy definitions to a validated bundled JSON catalog, read Mertviy versioned releases from GitHub, download only the exact Windows release asset after checking its published SHA-256, and create portable release artifacts with GitHub Actions.

**Tech Stack:** .NET 8 WinForms, System.Text.Json, GitHub Releases REST API, GitHub Actions on Windows, PowerShell.

**Spec:** `outputs/docs/superpowers/specs/2026-11-24-mertviy-update-system-design.md`

## Global Constraints

- Target framework remains `net8.0-windows` and release RID remains `win-x64`.
- The executable remains self-contained and includes the runtime engine files needed by the app.
- The configured update source is `angelsky33rus-ai/mertviy`; Flowseal Releases are not the Mertviy app update source.
- The source/release repository is intended to be public; do not change its visibility until immediately before that action is confirmed.
- Update checks and failures do not gate app startup; downloads require a user choice and verified SHA-256.
- The current executable is never silently replaced or launched by the updater.
- Engine code and binaries remain attributed to their upstream authors with applicable license notices retained.
- Do not commit generated publish output, `bin/`, `obj/`, or local executables to the Git repository.

---

## File Map

- Create `outputs/.gitignore` and `outputs/.gitattributes` to define source/release boundaries and stable line endings.
- Modify `outputs/Mertviy.csproj` to define the application version and embed config/engine metadata.
- Create `outputs/Config/service-profiles.json` for service domains, probes, and named strategies.
- Create `outputs/ProfileCatalog.cs` to load and validate the bundled profile catalog.
- Create `outputs/GitHubReleaseUpdater.cs` to check release metadata, download the exact asset, and verify its checksum.
- Create `outputs/MainForm.cs` by moving the form out of `outputs/Program.cs`, then add update controls and the improved layout.
- Keep `outputs/Program.cs` as the WinForms entry point.
- Create `outputs/EngineAssets/ENGINE_VERSION.txt` and update `outputs/EngineAssets/THIRD-PARTY-NOTICES.txt` with precise upstream/version/source references.
- Create `outputs/third_party/UPSTREAM.md` and vendor corresponding source snapshots without nested `.git` directories.
- Create `outputs/.github/workflows/release.yml` to build and publish the self-contained release executable and checksum file.
- Modify `outputs/README.md` to document local build, release process, update behavior, source provenance, and required notices.
- Keep `outputs/EngineAssets/` runtime payloads tracked; ignore only generated output and the root-level published executable.

### Task 1: Prepare the Mertviy repository

**Files:**
- Create: `outputs/.gitignore`
- Create: `outputs/.gitattributes`
- Modify: `outputs/Mertviy.csproj`
- Create: `outputs/EngineAssets/ENGINE_VERSION.txt`
- Modify: `outputs/EngineAssets/THIRD-PARTY-NOTICES.txt`
- Modify: `outputs/README.md`

**Interfaces:**
- The app version is exposed through `AssemblyInformationalVersion` and used in UI and release naming.
- The bundled engine version is read from the embedded `ENGINE_VERSION.txt` resource.

- [x] **Step 1: Add repository ignore rules**

  Add these rules to `.gitignore`; do not ignore runtime files under `EngineAssets/`:

  ```gitignore
  /bin/
  /obj/
  /publish/
  /.vs/
  /Mertviy.exe
  /ZapretPilot.exe
  *.user
  ```

- [x] **Step 2: Define source version metadata**

  Set `VersionPrefix` to `0.1.0` and `InformationalVersion` to `$(VersionPrefix)` in `Mertviy.csproj`; keep the engine version in `EngineAssets/ENGINE_VERSION.txt` at the version matching the currently packaged binaries (`1.9.9d`):

  ```xml
  <VersionPrefix>0.1.0</VersionPrefix>
  <InformationalVersion>$(VersionPrefix)</InformationalVersion>
  ```

- [ ] **Step 3: Record engine provenance**

  Create `third_party/UPSTREAM.md` listing the source URL, pinned commit/release, local asset filenames, and license for each third-party component. Include corresponding source snapshots under `third_party/` with `.git` directories excluded. If the exact source revision for a binary cannot be identified, do not describe that binary as rebuilt from that snapshot; preserve its upstream release provenance and do not publish until its license/source obligations are documented.

- [x] **Step 4: Document the current release boundary**

  Update the README to state that Mertviy is its own portable release, that its runtime engine is bundled, and that the upstream engine is derived rather than original Mertviy code.

- [x] **Step 5: Initialize the local Git checkout and add the remote**

  From the workspace root, run `git -C outputs init -b main` and `git -C outputs remote add origin https://github.com/angelsky33rus-ai/mertviy.git`. Confirm ignored executables are not part of the source inventory. Do not change remote visibility, push, or create a GitHub commit in this task.

- [x] **Step 6: Build the standalone executable**

  Run `dotnet publish .\Mertviy.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish` from `outputs`.

  Expected: publish succeeds and the output directory contains a self-contained `Mertviy.exe` while the repository ignore rules exclude that generated file.

### Task 2: Move service profiles into a validated catalog

**Files:**
- Create: `outputs/Config/service-profiles.json`
- Create: `outputs/ProfileCatalog.cs`
- Create: `outputs/ServiceProfile.cs`
- Modify: `outputs/Mertviy.csproj`
- Modify: `outputs/Program.cs`
- Modify: `outputs/MainForm.cs`

**Interfaces:**
- `ServiceProfileCatalog.Load(Assembly assembly, string resourceName)` returns `ProfileCatalog`.
- `ProfileCatalog.Services` is a read-only collection of `ServiceProfile`.
- `ProfileCatalog.Strategies` is a read-only collection of `StrategyProfile`.
- `ServiceProfile` contains `Name`, `Hosts`, and `ProbeUrls`.
- `StrategyProfile` contains `Name`, `Description`, `Desync`, `SplitPosition`, and `Repeats`.

- [ ] **Step 1: Add the JSON catalog**

  Move the existing YouTube, Discord, and Telegram hosts/probe URLs and the three existing strategies (`Fake TLS`, `Fake + multisplit`, `Fake + multidisorder`) into `Config/service-profiles.json`. Keep each strategy's current values unchanged in this task. Use this schema:

  ```json
  {
    "services": [
      { "name": "YouTube", "hosts": ["youtube.com", "googlevideo.com", "youtubei.googleapis.com", "ytimg.com"], "probeUrls": ["https://www.youtube.com/generate_204", "https://www.googlevideo.com/generate_204"] },
      { "name": "Discord", "hosts": ["discord.com", "discord.gg", "discordapp.com", "discord.media", "discordapp.net", "discordcdn.com"], "probeUrls": ["https://discord.com/api/v10/gateway"] },
      { "name": "Telegram", "hosts": ["telegram.org", "t.me", "telegram.me", "telegra.ph", "telegram.dog"], "probeUrls": ["https://telegram.org/"] }
    ],
    "strategies": [
      { "name": "Fake TLS", "description": "Fake-пакет и случайный TLS fingerprint", "desync": "fake", "splitPosition": "", "repeats": "6" },
      { "name": "Fake + multisplit", "description": "Разделение ClientHello в начале и около домена", "desync": "fake,multisplit", "splitPosition": "1,midsld", "repeats": "6" },
      { "name": "Fake + multidisorder", "description": "Перестановка сегментов ClientHello", "desync": "fake,multidisorder", "splitPosition": "1,midsld", "repeats": "6" }
    ]
  }
  ```

- [ ] **Step 2: Add catalog DTOs and resource loading**

  Implement the declared DTOs, add the JSON as an embedded resource in `Mertviy.csproj` using the logical name `Mertviy.Config.service-profiles.json`, and load it through `Assembly.GetManifestResourceStream`. Reject missing service names, empty hosts/probes, duplicate names, invalid HTTPS URLs, and unsupported `Desync` values before the form can use the catalog.

- [ ] **Step 3: Replace hard-coded catalog references**

  Pass the loaded catalog into `MainForm`; use it for the service grid, strategy combo box, probe iteration, saved-profile restoration, and command construction. Keep `settings.json` as the location for the user's selected strategy and QUIC setting.

- [ ] **Step 4: Build the standalone executable**

  Run the `dotnet publish` command from Task 1.

  Expected: publish succeeds with the JSON profile catalog embedded in the single-file executable.

### Task 3: Implement the Mertviy release updater

**Files:**
- Create: `outputs/GitHubReleaseUpdater.cs`
- Create: `outputs/ReleaseInfo.cs`
- Modify: `outputs/Mertviy.csproj`
- Modify: `outputs/README.md`

**Interfaces:**
- `GitHubReleaseUpdater.CheckLatestAsync(CancellationToken)` returns `UpdateCheckResult` with current version, latest version, release page URL, asset URL, checksum URL, and update availability.
- `GitHubReleaseUpdater.DownloadUpdateAsync(UpdateCheckResult, string destinationDirectory, IProgress<long> progress, CancellationToken)` returns the verified executable path.
- `UpdateCheckResult` exposes `IsUpdateAvailable`, `LatestVersion`, and a user-safe error/status message; HTTP/JSON/IO errors do not escape into startup.

- [ ] **Step 1: Add version and repository settings**

  Define the GitHub owner/repository as `angelsky33rus-ai/mertviy`, and read the current app version from the entry assembly's informational version. Do not accept an arbitrary API host from a downloaded config.

- [ ] **Step 2: Read the latest stable release**

  Use a GitHub API `HttpClient` with a Mertviy user-agent, GitHub JSON Accept header, a 5-second API timeout, and a 1 MiB response-size limit. Keep the download client separate so the API timeout does not interrupt large asset downloads. Call the repository's `/releases/latest` endpoint; parse `tag_name`; remove a leading `v` and informational `+metadata`; compare with the running version using `System.Version`. Mark missing or malformed tags as an unavailable update check.

- [ ] **Step 3: Select and validate release assets**

  Require the exact asset names `Mertviy-<version>-win-x64.exe` and `Mertviy-<version>-win-x64.exe.sha256`. Include the executable asset size in `UpdateCheckResult` for the confirmation dialog. Require the API's initial asset URL to use HTTPS on `github.com`; after redirect, accept only HTTPS on `release-assets.githubusercontent.com` or `objects.githubusercontent.com`. Refuse releases that omit either asset or use another initial host.

- [ ] **Step 4: Download and verify**

  Stream the executable to a temporary file under `%LOCALAPPDATA%\Mertviy\updates`, cap the download at 500 MiB, report byte progress through `IProgress<long>`, parse a 64-character SHA-256 value from the checksum file, and compare it with `SHA256.HashDataAsync`. Rename the temporary file to the final versioned filename only after a match. On cancellation, network failure, or checksum mismatch, delete only the incomplete temporary file and preserve the installed executable.

- [ ] **Step 5: Build the standalone executable**

  Run the `dotnet publish` command from Task 1.

  Expected: publish succeeds with updater code and no additional package dependency.

### Task 4: Improve the control panel and connect startup update flow

**Files:**
- Create: `outputs/MainForm.cs`
- Modify: `outputs/Program.cs`
- Modify: `outputs/GitHubReleaseUpdater.cs`
- Modify: `outputs/README.md`

**Interfaces:**
- `MainForm` receives `ProfileCatalog` and `GitHubReleaseUpdater` through its constructor.
- `MainForm.CheckForUpdatesAsync(bool userInitiated, CancellationToken cancellationToken)` updates the visible update state and optionally prompts for download.
- App startup awaits `CheckForUpdatesAsync(false, cancellationToken)` with a form-lifetime cancellation path, then continues existing auto-selection/start behavior regardless of update-check failure or decline.

- [ ] **Step 1: Split form code from the entry point**

  Move the current `MainForm` implementation to `MainForm.cs`. Keep `Program.cs` limited to loading embedded defaults, constructing the services, initializing WinForms, and opening `MainForm`.

- [ ] **Step 2: Reshape the desktop layout**

  Add a structured header with Mertviy version and engine version, a separate update status line and manual-check button, a clear service table, a compact action row, and a quiet diagnostic log area. Retain the existing profile selection, auto-select/start, manual start, stop, and QUIC behavior. Use restrained teal, green, and amber status colors on a light neutral surface; keep the form usable at its existing minimum size.

- [ ] **Step 3: Add service state styling**

  Style pending, selected, and fallback profile states with readable text and row-level color cues. Keep text and control sizes stable so status updates do not resize columns or shift the layout.

- [ ] **Step 4: Add non-blocking startup update behavior**

  Start the release check asynchronously on first display using a form-lifetime `CancellationTokenSource`, cancelled from `FormClosing`. If a newer release is available, show a Russian prompt with its version, size, and explicit Download/Skip choices. If declined or unavailable, continue auto-selection/start as before. During download, show byte progress. On successful checksum verification, show the saved path and an explicit button to launch the downloaded Mertviy executable; never launch it automatically.

- [ ] **Step 5: Add manual update check**

  Wire the header button to run the same update check while preventing overlapping requests. Update the status text for current, available, downloading, verified, cancelled, and unavailable states.

- [ ] **Step 6: Build the standalone executable**

  Run the `dotnet publish` command from Task 1.

  Expected: publish succeeds; inspect the UI and manually confirm the update prompt can be declined without interrupting the existing startup flow.

### Task 5: Publish reproducible Mertviy releases

**Files:**
- Create: `outputs/.github/workflows/release.yml`
- Modify: `outputs/Mertviy.csproj`
- Modify: `outputs/README.md`
- Modify: `outputs/EngineAssets/THIRD-PARTY-NOTICES.txt`

**Interfaces:**
- Pushing a `v*` tag triggers the Windows x64 release workflow.
- Each release contains `Mertviy-<version>-win-x64.exe` and `Mertviy-<version>-win-x64.exe.sha256`.

- [ ] **Step 1: Confirm repository visibility before release activation**

  Keep source changes local while the repository is private. Immediately before changing its visibility to public, show the user the repository name and state that all source and history will become visible, then obtain confirmation. After the confirmed change, verify the repository is publicly readable without authentication.

- [ ] **Step 2: Add tag-triggered Windows build**

  Create a workflow using `windows-latest`, checkout the tagged commit, install .NET 8, publish the self-contained executable with the command from Task 1, and fail if the executable or required engine notices are missing. Grant only `contents: write` to `GITHUB_TOKEN` for the release job:

  ```yaml
  on:
    push:
      tags: ["v*"]
  permissions:
    contents: write
  jobs:
    release:
      runs-on: windows-latest
  ```

- [ ] **Step 3: Generate the release checksum**

  Compute SHA-256 with PowerShell `Get-FileHash -Algorithm SHA256`, write the lowercase digest and exact executable filename to the `.sha256` asset, and ensure the name matches the updater's asset selector.

- [ ] **Step 4: Create the GitHub release**

  Use the runner's `GITHUB_TOKEN` with the GitHub CLI to create the release for the pushed tag and upload only the executable and checksum assets. Do not upload source-tree archives manually or commit generated executables.

- [ ] **Step 5: Document publishing and provenance**

  Add release-tag instructions, updater behavior, engine version tracking, source references, and third-party license notices to the README. Keep the Mertviy app version separate from the bundled engine version.

- [ ] **Step 6: Build locally and inspect the release payload**

  Run the `dotnet publish` command from Task 1, list the publish output, verify that required engine resources are embedded, and calculate the local checksum command used by CI.

  Expected: the release naming and checksum format match the updater's strict asset selection. No test project or automated test suite is added by this plan.

## Handoff

The first implementation run should stop after local repository setup and the first successful publish so the source layout and asset boundary can be reviewed before any remote push. The remote remains unchanged until the user explicitly asks to publish.
