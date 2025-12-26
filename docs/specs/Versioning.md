# PRD: Versioning and Auto-Update Integration

This document outlines the strategy for implementing application versioning and automatic updates for TerminalHost.

## Current State

- Version is hardcoded as `"TerminalHost v1.0"` in `HelpView.xaml:257`
- No versioning properties in the `.csproj` file
- No assembly version metadata
- No update checking mechanism

## Goals

1. **Track feature releases** - Know which version introduced which features
2. **Display version** - Show current version in Help window and Setup
3. **Minimal maintenance** - Avoid manual version bumping where possible
4. **Auto-update support** - Notify users of new versions and facilitate updates

## Recommended Solution

### Versioning: MinVer (Git Tag-Based)

**Package**: `MinVer` (NuGet)

MinVer automatically derives the assembly version from git tags, requiring zero configuration.

**How it works**:
1. Tag a release: `git tag v1.2.0`
2. MinVer reads the tag and sets `AssemblyVersion`, `FileVersion`, and `InformationalVersion`
3. Pre-release versions supported: `v1.2.0-beta.1`

**Integration**:
```xml
<!-- TerminalHost.csproj -->
<PackageReference Include="MinVer" Version="6.0.0" PrivateAssets="all" />
```

**Reading version at runtime**:
```csharp
var version = Assembly.GetExecutingAssembly()
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
    ?.InformationalVersion ?? "Unknown";
```

**Pros**:
- Zero configuration
- Version derived from git tags automatically
- Supports semantic versioning and pre-release tags
- Works with CI/CD pipelines

**Cons**:
- Requires git tags discipline
- Local builds without tags show commit-based versions

### Auto-Update: Updatum (GitHub Releases)

**Package**: `Updatum` v1.2.1 (NuGet)
**License**: MIT (free)
**Maintained**: Last updated Nov 28, 2025
**Frameworks**: .NET 8, 9, 10

Updatum is a lightweight library that automates updates using GitHub Releases.

**Features**:
- Checks GitHub Releases for new versions
- Displays changelog from release notes
- Downloads with progress tracking (WPF-bindable `INotifyPropertyChanged`)
- Auto-installs (ZIP extraction, single-file replacement, or MSI)
- Cross-platform (Windows, Linux, macOS)

**Integration**:
```csharp
// Create manager (e.g., in App.xaml.cs)
internal static readonly UpdatumManager AppUpdater = new("YourGitHubUser", "ConHoster")
{
    FetchOnlyLatestRelease = true,
    InstallUpdateSingleFileExecutableName = "host"
};

// Check on startup (after main window loads)
public async Task CheckForUpdatesAsync()
{
    var hasUpdate = await AppUpdater.CheckForUpdatesAsync();
    if (hasUpdate)
    {
        // Show update dialog with changelog
        var changelog = AppUpdater.GetChangelog();

        // If user accepts:
        var asset = await AppUpdater.DownloadUpdateAsync();
        if (asset != null)
        {
            await AppUpdater.InstallUpdateAsync(asset);
        }
    }
}
```

**WPF Progress Binding**:
```xml
<ProgressBar Value="{Binding DownloadedPercentage, Source={x:Static local:App.AppUpdater}}" Maximum="100"/>
<TextBlock Text="{Binding DownloadedMegabytes, Source={x:Static local:App.AppUpdater}}"/>
```

**GitHub Release Asset Naming**:
Assets must follow this pattern for Updatum to find them:
- `host_win-x64_v1.2.0.zip`
- `host_win-x64_v1.2.0.exe`

**Requirements**:
- Public GitHub repository (or authenticated API for private)
- Consistent asset naming in releases
- Version in assembly matches release tag

## Implementation Plan

### Phase 1: Versioning Infrastructure

1. Add MinVer package to `TerminalHost.csproj`
2. Create initial git tag: `git tag v1.0.0`
3. Create `VersionService.cs` to read and expose version
4. Update `HelpView.xaml` to bind version dynamically
5. Update `SetupWindow.xaml` to show version

### Phase 2: Update Checking

1. Add Updatum package
2. Create `UpdateService.cs` wrapper
3. Add update check on startup (with delay)
4. Create update notification UI (non-modal banner or dialog)
5. Add "Check for Updates" command to Command Palette

### Phase 3: Update Installation

1. Implement download progress UI
2. Add changelog display
3. Implement install flow (restart required)
4. Add "Skip This Version" persistence
5. Add settings option to disable auto-check

### Phase 4: Release Workflow

1. Document release process
2. Create GitHub Actions workflow for automated releases
3. Ensure asset naming matches Updatum requirements
4. Test update flow end-to-end

## Configuration Options

```json
{
  "settings": {
    "checkForUpdates": true,
    "skippedVersion": null,
    "lastUpdateCheck": "2025-12-17T00:00:00Z"
  }
}
```

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `checkForUpdates` | bool | true | Enable automatic update checks |
| `skippedVersion` | string | null | Version user chose to skip |
| `lastUpdateCheck` | datetime | null | Last time updates were checked |

## UI Components

### Update Available Banner

A non-intrusive banner at the top of the window when an update is available:

```
┌──────────────────────────────────────────────────────────────┐
│ ⬆ Update available: v1.3.0  [View Changes] [Update] [Later] │
└──────────────────────────────────────────────────────────────┘
```

### Update Dialog

For viewing changelog and initiating download:

- Version comparison (current → new)
- Scrollable changelog (markdown rendered)
- Download progress bar
- Buttons: Download & Install, Remind Later, Skip Version

### Settings Integration

Add to Settings > General section:
- Checkbox: "Check for updates automatically"
- Button: "Check Now"
- Last checked timestamp

## Alternative Solutions Considered

### Versioning Alternatives

| Solution | Pros | Cons |
|----------|------|------|
| **Manual csproj** | Simple, no deps | Manual updates required |
| **MinVer** (recommended) | Auto from git tags | Requires tag discipline |
| **Nerdbank.GitVersioning** | Flexible, CI-friendly | More complex setup |
| **GitVersion** | Feature-rich | Heavy, complex config |

### Auto-Update Alternatives

| Solution | Pros | Cons |
|----------|------|------|
| **Updatum** (recommended) | Simple, GitHub-native, WPF bindings | Newer library |
| **Velopack** | Delta updates, mature | More complex |
| **AutoUpdater.NET** | Simple, XML/JSON manifest | Less GitHub-integrated |
| **Squirrel.Windows** | Industry standard | Deprecated, no .NET 8 |
| **Manual GitHub API** | Full control | More code to maintain |

## Repository Requirements

For this implementation to work:
1. **GitHub repository** must be accessible (public recommended, private requires token)
2. **Releases** must be created with proper tags (e.g., `v1.2.0`)
3. **Assets** must follow naming convention: `host_win-x64_v{version}.zip`
4. **ZIP contents**: At minimum, `host.exe` + one other file (e.g., `README.md`)

## References

- [MinVer GitHub](https://github.com/adamralph/minver)
- [Updatum GitHub](https://github.com/sn4k3/Updatum)
- [Updatum NuGet](https://www.nuget.org/packages/Updatum)
- [Example Integration: WindowsEdgeLight](https://github.com/shanselman/WindowsEdgeLight/blob/master/docs/UPDATUM_INTEGRATION.md)

---

*Document Version: 1.0*
*Created: 2025-12-17*
