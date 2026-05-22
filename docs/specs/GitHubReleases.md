# GitHub Releases Pipeline

**Status:** Draft
**Owner:** TBD
**Related specs:** [Versioning.md](Versioning.md), [CrossPlatform.md](CrossPlatform.md)

## Problem

TerminalHost has no automated release pipeline. The single existing workflow (`.github/workflows/dotnet.yml`) is a `workflow_dispatch` build+test on `windows-latest` with no publish step, and the repository has zero git tags. There is a working local `scripts/build-macos.sh` that produces a `.dmg`, but no Linux equivalent and no CI integration for any platform.

Consequence: users cannot download a runnable build from the GitHub repository homepage. Every install requires checking out the source, installing the .NET 8 SDK, and running `dotnet publish` manually with platform-specific incantations (e.g., the forward-slash `-p:PublishDir` workaround required for Whisper native DLLs).

## Goals

1. Pushing a `v*.*.*` git tag produces a published GitHub Release with downloadable artifacts for Windows, macOS, and Linux.
2. Artifacts are named per the Updatum convention from [Versioning.md](Versioning.md): `host_<rid>_v<version>.<ext>` (and `host-avalonia_<rid>_v<version>.<ext>` for the Avalonia variant), so the future in-app auto-updater can consume them directly.
3. Both shippable GUI apps are released side-by-side: the WPF `TerminalHost` (Windows-only, primary product) and the cross-platform `TerminalHost.Avalonia`.
4. Versioning is derived from git tags via MinVer — no hardcoded version strings.
5. Manual `workflow_dispatch` runs produce **draft** releases for dry-run testing, never published.

## Non-Goals (v1)

- **Code signing / notarization.** Windows artifacts ship unsigned (SmartScreen warning), macOS `.dmg` ships unsigned (Gatekeeper "Open Anyway"). Document the warnings in README; defer signing infrastructure to a follow-up spec.
- **Installers.** No MSI/NSIS/Squirrel on Windows, no `.deb`/`.rpm`/AppImage on Linux. Portable zip and `.dmg` only.
- **ARM Windows / ARM Linux** artifacts. macOS ships both `osx-arm64` and `osx-x64`; everything else is `*-x64` only.
- **In-app auto-update wiring** (Updatum integration). Asset *naming* must be Updatum-compatible, but consuming the feed is out of scope here — owned by [Versioning.md](Versioning.md).
- **TerminalHost.CmdPal** (MSIX, ships through Microsoft Store, separate channel).
- **TerminalHost.Channel** (internal stdio bridge, not user-facing).
- **Release-please / changelog enforcement.** Use GitHub's built-in `generate_release_notes` from PR titles.

## Solution Overview

A new workflow `.github/workflows/release.yml` triggered by tag push (`v*.*.*`) or manual dispatch. Four jobs run as a fan-out/fan-in:

```
                   ┌─ build-windows  (windows-latest) ─┐
   tag push v1.2.3 ┼─ build-macos    (macos-14)       ─┼─→ release (ubuntu-latest)
                   └─ build-linux    (ubuntu-latest)  ─┘
```

Build jobs run in parallel, each producing one or more artifacts uploaded via `actions/upload-artifact@v4`. The `release` job depends on all three, downloads everything with `pattern: host*` and `merge-multiple: true`, and creates the GitHub Release via `softprops/action-gh-release@v2` with `generate_release_notes: true`.

MinVer adoption is bundled into this PRD because asset versioning depends on it, and the change is small (one NuGet reference + one property + replacing the hardcoded "v1.0" string in `HelpView.xaml`).

## Detailed Design

### 1. Versioning (MinVer)

Add a repo-root `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <MinVerTagPrefix>v</MinVerTagPrefix>
    <MinVerDefaultPreReleaseIdentifiers>alpha.0</MinVerDefaultPreReleaseIdentifiers>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="MinVer" Version="5.0.0" PrivateAssets="All" />
  </ItemGroup>
</Project>
```

This applies to every project in the solution including the existing `Directory.Build.props` in `src/TerminalHost.CmdPal/` — that file should be merged or left as-is (CmdPal is out of scope; it can override `<MinVerSkip>true</MinVerSkip>` if MinVer interferes with its MSIX versioning).

Replace the hardcoded version in `src/TerminalHost/TerminalHost/Views/Popups/HelpView.xaml:257` (`"TerminalHost v1.0"`) with a binding to a `VersionString` property on `MainViewModel` (or a small `IVersionService`), reading:

```csharp
typeof(App).Assembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion ?? "dev";
```

Tag convention: `v1.2.3`, `v1.2.3-beta.1`. Pre-releases (anything containing `-`) trigger the release workflow but pass `prerelease: true` to `action-gh-release` and are *not* listed as the "latest" release.

### 2. Workflow Triggers

```yaml
on:
  push:
    tags: ['v*.*.*']
  workflow_dispatch:  # produces a draft release for dry-run testing
```

The `release` job branches on trigger: tag push → published release; `workflow_dispatch` → draft (`draft: true`).

