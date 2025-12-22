# Stage 8: Testing & Polish

## Overview

| Attribute | Value |
|-----------|-------|
| **Estimated Effort** | 5-7 days |
| **Risk Level** | Medium |
| **Dependencies** | All previous stages complete |
| **Blocking For** | Release |

## Objective

Ensure application quality through comprehensive testing, performance optimization, and macOS-specific polish.

## Success Criteria

- [ ] All unit tests pass
- [ ] Manual testing complete
- [ ] Performance acceptable
- [ ] macOS conventions followed
- [ ] App bundle properly configured
- [ ] Documentation updated

---

## Testing Strategy

### 8.1 Unit Tests

**Location:** `tests/TerminalHost.Tests/`

All existing unit tests should pass without modification since they use mocked services.

```bash
cd tests/TerminalHost.Tests
dotnet test
```

**Expected Results:**
- ConfigurationServiceTests: 4 tests pass
- GitStatusServiceTests: 15 tests pass
- ProjectDetectionServiceTests: 8 tests pass
- JsonFileServiceTests: 7 tests pass
- MainViewModelTests: 6 tests pass
- SettingsTabViewModelTests: 4 tests pass

**Total: ~44 tests passing**

### 8.2 New Service Tests

Create tests for new platform services:

**CREATE:** `tests/TerminalHost.Tests/Services/SystemInfoServiceTests.cs`

```csharp
using Shouldly;
using TerminalHost.Services;
using Xunit;

namespace TerminalHost.Tests.Services;

public class SystemInfoServiceTests
{
    private readonly SystemInfoService _service = new();

    [Fact]
    public void GetApplicationDataPath_ReturnsValidPath()
    {
        var path = _service.GetApplicationDataPath();

        path.ShouldNotBeNullOrEmpty();
        path.ShouldContain("TerminalHost");
        path.ShouldContain("Library/Application Support");
    }

    [Fact]
    public void GetUserHomePath_ReturnsValidPath()
    {
        var path = _service.GetUserHomePath();

        path.ShouldNotBeNullOrEmpty();
        Directory.Exists(path).ShouldBeTrue();
    }

    [Fact]
    public void GetDefaultShell_ReturnsExistingShell()
    {
        var shell = _service.GetDefaultShell();

        shell.ShouldNotBeNullOrEmpty();
        File.Exists(shell).ShouldBeTrue();
    }

    [Fact]
    public void GetInstalledFontFamilies_ReturnsNonEmpty()
    {
        var fonts = _service.GetInstalledFontFamilies().ToList();

        fonts.ShouldNotBeEmpty();
    }
}
```

**CREATE:** `tests/TerminalHost.Tests/Services/ProcessServiceTests.cs`

```csharp
using Moq;
using Shouldly;
using TerminalHost.Services;
using Xunit;

namespace TerminalHost.Tests.Services;

public class ProcessServiceTests
{
    [Fact]
    public void OpenFolder_DoesNotThrow()
    {
        var service = new ProcessService();
        var tempDir = Path.GetTempPath();

        // Should not throw
        Should.NotThrow(() => service.OpenFolder(tempDir));
    }

    [Fact]
    public void RevealInFinder_DoesNotThrow()
    {
        var service = new ProcessService();
        var tempFile = Path.GetTempFileName();

        try
        {
            Should.NotThrow(() => service.RevealInFinder(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
```

---

### 8.3 Integration Tests

Create basic integration tests for terminal functionality:

**CREATE:** `tests/TerminalHost.Tests/Integration/TerminalIntegrationTests.cs`

```csharp
using Shouldly;
using TerminalHost.Services;
using Xunit;

namespace TerminalHost.Tests.Integration;

public class TerminalIntegrationTests
{
    [Fact(Skip = "Requires macOS environment")]
    public async Task TerminalFactory_CanCreateTerminal()
    {
        // This test requires the full Avalonia/Pty.Net stack
        // Run manually on macOS

        var fileSystem = new FileSystem();
        var dialogService = new Mock<IDialogService>().Object;
        var systemInfo = new SystemInfoService();

        var factory = new TerminalControlFactory(fileSystem, dialogService, systemInfo);

        var session = new TerminalSession(
            new Profile { Command = "/bin/zsh", WorkingDir = "~" },
            new Mock<IStatisticsService>().Object,
            new Mock<IClipboardService>().Object,
            "test");

        var control = await factory.CreateTerminalControlAsync(session);

        control.ShouldNotBeNull();
        control.IsProcessRunning.ShouldBeTrue();

        // Cleanup
        control.Kill();
    }
}
```

---

### 8.4 Manual Testing Checklist

#### Application Launch
- [ ] App launches without errors
- [ ] Main window displays correctly
- [ ] Dark theme applies
- [ ] Window position restores from last session

#### Tab Management
- [ ] Create new project tab (Ctrl+N)
- [ ] Close tab (Ctrl+W, middle-click, X button)
- [ ] Switch tabs (Ctrl+PageDown/Up, Ctrl+1-9)
- [ ] Tab reordering via drag
- [ ] Tab overflow dropdown works

