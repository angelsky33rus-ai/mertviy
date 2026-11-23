# Mertviy Update and Release Design

Date: 2026-11-24
Status: Draft for user review

## Goal

Make Mertviy an independently released Windows application with its own
versioned service profiles, a bundled network engine, a startup update check,
and a clearer control-panel interface. Users must not need to install Zapret or
start a separate `winws.exe` process.

## Architecture

- Keep the existing .NET 8 WinForms application and its managed child-process
  model.
- Maintain a Mertviy-owned engine distribution based on the open Flowseal/
  bol-van engine. Keep upstream notices and license texts, and include the
  corresponding source/build instructions needed for compliant distribution.
- Keep YouTube, Discord, and Telegram profile definitions in versioned config
  files rather than hard-coding the profile list and strategy arguments in the
  form. Package defaults with Mertviy and preserve user-selected settings.
- Publish the app, engine bundle, profile defaults, and notices as a
  self-contained `win-x64` release in the Mertviy GitHub repository. A Windows
  GitHub Actions workflow builds `Mertviy-<version>-win-x64.exe` and a matching
  SHA-256 checksum file from a version tag.
- Use `angelsky33rus-ai/mertviy` as the source and release repository. The user
  chose to make this repository public so installed app builds can read release
  metadata and download assets without embedding a private credential.
- Do not query Flowseal as the app's update source.

## Update Flow

1. At startup, query the Mertviy GitHub Releases API for the latest stable
   release. The check is asynchronous and has a short timeout.
2. Compare the release version with the running app version. A matching or
   older release updates the UI status and continues normal startup.
3. A newer release prompts the user to download the Mertviy Windows asset.
   Declining continues normal startup.
4. Download the selected asset to a Mertviy updates directory. Verify it using
   a SHA-256 checksum published with that release. Reject incomplete downloads
   or mismatched checksums and keep the current executable untouched.
5. After a verified download, show its path and let the user open or launch it
   explicitly. Do not replace a running executable or launch a downloaded file
   silently.
6. Network errors, missing releases, rate limits, and invalid release metadata
   produce a non-blocking status in the app and do not prevent using the
   installed version.

The UI also offers a manual check. Stable releases are used; prereleases are
ignored unless a future explicit preview setting is added.

## UI Changes

- Keep the Windows desktop form and existing service workflow.
- Establish a clear header with Mertviy/app version and engine version.
- Present update state and network-engine state distinctly.
- Improve service rows with readable status, selected profile, and last probe
  time; visually distinguish success, fallback, and pending states.
- Group the primary actions and QUIC setting in a compact control band.
- Keep the diagnostic log readable and secondary to the service controls.
- Retain Russian labels and the current auto-selection, manual profile,
  start, and stop workflows.

## Failure and Security Behavior

- Update checking never blocks the UI and never gates normal startup.
- Only HTTPS GitHub API/release URLs from the configured Mertviy repository are
  accepted. Validate repository coordinates and release asset names before
  downloading.
- Verify SHA-256 before presenting a download as ready.
- Preserve the current app and engine when an update is incomplete or invalid.
- Keep the administrator requirement for WinDivert and clearly report when it
  is missing.
- Keep upstream attributions and license obligations visible in the repo and
  release package. Mertviy branding does not imply original authorship of the
  upstream engine.

## Scope and Constraints

- This design does not implement a new packet-processing engine from scratch.
- This design does not perform silent or in-place self-updates.
- The app version and bundled engine version are tracked separately.
- The Mertviy repository must be public before an installed app can use its
  GitHub Releases API without user credentials. Changing repository visibility
  requires a separate confirmation immediately before that action.

## Verification Plan

- Build the self-contained Windows x64 executable and inspect release assets.
- Manually inspect updater behavior for current, newer, prerelease, malformed,
  and unavailable release metadata, and for successful or invalid checksums.
- Review the WinForms UI at desktop size and confirm update prompts do not
  prevent normal startup when declined or unavailable.
- Confirm the package contains the engine files and required notices, and that
  a separate Zapret installation is not required.