### 3. Per-Platform Build Steps

All build jobs share these prerequisites:

```yaml
- uses: actions/checkout@v4
  with:
    fetch-depth: 0   # required for MinVer to see tags
- uses: actions/setup-dotnet@v4
  with:
    dotnet-version: 8.0.x
```

Version captured into a job env var via `${{ github.ref_name }}` on tag push, or via `dotnet minver` invocation on manual dispatch (so dry runs get a sensible `0.0.0-alpha.0.N` name).

#### 3a. `build-windows` (`windows-latest`)

Builds **both** publishable Windows apps:

**WPF (`TerminalHost`, primary product):**
```bash
dotnet publish src/TerminalHost/TerminalHost -c Release \
  -p:PublishDir=./publish/wpf/
```
- The csproj already sets `PublishSingleFile=true`, `SelfContained=true`, `IncludeNativeLibrariesForSelfExtract=true`, `RuntimeIdentifier=win-x64`, plus a custom `CopyWhisperNativeLibs` target that copies four Whisper DLLs and `conpty.dll` next to `host.exe`. **Do not override these on the CLI** — let the csproj drive.
- Use forward slashes in `PublishDir` per the CLAUDE.md note (Whisper copy target is path-sensitive).
- Zip the *entire* publish directory (not just `host.exe`) — the loose native DLLs must travel with it.

**Avalonia (`TerminalHost.Avalonia`, secondary):**
```bash
dotnet publish src/TerminalHost.Avalonia -c Release -r win-x64 \
  -p:PublishDir=./publish/avalonia-win/
```

Artifacts produced:
- `host_win-x64_v<version>.zip`
- `host-avalonia_win-x64_v<version>.zip`

#### 3b. `build-macos` (`macos-14`, Apple Silicon)

Adapt `scripts/build-macos.sh` to accept a `$VERSION` env var (currently hardcoded to `1.0.0`) and to skip the codesign block when `$SIGNING_IDENTITY` is empty (unsigned v1).

For each of `osx-arm64` and `osx-x64`:
1. `dotnet publish src/TerminalHost.Avalonia -c Release -r <rid> -p:PublishDir=...`
2. Wrap into `.app` bundle (existing script logic): `Contents/MacOS/`, `Contents/Resources/` (including `pty_helper.py`), `Info.plist` with `CFBundleVersion=$VERSION`.
3. `hdiutil create` into a `.dmg` (script already does this).

Artifacts produced:
- `host-avalonia_osx-arm64_v<version>.dmg`
- `host-avalonia_osx-x64_v<version>.dmg`

Universal binary is **not** produced — keep two separate DMGs.

#### 3c. `build-linux` (`ubuntu-latest`)

```bash
dotnet publish src/TerminalHost.Avalonia -c Release -r linux-x64 \
  -p:PublishDir=./publish/linux/ \
  -p:InvariantGlobalization=true
```

`InvariantGlobalization=true` avoids the libicu runtime dependency on minimal distros (acceptable trade-off for v1 since the UI is English-only; revisit if localization lands).

Generate a `TerminalHost.sh` launcher next to the binary (`cd "$(dirname "$0")" && exec ./TerminalHost.Avalonia "$@"`), `chmod +x` both, then `tar -czf` the publish dir.

Artifacts produced:
- `host-avalonia_linux-x64_v<version>.tar.gz`

Release notes section to mention `libice6 libsm6 libfontconfig1` as runtime prerequisites on minimal distros.

### 4. Artifact Naming Summary

| RID | Project | Filename |
|-----|---------|----------|
| `win-x64` | TerminalHost (WPF) | `host_win-x64_v<version>.zip` |
| `win-x64` | TerminalHost.Avalonia | `host-avalonia_win-x64_v<version>.zip` |
| `osx-arm64` | TerminalHost.Avalonia | `host-avalonia_osx-arm64_v<version>.dmg` |
| `osx-x64` | TerminalHost.Avalonia | `host-avalonia_osx-x64_v<version>.dmg` |
| `linux-x64` | TerminalHost.Avalonia | `host-avalonia_linux-x64_v<version>.tar.gz` |

Total: **5 assets per release**. The `host_` prefix without a variant suffix is reserved for the WPF primary product (matches Versioning.md verbatim); `host-avalonia_` is the Avalonia variant.

### 5. Release Job

```yaml
release:
  runs-on: ubuntu-latest
  needs: [build-windows, build-macos, build-linux]
  permissions:
    contents: write
  steps:
    - uses: actions/download-artifact@v4
      with:
        pattern: host*
        merge-multiple: true
        path: ./artifacts
    - uses: softprops/action-gh-release@v2
      with:
        files: ./artifacts/*
        generate_release_notes: true
        draft: ${{ github.event_name == 'workflow_dispatch' }}
        prerelease: ${{ contains(github.ref_name, '-') }}
```

Add `.github/release.yml` to categorize PRs by label in the auto-generated notes (Features / Bug Fixes / Other).

## Phases