#### Terminal Functionality
- [ ] Terminal renders output correctly
- [ ] Typing sends input to shell
- [ ] Arrow keys work for history/navigation
- [ ] Tab completion works
- [ ] Ctrl+C interrupts commands
- [ ] Colors display correctly (ls --color)
- [ ] Ctrl+` switches between custom/shell

#### File Operations
- [ ] Open file viewer (Ctrl+O)
- [ ] File picker dialog works
- [ ] Folder picker dialog works
- [ ] File explorer panel works (Ctrl+Shift+F)
- [ ] Open in Finder works (Ctrl+E)

#### Git Integration
- [ ] Git branch shows in status bar
- [ ] Git branch switcher works (Ctrl+B)
- [ ] Git changes panel works (Ctrl+G)
- [ ] File status colors correct

#### Settings
- [ ] Settings tab opens (Ctrl+,)
- [ ] Rich mode editing works
- [ ] Raw JSON mode works
- [ ] Save/reset buttons work
- [ ] Settings persist after restart

#### Keyboard Shortcuts
Test all shortcuts from help (F1):
- [ ] F1 - Help
- [ ] Ctrl+Shift+P - Command palette
- [ ] Ctrl+Shift+T - Tab switcher
- [ ] All other documented shortcuts

#### Window Management
- [ ] Resize window
- [ ] Minimize/restore
- [ ] Maximize/restore
- [ ] Window position persists

---

### 8.5 Performance Testing

#### Startup Time
```bash
time ./host
```
Target: < 2 seconds to window display

#### Memory Usage
```bash
# Monitor in Activity Monitor
# Target: < 200MB for basic usage
# Target: < 500MB with 5 terminal tabs
```

#### Terminal Rendering
- Scroll large output (e.g., `cat /usr/share/dict/words`)
- Target: Smooth scrolling, no visible lag

#### Large File Preview
- Open files > 1MB
- Target: < 1 second to display

---

## macOS-Specific Polish

### 8.6 Menu Bar Integration

**UPDATE:** `src/TerminalHost/TerminalHost/MainWindow.axaml.cs`

```csharp
protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
{
    base.OnAttachedToVisualTree(e);

    // Set up macOS native menu
    if (OperatingSystem.IsMacOS())
    {
        var menu = NativeMenu.GetMenu(this);
        if (menu == null)
        {
            menu = new NativeMenu();
            NativeMenu.SetMenu(this, menu);
        }

        // File menu
        var fileMenu = new NativeMenuItem("File");
        fileMenu.Menu = new NativeMenu();
        fileMenu.Menu.Add(new NativeMenuItem("New Project")
        {
            Command = _viewModel.OpenNewProjectCommand,
            Gesture = new KeyGesture(Key.N, KeyModifiers.Meta)
        });
        fileMenu.Menu.Add(new NativeMenuItem("Close Tab")
        {
            Command = _viewModel.CloseCurrentTabCommand,
            Gesture = new KeyGesture(Key.W, KeyModifiers.Meta)
        });
        menu.Add(fileMenu);

        // Edit menu
        var editMenu = new NativeMenuItem("Edit");
        editMenu.Menu = new NativeMenu();
        editMenu.Menu.Add(new NativeMenuItem("Copy")
        {
            Gesture = new KeyGesture(Key.C, KeyModifiers.Meta)
        });
        editMenu.Menu.Add(new NativeMenuItem("Paste")
        {
            Gesture = new KeyGesture(Key.V, KeyModifiers.Meta)
        });
        menu.Add(editMenu);

        // View menu
        var viewMenu = new NativeMenuItem("View");
        viewMenu.Menu = new NativeMenu();
        viewMenu.Menu.Add(new NativeMenuItem("Settings")
        {
            Command = _viewModel.OpenSettingsCommand,
            Gesture = new KeyGesture(Key.OemComma, KeyModifiers.Meta)
        });
        menu.Add(viewMenu);
    }
}
```

### 8.7 Keyboard Modifiers

On macOS, use Cmd (Meta) instead of Ctrl for standard shortcuts:

```csharp
// Add platform-aware shortcuts
if (OperatingSystem.IsMacOS())
{
    KeyBindings.Add(new KeyBinding
    {
        Gesture = new KeyGesture(Key.N, KeyModifiers.Meta),
        Command = _viewModel.OpenNewProjectCommand
    });
}
else
{
    KeyBindings.Add(new KeyBinding
    {
        Gesture = new KeyGesture(Key.N, KeyModifiers.Control),
        Command = _viewModel.OpenNewProjectCommand
    });
}
```

### 8.8 Font Updates

**UPDATE:** Typography resources for macOS:

```xml
<!-- macOS system fonts -->
<FontFamily x:Key="FontFamilyDefault">-apple-system, SF Pro, Helvetica Neue</FontFamily>
<FontFamily x:Key="FontFamilyMonospace">SF Mono, Menlo, Monaco, Consolas</FontFamily>
```

### 8.9 Path Display

Show paths using macOS conventions:

```csharp
// Display ~/Documents instead of /Users/name/Documents
public static string FormatPath(string path)
{
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    if (path.StartsWith(home))
    {
        return "~" + path[home.Length..];
    }
    return path;
}
```

---

## App Bundle Configuration

### 8.10 Create App Bundle

**CREATE:** Build script for macOS app bundle

```bash
#!/bin/bash
# build-macos.sh