**Phase 1 — Versioning groundwork**
- [x] Add root `Directory.Build.props` with MinVer.
- [x] Replace hardcoded "v1.0" in `HelpView.xaml` with `AssemblyInformationalVersionAttribute` lookup (via `IVersionService` injected into `MainViewModel`, proxied through `HelpViewModel`).
- [x] Verify `dotnet build` on a clean checkout still succeeds; with no tags MinVer produced `0.0.0-alpha.0.515+<sha>`.
- [x] Tag `v0.1.0-alpha.1` locally, rebuild, verify version string updates to `0.1.0-alpha.1+<sha>`.

**Phase 2 — Windows release**
- [x] Create `.github/workflows/release.yml` with `build-windows` job and a `release` job (draft on `workflow_dispatch`).
- [ ] Trigger via `workflow_dispatch`, verify both Windows zips upload to a draft release.
- [ ] Manually download, extract, and run each on a clean Windows machine (no .NET SDK installed).

**Phase 3 — macOS release**
- [x] Parametrize `scripts/build-macos.sh` to read `$VERSION` and `$RUNTIME`. Codesign was already optional (existing `CODESIGN_IDENTITY` env var with ad-hoc fallback). DMG filename now follows the Updatum convention `host-avalonia_<rid>_v<version>.dmg`. Cleanup narrowed so a second invocation for the other RID does not wipe the first DMG. Plist version sanitized (strips `-prerelease` and `+sha`) to satisfy `CFBundleShortVersionString`.
- [x] Add `build-macos` job (`macos-14`) invoking the script for both `osx-arm64` and `osx-x64`; cross-publish runs on Apple Silicon.
- [ ] Verify on a clean Mac: the `.dmg` mounts, the `.app` launches after "Open Anyway".

**Phase 4 — Linux release**
- [ ] Add `build-linux` job.
- [ ] Verify on Ubuntu 24.04 LTS clean install (with `libice6 libsm6 libfontconfig1` installed): tarball extracts, launcher runs.

**Phase 5 — Production trigger**
- [ ] Switch trigger to `push: tags: ['v*.*.*']`; remove draft-only restriction for tag pushes.
- [ ] Add `.github/release.yml` for note categorization.
- [ ] Cut `v0.1.0` as the first real release.
- [ ] Update README with download instructions and SmartScreen/Gatekeeper warnings.

## Risks & Open Questions

- **WPF publish flakiness on CI.** The CLAUDE.md note about forward-slash `PublishDir` and the custom `CopyWhisperNativeLibs` target suggests Windows publish is finicky. Mitigation: keep all publish settings in the csproj; never override on the CLI. If the Whisper DLL copy fails on `windows-latest`, fall back to running PowerShell with forward-slash paths explicitly.
- **macOS runner phase-out.** GitHub is migrating runners; `macos-14` (Apple Silicon) is current. Pin explicitly; don't use `macos-latest` which has shifted meaning before.
- **`HelpView.xaml` binding context.** It's a popup, not always inside `MainViewModel`'s DataContext chain. May need a small `IVersionService` registered in DI rather than a direct binding. Confirm during Phase 1.
- **Avalonia version string.** No Help view in the Avalonia project today (per [AvaloniaPortParity.md](AvaloniaPortParity.md)). When that's ported, it needs the same MinVer-fed binding.
- **First-run experience for unsigned macOS apps.** Sequoia removed the right-click-Open bypass. README must document the Settings → Privacy & Security → "Open Anyway" path explicitly with a screenshot.
- **Open question: do we want a `latest` redirect?** GitHub provides `releases/latest/download/<asset>` for the most recent non-prerelease. If Updatum uses this in the future, asset naming must be stable across releases (✓ already designed for this).

## Deferred / Out of Scope

| Item | Owner / Trigger |
|------|-----------------|
| Windows code signing (EV cert + `signtool`) | Future spec |
| macOS notarization (Developer ID + `notarytool`) | Future spec |
| MSI / NSIS / Squirrel Windows installer | Future spec |
| `.deb` / AppImage / `.rpm` Linux packages | Future spec |
| ARM64 Windows / ARM64 Linux artifacts | When demand surfaces |
| In-app auto-update (Updatum consumer) | [Versioning.md](Versioning.md) |
| Universal macOS binary (`lipo`) | Defer indefinitely; two DMGs are clearer |
| TerminalHost.CmdPal MSIX release | Microsoft Store, separate channel |
| TerminalHost.Channel distribution | Internal; bundled with main app if needed |

## Success Criteria

1. `git push origin v0.1.0` produces a GitHub Release at `github.com/<owner>/TerminalHost/releases/tag/v0.1.0` containing all 5 assets.
2. A user landing on the repository homepage sees the latest release in the right sidebar with downloadable artifacts.
3. Each downloaded artifact runs on a clean target OS (Windows 11 / macOS 14+ / Ubuntu 24.04) without requiring the .NET SDK.
4. The Help view's version string matches the released tag exactly.
5. `workflow_dispatch` runs always produce drafts; tag pushes never produce drafts.