# Configuration
APP_NAME="TerminalHost"
BUNDLE_ID="com.terminalhost.app"
VERSION="1.0.0"

# Build
dotnet publish src/TerminalHost/TerminalHost \
    -c Release \
    -r osx-arm64 \
    -o publish/osx-arm64

# Create app bundle structure
mkdir -p "publish/${APP_NAME}.app/Contents/MacOS"
mkdir -p "publish/${APP_NAME}.app/Contents/Resources"

# Copy executable
cp publish/osx-arm64/host "publish/${APP_NAME}.app/Contents/MacOS/"

# Copy Info.plist
cp src/TerminalHost/TerminalHost/Info.plist "publish/${APP_NAME}.app/Contents/"

# Copy icon
cp src/TerminalHost/TerminalHost/Resources/app.icns \
    "publish/${APP_NAME}.app/Contents/Resources/"

# Make executable
chmod +x "publish/${APP_NAME}.app/Contents/MacOS/host"

echo "App bundle created at publish/${APP_NAME}.app"
```

### 8.11 Code Signing (Optional)

For distribution outside App Store:

```bash
# Sign the app (requires Apple Developer account)
codesign --force --deep --sign "Developer ID Application: Your Name" \
    "publish/TerminalHost.app"

# Verify signature
codesign --verify --verbose "publish/TerminalHost.app"
```

### 8.12 Notarization (Optional)

For distribution:

```bash
# Create ZIP for notarization
ditto -c -k --keepParent "publish/TerminalHost.app" "TerminalHost.zip"

# Submit for notarization
xcrun notarytool submit "TerminalHost.zip" \
    --apple-id "your@email.com" \
    --password "app-specific-password" \
    --team-id "TEAM_ID" \
    --wait

# Staple ticket
xcrun stapler staple "publish/TerminalHost.app"
```

---

## Documentation Updates

### 8.13 Update PRD.md

Update the main PRD to reflect macOS-only status:

- Remove Windows-specific instructions
- Update keyboard shortcuts (Cmd vs Ctrl)
- Update installation instructions
- Update build commands

### 8.14 Update CLAUDE.md

Update developer documentation:

- Update build commands for macOS
- Update test running instructions
- Document new service abstractions
- Update contribution guidelines

### 8.15 Create README for macOS

**CREATE:** `README-MACOS.md`

```markdown
# TerminalHost for macOS

A terminal host application for managing project terminals with Claude Code integration.

## Requirements

- macOS 12.0 or later
- .NET 8.0 Runtime (bundled in self-contained build)

## Installation

### From Release
1. Download `TerminalHost.app.zip`
2. Extract and drag to Applications
3. Right-click and select "Open" (first time only)

### From Source
```bash
git clone <repo>
cd TerminalHost
./build-macos.sh
```

## Usage

```bash
# Open app
open /Applications/TerminalHost.app

# Open with specific project
open /Applications/TerminalHost.app --args ~/my-project
```

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| Cmd+N | New Project |
| Cmd+W | Close Tab |
| Cmd+, | Settings |
| Cmd+` | Switch Terminal |
| Cmd+B | Git Branch |
| Cmd+G | Git Changes |
| F1 | Help |

## Known Issues

- First launch may require right-click → Open due to Gatekeeper
- System tray not yet implemented

## License

[Your license]
```

---

## Final Verification

### 8.16 Complete Test Pass

Run through all functionality one final time:

1. Fresh install test
2. Upgrade from previous version (if applicable)
3. All keyboard shortcuts
4. All menu items
5. Settings persistence
6. Window state persistence
7. Multiple terminals
8. Git operations
9. File operations

### 8.17 Performance Baseline

Document performance metrics:

| Metric | Target | Actual |
|--------|--------|--------|
| Startup time | < 2s | ___ |
| Memory (idle) | < 100MB | ___ |
| Memory (5 tabs) | < 500MB | ___ |
| Terminal scroll | Smooth | ___ |

---

## Release Checklist

- [ ] All unit tests pass
- [ ] All manual tests pass
- [ ] Performance acceptable
- [ ] App bundle created
- [ ] Code signed (optional)
- [ ] Notarized (optional)
- [ ] Documentation updated
- [ ] README updated
- [ ] Release notes written
- [ ] GitHub release created

---

## Post-Migration Tasks

1. **Monitor Issues:** Watch for bug reports specific to macOS
2. **Performance Tuning:** Profile and optimize as needed
3. **Feature Parity:** Ensure all WPF features work in Avalonia
4. **User Feedback:** Collect and address user feedback
5. **Maintenance:** Keep dependencies updated

---

## Congratulations!

Upon completing Stage 8, the TerminalHost macOS migration is complete. The application should now run natively on macOS with full functionality.
