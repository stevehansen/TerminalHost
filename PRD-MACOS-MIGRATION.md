# PRD: TerminalHost macOS Migration

## Executive Summary

This document outlines a comprehensive migration plan to port TerminalHost from a WPF Windows application to a macOS-native application using Avalonia UI. The migration eliminates all Windows dependencies and focuses exclusively on macOS support.

**Current State:** WPF .NET 8 Windows application with EasyWindowsTerminalControl
**Target State:** Avalonia .NET 8 macOS application with XtermSharp/Pty.Net

---

## Migration Overview

### Technology Stack Changes

| Component | Current (Windows) | Target (macOS) |
|-----------|-------------------|----------------|
| UI Framework | WPF | Avalonia UI 11.x |
| Terminal Control | EasyWindowsTerminalControl | XtermSharp + Pty.Net |
| PTY Library | ConPTY (Windows) | Pty.Net (POSIX) |
| Charts | LiveChartsCore.SkiaSharpView.WPF | LiveChartsCore.SkiaSharpView.Avalonia |
| WebView | Microsoft.Web.WebView2 | Avalonia.WebView or native WebKit |
| System Tray | System.Windows.Forms.NotifyIcon | Avalonia Desktop Notifications |
| Dialogs | Microsoft.Win32.OpenFileDialog | Avalonia IStorageProvider |

### Key Metrics

- **Total XAML Files:** 44 files requiring conversion
- **Total ViewModels:** 24 files (14 need modifications)
- **Total Services:** 26 service interfaces
- **P/Invoke Calls:** 4 files with Win32 interop to remove
- **Unit Tests:** 44 tests (all portable)
- **UI Tests:** 4 tests (must be rewritten - FlaUI is Windows-only)

---

## Stage Overview

| Stage | Focus | Est. Effort | Risk |
|-------|-------|-------------|------|
| **Stage 1** | Project Structure & Build System | 2-3 days | Low |
| **Stage 2** | Service Layer Abstractions | 3-5 days | Low |
| **Stage 3** | Domain Model Platform Independence | 2-3 days | Medium |
| **Stage 4** | Terminal Control Integration | 5-7 days | High |
| **Stage 5** | Core UI Migration (Avalonia) | 7-10 days | High |
| **Stage 6** | ViewModels Platform Independence | 3-5 days | Medium |
| **Stage 7** | Views & Controls Migration | 10-15 days | High |
| **Stage 8** | Testing & Polish | 5-7 days | Medium |

**Total Estimated Effort:** 37-55 developer days

---

# STAGE 1: Project Structure & Build System

## Objective
Convert the project structure from Windows-only to macOS-only, update all build configurations and dependencies.

## Deliverables
1. Updated .csproj targeting macOS
2. New NuGet package references for Avalonia
3. Removed Windows-specific packages
4. macOS-specific publish configuration
5. Updated solution structure

## Detailed File Changes

### 1.1 TerminalHost.csproj (CRITICAL)

**Remove:**
```xml
<TargetFramework>net8.0-windows</TargetFramework>
<UseWPF>true</UseWPF>
<UseWindowsForms>true</UseWindowsForms>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<ApplicationHighDpiMode>PerMonitorV2</ApplicationHighDpiMode>
```

**Replace with:**
```xml
<TargetFramework>net8.0</TargetFramework>
<RuntimeIdentifier>osx-arm64</RuntimeIdentifier>
<!-- Also consider osx-x64 for Intel Macs -->
```

**Remove NuGet Packages:**
- `EasyWindowsTerminalControl` (v1.0.36)
- `LiveChartsCore.SkiaSharpView.WPF` (v2.0.0-rc5.4)
- `Microsoft.Web.WebView2` (v1.0.3650.58)

**Add NuGet Packages:**
```xml
<PackageReference Include="Avalonia" Version="11.2.x" />
<PackageReference Include="Avalonia.Desktop" Version="11.2.x" />
<PackageReference Include="Avalonia.Themes.Fluent" Version="11.2.x" />
<PackageReference Include="LiveChartsCore.SkiaSharpView.Avalonia" Version="2.0.0-rc5.4" />
<PackageReference Include="Pty.Net" Version="x.x.x" />
<!-- XtermSharp may need to be added as git submodule or local project -->
```

**Remove conpty.dll copy target:**
```xml
<!-- DELETE THIS ENTIRE SECTION -->
<ItemGroup>
  <None Include="$(NuGetPackageRoot)ci.microsoft.windows.console.conpty\...">
    ...
  </None>
</ItemGroup>
```

**Remove WPF type aliases:**
All `<Using Include="System.Windows.*" .../>` items (lines 59-82)

### 1.2 Global Usings Updates

**Create `GlobalUsings.cs`:**
```csharp
// Remove all System.Windows.* usings
// Add Avalonia equivalents
global using Avalonia;
global using Avalonia.Controls;
global using Avalonia.Input;
global using Avalonia.Media;
global using Avalonia.Threading;
```

### 1.3 Solution File Updates

**Update TerminalHost.sln:**
- Keep project GUIDs
- Update any platform-specific configurations
- Remove Windows-specific build configurations

### 1.4 Test Projects

**tests/TerminalHost.Tests/TerminalHost.Tests.csproj:**
- Change `net8.0-windows` to `net8.0`
- No other changes needed (unit tests are platform-agnostic)

**tests/TerminalHost.UITests/TerminalHost.UITests.csproj:**
- **DELETE ENTIRE PROJECT** - FlaUI is Windows-only
- Create new `TerminalHost.MacTests` project later (Stage 8)

### 1.5 Resources

**Remove:**
- `Resources/app.ico` (Windows icon format)

**Add:**
- `Resources/app.icns` (macOS icon format)
- `Info.plist` (macOS app bundle metadata)
- `Entitlements.plist` (macOS sandbox permissions)

### 1.6 New Files to Create

**Info.plist:**
```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "...">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>TerminalHost</string>
    <key>CFBundleIdentifier</key>
    <string>com.yourcompany.terminalhost</string>
    <key>CFBundleVersion</key>
    <string>1.0.0</string>
    <key>CFBundleExecutable</key>
    <string>host</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
</dict>
</plist>
```

## Verification Checklist
- [ ] Project builds with `dotnet build`
- [ ] No Windows-specific framework references remain
- [ ] NuGet restore succeeds
- [ ] Basic console app runs on macOS

---

# STAGE 2: Service Layer Abstractions

## Objective
Create or update service abstractions to remove all Windows-specific implementations, preparing for macOS-native implementations.

## Deliverables
1. Platform-agnostic service interfaces
2. macOS implementations of platform services
3. Updated DI registration

## Detailed File Changes

### 2.1 New Service Interfaces to Create

**Services/IFolderPickerService.cs (NEW):**
```csharp
public interface IFolderPickerService
{
    Task<string?> PickFolderAsync(string? title = null, string? initialDirectory = null);
}
```

**Services/IFilePickerService.cs (NEW):**
```csharp
public interface IFilePickerService
{
    Task<string?> PickFileAsync(string? title = null, string? filter = null, string? initialDirectory = null);
    Task<string?> PickSaveFileAsync(string? title = null, string? defaultFileName = null, string? initialDirectory = null);
}
```

**Services/IDispatcherService.cs (NEW):**
```csharp
public interface IDispatcherService
{
    void Post(Action action);
    Task InvokeAsync(Action action);
    Task<T> InvokeAsync<T>(Func<T> func);
    bool CheckAccess();
}
```

**Services/ITimerService.cs (NEW):**
```csharp
public interface ITimerService
{
    ITimer CreateTimer(TimeSpan interval, Action callback);
}

public interface ITimer : IDisposable
{
    void Start();
    void Stop();
    bool IsRunning { get; }
}
```

**Services/IClipboardService.cs (NEW):**
```csharp
public interface IClipboardService
{
    Task SetTextAsync(string text);
    Task<string?> GetTextAsync();
    Task<bool> ContainsTextAsync();
}
```

**Services/ISystemInfoService.cs (NEW):**
```csharp
public interface ISystemInfoService
{
    string GetApplicationDataPath();
    string GetUserHomePath();
    IEnumerable<string> GetInstalledFontFamilies();
}
```

### 2.2 Service Implementations to Update

**Services/ProcessService.cs:**

Current Windows-specific code:
```csharp
// Opens explorer.exe
Process.Start(new ProcessStartInfo
{
    FileName = "explorer.exe",
    Arguments = $"\"{path}\"",
    UseShellExecute = true
});
```

**macOS implementation:**
```csharp
public void OpenFolder(string path)
{
    Process.Start(new ProcessStartInfo
    {
        FileName = "open",
        Arguments = $"\"{path}\"",
        UseShellExecute = false
    });
}

public void RevealInFinder(string filePath)
{
    Process.Start(new ProcessStartInfo
    {
        FileName = "open",
        Arguments = $"-R \"{filePath}\"",
        UseShellExecute = false
    });
}

public void OpenUrl(string url)
{
    Process.Start(new ProcessStartInfo
    {
        FileName = "open",
        Arguments = url,
        UseShellExecute = false
    });
}
```

**Services/ConfigurationService.cs (Line 22-24):**

Current:
```csharp
private static readonly string DefaultConfigDir =
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\TerminalHost";
```

**macOS implementation:**
```csharp
private static readonly string DefaultConfigDir =
    Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "Application Support", "TerminalHost");
```

**Services/SingleInstanceService.cs:**

Named pipes work on macOS but use different path conventions:

```csharp
// Windows: \\.\pipe\TerminalHost_IPC_Pipe
// macOS: /tmp/TerminalHost_IPC_Pipe or use Unix domain sockets

private const string PipeName = "/tmp/TerminalHost_IPC_Pipe";
```

Alternative: Use Unix domain sockets for better macOS compatibility.

**Services/SystemTrayService.cs (COMPLETE REWRITE):**

Current implementation uses `System.Windows.Forms.NotifyIcon` - completely Windows-specific.

**macOS approach:** Use `NSStatusBar` via Avalonia Desktop APIs or a native wrapper.

```csharp
// New file: Services/MacOsStatusBarService.cs
public class MacOsStatusBarService : ISystemTrayService
{
    // Avalonia doesn't have native tray support, options:
    // 1. Use platform-specific code with ObjCRuntime
    // 2. Use a third-party library
    // 3. Implement menu bar extra via native interop

    // For MVP, this can be a no-op implementation
}
```

### 2.3 Services to Delete (Windows-Only)

**Services/DarkModeHelper.cs (DELETE):**
- Uses `dwmapi.dll` P/Invoke for Windows dark title bar
- macOS handles dark mode automatically via system settings
- Avalonia respects system theme

### 2.4 Dialog Service Updates

**Services/DialogService.cs:**

Current uses WPF `MessageBox`. Replace with Avalonia dialogs:

```csharp
public class DialogService : IDialogService
{
    public async Task ShowErrorAsync(string message, string title)
    {
        var dialog = new MessageBoxWindow
        {
            Title = title,
            Message = message,
            Icon = MessageBoxIcon.Error
        };
        await dialog.ShowDialog(GetMainWindow());
    }

    // Similar for ShowInfo, ShowWarning, ShowConfirmation
}
```

### 2.5 Files in Services/ Directory - Full Analysis

| File | Windows-Specific | Action |
|------|------------------|--------|
| `ConfigurationService.cs` | Path convention | Update paths |
| `DialogService.cs` | WPF MessageBox | Rewrite for Avalonia |
| `DarkModeHelper.cs` | dwmapi.dll | DELETE |
| `FileEditService.cs` | No | Keep |
| `FileExplorerService.cs` | No | Keep |
| `FilePreviewService.cs` | No | Keep |
| `FileSystem.cs` | No | Keep |
| `GitProcessRunner.cs` | No | Keep |
| `GitPrService.cs` | Process.Start | Update commands |
| `GitStatusService.cs` | No | Keep |
| `GitHubService.cs` | Process.Start | Update commands |
| `JsonSyntaxHighlighter.cs` | FlowDocument (WPF) | Rewrite |
| `LinkDetectionService.cs` | No | Keep |
| `ProcessService.cs` | explorer.exe | Update commands |
| `ProfileRegistry.cs` | No | Keep |
| `ProjectDetectionService.cs` | No | Keep |
| `RunUrlDetectionService.cs` | No | Keep |
| `SessionManager.cs` | No | Keep |
| `SingleInstanceService.cs` | Named pipes | Update for macOS |
| `StatisticsService.cs` | No | Keep |
| `SystemTrayService.cs` | NotifyIcon | Rewrite |
| `TerminalControlFactory.cs` | EasyTerminalControl | Rewrite |
| `ToastService.cs` | WPF-based | Rewrite |
| `ClaudeCommandService.cs` | No | Keep |
| `TaskService.cs` | No | Keep |
| `AiAssistantService.cs` | No | Keep |
| `TestRunnerService.cs` | No | Keep |
| `MarkdownService.cs` | No | Keep |
| `DiffParserService.cs` | No | Keep |

## Verification Checklist
- [ ] All new interfaces created
- [ ] macOS service implementations build
- [ ] DI registration updated in App.xaml.cs
- [ ] Unit tests pass with mock services

---

# STAGE 3: Domain Model Platform Independence

## Objective
Remove all P/Invoke and Windows-specific code from Domain models.

## Deliverables
1. Platform-independent domain models
2. Terminal session without Win32 calls
3. Updated terminal focus detection

## Detailed File Changes

### 3.1 Domain/TerminalSession.cs (MAJOR CHANGES)

**Lines 461-563 - DELETE ALL P/Invoke:**
```csharp
// DELETE THESE:
[DllImport("user32.dll")]
private static extern IntPtr GetFocus();

[DllImport("user32.dll")]
private static extern IntPtr GetParent(IntPtr hWnd);

[DllImport("user32.dll")]
private static extern IntPtr WindowFromPoint(POINT point);

[DllImport("user32.dll")]
private static extern bool GetCursorPos(out POINT lpPoint);

[DllImport("user32.dll")]
private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

private struct POINT { public int X; public int Y; }
private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
```

**Replace `HasWin32Focus()` method (lines 480-519):**

Current implementation uses Win32 cursor/focus detection. Replace with:

```csharp
/// <summary>
/// Checks if this terminal has focus.
/// Uses tracked focus state instead of Win32 APIs.
/// </summary>
public bool HasFocus()
{
    // Delegate to terminal control's focus state
    return _terminalControl?.IsFocused ?? false;
}
```

**Replace `GetScreenBounds()` method (lines 521-556):**

Delete entirely - not needed for macOS implementation.

**Line 450 - Clipboard:**

Current:
```csharp
System.Windows.Clipboard.SetText(text);
```

Replace with injected service:
```csharp
private readonly IClipboardService _clipboardService;

public async Task<bool> CopySelectionToClipboardAsync()
{
    var text = GetSelectedText();
    if (!string.IsNullOrEmpty(text))
    {
        await _clipboardService.SetTextAsync(text);
        return true;
    }
    return false;
}
```

**Lines 83-109 - SetTerminalControl:**

Current binds to `EasyTerminalControl`. Replace with interface:

```csharp
public void SetTerminalControl(ITerminalControl control)
{
    _terminalControl = control;
    TerminalControl = control.NativeControl;

    control.OutputReceived += OnTerminalOutput;
    control.MouseDown += OnTerminalMouseDown;
}
```

### 3.2 Domain Files - Full Analysis

| File | Platform-Specific | Action |
|------|-------------------|--------|
| `AppConfiguration.cs` | No | Keep |
| `ClaudeCommand.cs` | No | Keep |
| `FileIconMapper.cs` | No | Keep |
| `FileSystemNode.cs` | No | Keep |
| `GitBranch.cs` | No | Keep |
| `GitFileStatus.cs` | No | Keep |
| `GitStatus.cs` | No | Keep |
| `LinkPattern.cs` | No | Keep |
| `PaletteCommand.cs` | No | Keep |
| `Profile.cs` | No | Keep |
| `ProjectType.cs` | No | Keep |
| `QuickCommand.cs` | No | Keep |
| `RunConfiguration.cs` | No | Keep |
| `RunState.cs` | No | Keep |
| `SessionState.cs` | No | Keep |
| `TerminalPair.cs` | No | Keep |
| **`TerminalSession.cs`** | **Yes - P/Invoke** | **Major rewrite** |

### 3.3 New Interface: ITerminalControl

**Domain/ITerminalControl.cs (NEW):**
```csharp
public interface ITerminalControl
{
    /// <summary>The native control to embed in UI.</summary>
    object NativeControl { get; }

    /// <summary>Whether the control has keyboard focus.</summary>
    bool IsFocused { get; }

    /// <summary>Write text to the terminal.</summary>
    void WriteToTerminal(string text);

    /// <summary>Get selected text from terminal.</summary>
    string GetSelectedText();

    /// <summary>Focus the terminal control.</summary>
    void Focus();

    /// <summary>Restart the terminal process.</summary>
    Task RestartAsync();

    /// <summary>Fired when output is received.</summary>
    event Action<string> OutputReceived;

    /// <summary>Fired on mouse events.</summary>
    event EventHandler<MouseEventArgs> MouseDown;
}
```

## Verification Checklist
- [ ] No P/Invoke remains in Domain/
- [ ] TerminalSession compiles without Win32
- [ ] Unit tests for TerminalSession pass
- [ ] ITerminalControl interface is complete

---

# STAGE 4: Terminal Control Integration

## Objective
Integrate XtermSharp and Pty.Net as the terminal emulation stack for macOS.

## Deliverables
1. XtermSharp integration (or alternative)
2. Pty.Net PTY management
3. New TerminalControlFactory implementation
4. Terminal theming support

## Critical Research Finding

**Recommended Stack:**
- **Pty.Net** (Microsoft) - Cross-platform PTY management
- **XtermSharp** - .NET port of xterm.js with macOS support

**Alternative if XtermSharp doesn't work:**
- **AvalonStudio.TerminalEmulator** - Avalonia-native terminal control

## Detailed Implementation

### 4.1 Add XtermSharp Reference

XtermSharp may need to be added as a submodule or local project:

```bash
git submodule add https://github.com/migueldeicaza/XtermSharp.git external/XtermSharp
```

Or reference the built package if available.

### 4.2 Services/TerminalControlFactory.cs (COMPLETE REWRITE)

Current creates `EasyTerminalControl`. Replace entirely:

```csharp
using Pty.Net;

public class TerminalControlFactory : ITerminalControlFactory
{
    public async Task<ITerminalControl> CreateTerminalControlAsync(TerminalSession session)
    {
        var profile = session.Profile;
        var workingDir = profile.GetExpandedWorkingDir();
        var shell = profile.Command ?? GetDefaultShell();

        // Create PTY process
        var options = new PtyOptions
        {
            Name = "xterm-256color",
            Cols = 120,
            Rows = 30,
            Cwd = workingDir,
            App = shell,
            Environment = GetEnvironment()
        };

        var ptyProcess = await PtyProvider.SpawnAsync(options, CancellationToken.None);

        // Create terminal view (XtermSharp or Avalonia control)
        var terminalControl = new MacTerminalControl(ptyProcess);

        return terminalControl;
    }

    private string GetDefaultShell()
    {
        // macOS default shell
        var shell = Environment.GetEnvironmentVariable("SHELL");
        return shell ?? "/bin/zsh";
    }

    private IDictionary<string, string> GetEnvironment()
    {
        var env = new Dictionary<string, string>();
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            env[entry.Key.ToString()!] = entry.Value?.ToString() ?? "";
        }
        env["TERM"] = "xterm-256color";
        env["COLORTERM"] = "truecolor";
        return env;
    }
}
```

### 4.3 New Control: MacTerminalControl

**Controls/MacTerminalControl.cs (NEW):**

```csharp
using Avalonia.Controls;
using Pty.Net;
using XtermSharp;

public class MacTerminalControl : UserControl, ITerminalControl
{
    private readonly IPtyConnection _pty;
    private readonly Terminal _terminal;

    public MacTerminalControl(IPtyConnection pty)
    {
        _pty = pty;
        _terminal = new Terminal(null, new TerminalOptions
        {
            Cols = 120,
            Rows = 30
        });

        // Wire up PTY output to terminal
        _pty.ProcessExited += OnProcessExited;
        StartReadingOutput();

        // Create Avalonia visual representation
        InitializeVisual();
    }

    private async void StartReadingOutput()
    {
        var buffer = new byte[4096];
        while (!_pty.HasExited)
        {
            var bytesRead = await _pty.ReaderStream.ReadAsync(buffer);
            if (bytesRead > 0)
            {
                var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                _terminal.Feed(text);
                OutputReceived?.Invoke(text);
                InvalidateVisual(); // Trigger repaint
            }
        }
    }

    public void WriteToTerminal(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        _pty.WriterStream.Write(bytes);
        _pty.WriterStream.Flush();
    }

    // ... implement remaining ITerminalControl members
}
```

### 4.4 Terminal Theming

**Domain/TerminalTheme.cs (NEW):**
```csharp
public class TerminalTheme
{
    public Color Background { get; set; } = Color.FromRgb(0x0C, 0x0C, 0x0C);
    public Color Foreground { get; set; } = Color.FromRgb(0xCC, 0xCC, 0xCC);
    public Color SelectionBackground { get; set; } = Color.FromRgb(0x26, 0x4F, 0x78);
    public Color[] AnsiColors { get; set; } = CampbellColorScheme.Colors;
}

public static class CampbellColorScheme
{
    public static Color[] Colors = new[]
    {
        Color.FromRgb(0x0C, 0x0C, 0x0C), // Black
        Color.FromRgb(0xC5, 0x0F, 0x1F), // Red
        Color.FromRgb(0x13, 0xA1, 0x0E), // Green
        Color.FromRgb(0xC1, 0x9C, 0x00), // Yellow
        Color.FromRgb(0x00, 0x37, 0xDA), // Blue
        Color.FromRgb(0x88, 0x17, 0x98), // Magenta
        Color.FromRgb(0x3A, 0x96, 0xDD), // Cyan
        Color.FromRgb(0xCC, 0xCC, 0xCC), // White
        // Bright variants...
    };
}
```

### 4.5 Built-in Shell Commands Update

Current code references Windows shells. Update for macOS:

```csharp
private static bool IsBuiltInCommand(string command)
{
    var builtIns = new[]
    {
        "zsh", "/bin/zsh",
        "bash", "/bin/bash",
        "sh", "/bin/sh",
        "fish", "/usr/local/bin/fish"
    };
    return builtIns.Any(b => command.EndsWith(b, StringComparison.OrdinalIgnoreCase));
}
```

## Verification Checklist
- [ ] Pty.Net spawns shell processes on macOS
- [ ] XtermSharp renders terminal output
- [ ] Keyboard input works
- [ ] Terminal themes apply correctly
- [ ] Process exit detection works

---

# STAGE 5: Core UI Migration (Avalonia)

## Objective
Migrate the application shell from WPF to Avalonia, including App.xaml and MainWindow.

## Deliverables
1. Avalonia App.axaml
2. Avalonia MainWindow.axaml
3. Application startup flow
4. Resource dictionary migration

## Detailed File Changes

### 5.1 App.xaml → App.axaml

**DELETE:** `App.xaml` and `App.xaml.cs`

**CREATE:** `App.axaml`
```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="TerminalHost.App"
             RequestedThemeVariant="Dark">
    <Application.Styles>
        <FluentTheme />
        <StyleInclude Source="avares://TerminalHost/Styles/AppStyles.axaml"/>
    </Application.Styles>

    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceInclude Source="avares://TerminalHost/Resources/Colors.axaml"/>
                <ResourceInclude Source="avares://TerminalHost/Resources/Converters.axaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

**CREATE:** `App.axaml.cs`
```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;

namespace TerminalHost;

public partial class App : Application
{
    private IServiceProvider? _services;
    private ISingleInstanceService? _singleInstanceService;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = CommandLineArgs.Parse(desktop.Args ?? Array.Empty<string>());

            // Single instance handling
            _singleInstanceService = new SingleInstanceService();
            if (!args.DisableSingleInstance && !_singleInstanceService.TryAcquireLock())
            {
                if (args.HasValidRequest())
                    SingleInstanceService.SendToRunningInstance(args);
                desktop.Shutdown();
                return;
            }

            // Configure DI
            var services = new ServiceCollection();
            ConfigureServices(services, args);
            _services = services.BuildServiceProvider();

            // Show main window
            desktop.MainWindow = _services.GetRequiredService<MainWindow>();
            desktop.ShutdownRequested += OnShutdownRequested;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ConfigureServices(IServiceCollection services, CommandLineArgs args)
    {
        // Same service registration as before, with macOS implementations
    }
}
```

### 5.2 MainWindow.xaml → MainWindow.axaml

**Key XAML Differences (WPF → Avalonia):**

| WPF | Avalonia |
|-----|----------|
| `xmlns="http://schemas..."` | `xmlns="https://github.com/avaloniaui"` |
| `Visibility.Collapsed` | `IsVisible="False"` |
| `TextBlock.TextWrapping` | `TextBlock.TextWrapping` (same) |
| `InputBinding` | `KeyBinding` or handled in code |
| `SystemParameters.VirtualScreen*` | Custom screen detection |
| `WindowStyle="SingleBorderWindow"` | Platform default |

**MainWindow.axaml structure:**
```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="using:TerminalHost.ViewModels"
        x:Class="TerminalHost.MainWindow"
        Title="TerminalHost"
        Width="1200" Height="800"
        MinWidth="800" MinHeight="600">

    <Window.KeyBindings>
        <KeyBinding Gesture="Ctrl+N" Command="{Binding OpenNewProjectCommand}"/>
        <KeyBinding Gesture="Ctrl+W" Command="{Binding CloseTabCommand}"/>
        <!-- More shortcuts -->
    </Window.KeyBindings>

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <!-- Tab strip -->
        <views:TabStrip Grid.Row="0" DataContext="{Binding}"/>

        <!-- Content area -->
        <ContentControl Grid.Row="1" Content="{Binding SelectedTab}"/>
    </Grid>
</Window>
```

### 5.3 Remove P/Invoke from App.xaml.cs

**DELETE lines 412-426:**
```csharp
private static class NativeMethods
{
    [DllImport("user32.dll")]
    internal static extern IntPtr SetFocus(IntPtr hWnd);
    // ... all P/Invoke declarations
}
```

**DELETE Popup Focus Fix region (lines 252-410):**
The entire `#region Popup Focus Fix` is Windows-specific WPF workaround. Avalonia handles popups differently.

### 5.4 Resources Migration

**Current App.xaml Resources (40+ colors, 8 button styles):**

Create separate resource files:

**Resources/Colors.axaml:**
```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui">
    <!-- Background colors -->
    <Color x:Key="BackgroundColor">#1E1E1E</Color>
    <Color x:Key="SidebarBackground">#252526</Color>
    <Color x:Key="TabBackground">#2D2D2D</Color>
    <!-- ... all 40+ colors -->
</ResourceDictionary>
```

**Resources/Converters.axaml:**
```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:conv="using:TerminalHost.Converters">
    <conv:BoolToVisibilityConverter x:Key="BoolToVisibility"/>
    <!-- ... all converters -->
</ResourceDictionary>
```

### 5.5 Converters.cs Updates

Most converters work with Avalonia. Key changes:

```csharp
// WPF:
public object Convert(...) => boolValue ? Visibility.Visible : Visibility.Collapsed;

// Avalonia:
public object Convert(...) => boolValue; // Bind to IsVisible directly
```

## Verification Checklist
- [ ] Application launches on macOS
- [ ] Main window displays
- [ ] Theme/colors apply correctly
- [ ] Keyboard shortcuts work
- [ ] DI services resolve

---

# STAGE 6: ViewModels Platform Independence

## Objective
Remove all Windows-specific code from ViewModels, using injected services.

## Deliverables
1. Platform-independent ViewModels
2. Updated DI dependencies
3. Timer abstraction usage

## Detailed File Changes

### 6.1 MainViewModel.cs (21 Changes)

**Lines 609-621 - FolderBrowserDialog:**
```csharp
// BEFORE (Windows):
var dialog = new FolderBrowserDialog { ... };
if (dialog.ShowDialog() == DialogResult.OK)
    path = dialog.SelectedPath;

// AFTER (Service):
private readonly IFolderPickerService _folderPicker;

var path = await _folderPicker.PickFolderAsync(
    "Select Project Directory",
    initialDirectory);
if (!string.IsNullOrEmpty(path))
    OpenProjectTab(path);
```

**Lines 215-240 - DispatcherTimer:**
```csharp
// BEFORE (WPF):
_gitStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
_gitStatusTimer.Tick += async (_, _) => await RefreshSelectedTabGitStatusAsync();
_gitStatusTimer.Start();

// AFTER (Service):
private readonly ITimerService _timerService;
private ITimer _gitStatusTimer;

_gitStatusTimer = _timerService.CreateTimer(
    TimeSpan.FromSeconds(5),
    async () => await RefreshSelectedTabGitStatusAsync());
_gitStatusTimer.Start();
```

**Lines 1332-1341 - OpenInExplorer:**
```csharp
// BEFORE:
Process.Start(new ProcessStartInfo
{
    FileName = "explorer.exe",
    Arguments = $"\"{path}\"",
    UseShellExecute = true
});

// AFTER:
_processService.OpenFolder(path);  // Uses 'open' on macOS
```

### 6.2 FileViewerViewModel.cs (3 Changes)

**Line 192 - OpenFileDialog:**
```csharp
// BEFORE:
var dialog = new Microsoft.Win32.OpenFileDialog { ... };
if (dialog.ShowDialog() == true)
    Open(dialog.FileName, mode);

// AFTER:
var path = await _filePickerService.PickFileAsync("Select File", filter);
if (!string.IsNullOrEmpty(path))
    Open(path, mode);
```

**Lines 197-199 - Application.Current.Resources:**
```csharp
// BEFORE:
FontFamily = Application.Current?.Resources["FontFamilyMonospace"] as FontFamily;

// AFTER:
FontFamily = _themeService.GetMonospaceFont();
```

### 6.3 GitFilesViewModel.cs (2 Changes)

**Lines 180-197 - ExploreFile:**
```csharp
// BEFORE:
Process.Start(new ProcessStartInfo
{
    FileName = "explorer.exe",
    Arguments = $"/select, \"{filePath}\"",
    UseShellExecute = true
});

// AFTER:
_processService.RevealInFinder(filePath);
```

### 6.4 TerminalPairTabViewModel.cs (2 Changes)

**Lines 762-776 - GetFocusedSession:**
```csharp
// BEFORE:
if (Pair.RunTerminal != null && Pair.RunTerminal.HasWin32Focus())
    return Pair.RunTerminal;

// AFTER:
if (Pair.RunTerminal != null && Pair.RunTerminal.HasFocus())
    return Pair.RunTerminal;
```

### 6.5 SetupViewModel.cs (1 Change)

**Lines 140-146 - Font enumeration:**
```csharp
// BEFORE:
var fonts = System.Windows.Media.Fonts.SystemFontFamilies;

// AFTER:
var fonts = _systemInfoService.GetInstalledFontFamilies();
```

### 6.6 Complete ViewModel Change List

| ViewModel | Windows Code | Fix |
|-----------|--------------|-----|
| MainViewModel | FolderBrowserDialog | IFolderPickerService |
| MainViewModel | DispatcherTimer (x4) | ITimerService |
| MainViewModel | Process.Start(explorer) | IProcessService |
| FileViewerViewModel | OpenFileDialog | IFilePickerService |
| FileViewerViewModel | Application.Resources | IThemeService |
| FilePreviewViewModel | OpenFileDialog | IFilePickerService |
| GitFilesViewModel | Process.Start(explorer /select) | IProcessService |
| TerminalPairTabViewModel | HasWin32Focus() | HasFocus() |
| FileExplorerViewModel | Application.Dispatcher | IDispatcherService |
| ScratchPadViewModel | DispatcherTimer | ITimerService |
| MarkdownPreviewViewModel | Application.Dispatcher | IDispatcherService |
| SetupViewModel | Fonts.SystemFontFamilies | ISystemInfoService |

## Verification Checklist
- [ ] All ViewModels compile without WPF references
- [ ] Unit tests pass
- [ ] No Application.Current references remain
- [ ] No DispatcherTimer references remain

---

# STAGE 7: Views & Controls Migration

## Objective
Migrate all 44 XAML views from WPF to Avalonia.

## Deliverables
1. All views converted to .axaml
2. Custom controls migrated
3. Data templates updated

## File-by-File Migration Guide

### 7.1 Priority Order

**Phase 7A: Critical Views (First)**
1. `MainWindow.axaml` (done in Stage 5)
2. `Views/TabStrip.axaml` - Tab bar
3. `Views/Tabs/TerminalPairView.axaml` - Main terminal UI
4. `Resources/TabContentTemplates.axaml` - Tab content selection

**Phase 7B: Secondary Views**
5. `Views/Tabs/ProfileTerminalView.axaml`
6. `Views/SettingsView.axaml` (largest file - 30K+ tokens)
7. `Views/FileExplorerView.axaml`
8. `Views/FileViewerView.axaml`

**Phase 7C: Popups**
9. All files in `Views/Popups/`

**Phase 7D: Controls**
10. All files in `Controls/`

**Phase 7E: Remaining Views**
11. All other view files

### 7.2 Common XAML Conversions

**Namespace Changes:**
```xml
<!-- WPF -->
xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"

<!-- Avalonia -->
xmlns="https://github.com/avaloniaui"
```

**Visibility:**
```xml
<!-- WPF -->
<Border Visibility="{Binding IsVisible, Converter={StaticResource BoolToVisibility}}"/>

<!-- Avalonia -->
<Border IsVisible="{Binding IsVisible}"/>
```

**Triggers → Styles:**
```xml
<!-- WPF -->
<Style.Triggers>
    <Trigger Property="IsMouseOver" Value="True">
        <Setter Property="Background" Value="Red"/>
    </Trigger>
</Style.Triggers>

<!-- Avalonia -->
<Style Selector="Button:pointerover">
    <Setter Property="Background" Value="Red"/>
</Style>
```

**DataTrigger → Classes:**
```xml
<!-- WPF -->
<DataTrigger Binding="{Binding IsActive}" Value="True">
    <Setter Property="Opacity" Value="1"/>
</DataTrigger>

<!-- Avalonia -->
<Style Selector="Border.active">
    <Setter Property="Opacity" Value="1"/>
</Style>
<!-- Then use Classes.active binding -->
```

**InputBindings:**
```xml
<!-- WPF -->
<Window.InputBindings>
    <KeyBinding Key="N" Modifiers="Control" Command="{Binding NewCommand}"/>
</Window.InputBindings>

<!-- Avalonia -->
<Window.KeyBindings>
    <KeyBinding Gesture="Ctrl+N" Command="{Binding NewCommand}"/>
</Window.KeyBindings>
```

### 7.3 Controls to Reimplement

**Controls/DraggablePopup.xaml:**
- WPF Popup with custom drag/resize
- Avalonia alternative: Use Window with custom chrome or Avalonia.Controls.PopupFlyout

**Controls/DiffViewer.xaml:**
- Uses FlowDocument (WPF-only)
- Replace with Avalonia TextBlock or AvaloniaEdit

**Controls/MarkdownViewer.xaml:**
- Uses WebView2 (Windows-only)
- Replace with AvaloniaWebView or custom markdown renderer

### 7.4 Views Code-Behind Changes

Many `.xaml.cs` files have Windows-specific code:

**SettingsView.xaml.cs (Lines 100-140):**
```csharp
// BEFORE:
var dialog = new Microsoft.Win32.OpenFileDialog { ... };

// AFTER:
var topLevel = TopLevel.GetTopLevel(this);
var files = await topLevel.StorageProvider.OpenFilePickerAsync(...);
```

**ToastWindow.xaml.cs (Lines 153-172):**
- DELETE all P/Invoke for window styles
- Avalonia windows don't need these hacks

### 7.5 DataTemplates Migration

**TabContentTemplates.xaml → TabContentTemplates.axaml:**
```xml
<!-- WPF -->
<DataTemplate DataType="{x:Type vm:TerminalPairTabViewModel}">
    <views:TerminalPairView/>
</DataTemplate>

<!-- Avalonia -->
<DataTemplate DataType="vm:TerminalPairTabViewModel">
    <views:TerminalPairView/>
</DataTemplate>
```

### 7.6 Views Not Requiring Migration

These views have minimal platform-specific code:
- Most popup content views
- Simple display views

## Verification Checklist
- [ ] All .xaml files converted to .axaml
- [ ] Application builds without XAML errors
- [ ] All views display correctly
- [ ] All data bindings work
- [ ] All styles apply correctly

---

# STAGE 8: Testing & Polish

## Objective
Ensure quality through testing and address platform-specific polish.

## Deliverables
1. Migrated unit tests
2. New macOS UI tests
3. Performance optimization
4. macOS-specific polish

## Detailed Tasks

### 8.1 Unit Tests Migration

**tests/TerminalHost.Tests:**
- Change target framework to `net8.0` (remove `-windows`)
- All 44 unit tests should pass without changes
- May need to update mock setups for new service interfaces

### 8.2 UI Tests (New Project)

**CREATE: tests/TerminalHost.MacTests/**

Options for macOS UI testing:
1. **Appium** - Cross-platform automation
2. **XCTest** - Native macOS testing
3. **Avalonia.Headless** - Headless UI testing

```csharp
// Example using Avalonia.Headless
[Fact]
public void MainWindow_Opens_Successfully()
{
    using var app = new TestApp();
    var window = app.MainWindow;
    window.Should().NotBeNull();
}
```

### 8.3 macOS-Specific Polish

**Menu Bar Integration:**
```csharp
// Add native macOS menu bar
NativeMenu.SetMenu(window, CreateMacMenu());
```

**Touch Bar Support (optional):**
- Consider adding Touch Bar buttons for common actions

**Dock Icon:**
- Implement dock icon badge for notifications
- Add dock menu items

**Trackpad Gestures:**
- Two-finger scroll in terminal
- Pinch to zoom (optional)

### 8.4 Font Fallback Updates

**Current fonts (Windows):**
- Cascadia Code NF
- Segoe UI

**macOS equivalents:**
- SF Mono (system monospace)
- SF Pro (system UI)
- Menlo (fallback monospace)

```csharp
public static FontFamily GetMonospaceFont()
{
    return new FontFamily("SF Mono, Menlo, Monaco, Consolas, monospace");
}

public static FontFamily GetUiFont()
{
    return new FontFamily("-apple-system, SF Pro, Helvetica Neue, sans-serif");
}
```

### 8.5 Path Conventions

Update all hardcoded paths:

| Windows | macOS |
|---------|-------|
| `%APPDATA%\TerminalHost` | `~/Library/Application Support/TerminalHost` |
| `%USERPROFILE%` | `~` |
| `%TEMP%` | `/tmp` or `$TMPDIR` |
| `explorer.exe` | `open` |
| `pwsh.exe` / `cmd.exe` | `/bin/zsh` / `/bin/bash` |

### 8.6 Performance Testing

Verify acceptable performance for:
- Terminal rendering speed
- Large file preview
- Git operations on large repos
- Memory usage with multiple tabs

## Verification Checklist
- [ ] All unit tests pass
- [ ] UI tests pass (new framework)
- [ ] Performance is acceptable
- [ ] macOS UI conventions followed
- [ ] App bundle is signed
- [ ] Notarization complete (for distribution)

---

# Appendix A: Complete File Inventory

## Files to DELETE

| File | Reason |
|------|--------|
| `Services/DarkModeHelper.cs` | Windows-only dwmapi.dll P/Invoke |
| `tests/TerminalHost.UITests/*` | FlaUI is Windows-only |
| `Resources/app.ico` | Windows icon format |
| All `.xaml` files | Replaced by `.axaml` |

## Files to CREATE

| File | Purpose |
|------|---------|
| `Services/IFolderPickerService.cs` | Folder dialog abstraction |
| `Services/FolderPickerService.cs` | Avalonia implementation |
| `Services/IFilePickerService.cs` | File dialog abstraction |
| `Services/FilePickerService.cs` | Avalonia implementation |
| `Services/IDispatcherService.cs` | Threading abstraction |
| `Services/DispatcherService.cs` | Avalonia implementation |
| `Services/ITimerService.cs` | Timer abstraction |
| `Services/TimerService.cs` | Avalonia implementation |
| `Services/IClipboardService.cs` | Clipboard abstraction |
| `Services/ClipboardService.cs` | Avalonia implementation |
| `Services/ISystemInfoService.cs` | System info abstraction |
| `Services/SystemInfoService.cs` | macOS implementation |
| `Services/IScreenService.cs` | **NEW** - Screen/monitor abstraction |
| `Services/ScreenService.cs` | **NEW** - Avalonia implementation |
| `Domain/ITerminalControl.cs` | Terminal control interface |
| `Controls/MacTerminalControl.cs` | macOS terminal control |
| `Resources/app.icns` | macOS icon |
| `Info.plist` | macOS app metadata |
| `Entitlements.plist` | macOS permissions |
| `GlobalUsings.cs` | Avalonia global usings |
| All `.axaml` files | Avalonia views |
| `tests/TerminalHost.MacTests/*` | macOS UI tests |

## Files to HEAVILY MODIFY

| File | Changes |
|------|---------|
| `TerminalHost.csproj` | Target framework, packages |
| `AssemblyInfo.cs` | **GAP FIX** - Remove WPF ThemeInfo attribute |
| `App.xaml.cs` → `App.axaml.cs` | Remove P/Invoke (4 DllImports), popup focus region, Avalonia startup |
| `MainWindow.xaml.cs` | Remove SystemParameters, VisualTreeHelper, Avalonia APIs |
| `Domain/TerminalSession.cs` | Remove all P/Invoke (5 DllImports) |
| `Domain/FileSystemNode.cs` | **GAP FIX** - Remove WPF Brush/Color types |
| `Domain/AppConfiguration.cs` | **NEW** - Default shell detection for macOS |
| `Services/TerminalControlFactory.cs` | Complete rewrite for Pty.Net |
| `Services/SystemTrayService.cs` | Complete rewrite (stub for macOS) |
| `Services/DialogService.cs` | Avalonia dialogs |
| `Services/ProcessService.cs` | macOS commands (open, open -R) |
| `Services/ConfigurationService.cs` | macOS paths, shell detection |
| `Services/LinkDetectionService.cs` | **GAP FIX** - Replace explorer.exe with IProcessService |
| `Services/ToastService.cs` | Avalonia notifications, remove DispatcherTimer |
| `Services/GitHubService.cs` | **NEW** - Replace cmd.exe with /bin/sh |
| `Services/TestRunnerService.cs` | **NEW** - Replace cmd.exe with direct dotnet |
| `Services/JsonSyntaxHighlighter.cs` | **NEW** - Replace FlowDocument with TextBlock |
| `Services/FilePreviewService.cs` | **NEW** - Replace FlowDocument return type |
| `Services/SyntaxHighlighting/*.cs` | **NEW** - Replace FlowDocument with Avalonia |
| `ViewModels/MainViewModel.cs` | Service dependencies, timer service |
| `ViewModels/TerminalPairTabViewModel.cs` | HasFocus, GridLength removal |
| `ViewModels/DashboardTabViewModel.cs` | **NEW** - DispatcherTimer replacement |
| `ViewModels/FilePreviewViewModel.cs` | **NEW** - OpenFileDialog, FontFamily |
| `ViewModels/FileExplorerViewModel.cs` | **GAP FIX** - IClipboardService for copy path |
| `ViewModels/SetupViewModel.cs` | **GAP FIX** - Replace PowerShell with /bin/sh |
| `ViewModels/ProfileTerminalTabViewModel.cs` | **NEW** - EasyWindowsTerminalControl removal |
| `ViewModels/SettingsTabViewModel.cs` | **NEW** - Default shell references |
| `ViewModels/ProfilesTabViewModel.cs` | **NEW** - Default shell references |
| `Views/ToastWindow.xaml.cs` | **NEW** - Remove P/Invoke (3 DllImports), Screen.FromHandle |
| `Views/SettingsView.xaml.cs` | **NEW** - OpenFileDialog, explorer.exe |
| `Views/SetupWindow.xaml.cs` | **NEW** - Clipboard, DispatcherTimer |
| `Views/TabStrip.xaml.cs` | **NEW** - DispatcherPriority, SystemParameters, **GAP FIX** DnD APIs |
| `Views/Popups/HelpView.xaml` | **NEW** - Update %APPDATA% path text |
| `Controls/DraggablePopup.xaml.cs` | **NEW** - Screen.FromHandle, SystemParameters, DependencyProperty |
| `Controls/MarkdownViewer.xaml.cs` | **NEW** - WebView2 replacement |
| `Controls/DiffViewer.xaml.cs` | **NEW** - FlowDocument replacement |
| `Converters.cs` | Avalonia converter signatures |

---

# Appendix B: Risk Assessment

## High Risk Items

1. **Terminal Control Integration**
   - XtermSharp may have bugs or missing features
   - Fallback: AvalonStudio.TerminalEmulator or custom rendering

2. **Performance**
   - Avalonia terminal rendering may be slower than native
   - Mitigation: Profile early, optimize rendering path

3. **macOS-Specific Behaviors**
   - System tray (menu bar extras) not standard in Avalonia
   - Mitigation: Accept limited tray functionality or use native interop

4. **FlowDocument Replacement** (NEW - HIGH RISK)
   - Affects 8+ files for syntax highlighting and file preview
   - Multiple architectural decisions required
   - Mitigation: Use AvaloniaEdit for code, ItemsControl for diffs

## Medium Risk Items

5. **XAML Migration**
   - Large settings view may have edge cases
   - Mitigation: Incremental migration, thorough testing

6. **WebView Migration**
   - Markdown preview uses WebView2
   - Mitigation: Use Markdown.Avalonia or AvaloniaWebView

7. **P/Invoke Removal** (NEW)
   - App.xaml.cs popup focus logic (4 P/Invoke)
   - ToastWindow.xaml.cs click-through logic (3 P/Invoke)
   - Mitigation: Avalonia handles popups/transparency natively

8. **Drag-and-Drop API Migration** (GAP FIX)
   - TabStrip.xaml.cs uses WPF DnD types (DataObject, DragDrop, DragDropEffects)
   - Avalonia DnD is async and uses different API signatures
   - Mitigation: Well-documented Avalonia DnD API, similar concepts

## Low Risk Items

9. **Unit Tests**
   - Should migrate cleanly

10. **Service Layer**
    - Well-abstracted, macOS implementations straightforward

11. **Git Operations**
    - Git CLI works identically on macOS

---

# Appendix C: Dependencies Summary

## NuGet Packages to REMOVE
- `EasyWindowsTerminalControl`
- `LiveChartsCore.SkiaSharpView.WPF`
- `Microsoft.Web.WebView2`
- `ci.microsoft.windows.console.conpty`

## NuGet Packages to ADD
- `Avalonia` (11.2.x)
- `Avalonia.Desktop`
- `Avalonia.Themes.Fluent`
- `LiveChartsCore.SkiaSharpView.Avalonia`
- `Pty.Net`
- `AvaloniaEdit` (for syntax highlighting) **NEW**
- `Markdown.Avalonia` (for markdown preview) **NEW**
- XtermSharp (submodule or package)

## External Dependencies
- Xcode Command Line Tools (for building on Mac)
- .NET 8 SDK for macOS
- Optional: Apple Developer account for signing/notarization

---

# Appendix D: P/Invoke Complete Inventory (NEW)

## All P/Invoke Declarations to Remove

| File | Line | DllImport | Purpose |
|------|------|-----------|---------|
| `App.xaml.cs` | 414 | `user32.dll` SetFocus | Popup focus handling |
| `App.xaml.cs` | 417 | `user32.dll` GetFocus | Popup focus handling |
| `App.xaml.cs` | 420 | `user32.dll` SetActiveWindow | Popup focus handling |
| `App.xaml.cs` | 423 | `user32.dll` SetForegroundWindow | Popup focus handling |
| `Domain/TerminalSession.cs` | 461 | `user32.dll` GetFocus | Terminal focus detection |
| `Domain/TerminalSession.cs` | 464 | `user32.dll` GetParent | Window hierarchy |
| `Domain/TerminalSession.cs` | 467 | `user32.dll` WindowFromPoint | Mouse position |
| `Domain/TerminalSession.cs` | 470 | `user32.dll` GetCursorPos | Cursor tracking |
| `Domain/TerminalSession.cs` | 559 | `user32.dll` GetWindowRect | Window bounds |
| `Services/DarkModeHelper.cs` | 12 | `dwmapi.dll` DwmSetWindowAttribute | Dark title bar |
| `Views/ToastWindow.xaml.cs` | 163 | `user32.dll` GetWindowLong | Window style |
| `Views/ToastWindow.xaml.cs` | 166 | `user32.dll` SetWindowLong | Click-through |
| `Views/ToastWindow.xaml.cs` | 169 | `user32.dll` SetWindowPos | Window positioning |

**Total: 13 P/Invoke declarations across 4 files**

---

# Appendix E: Shell Command Replacements (NEW)

## Windows → macOS Command Mapping

| Windows Command | macOS Equivalent | Files Affected |
|-----------------|------------------|----------------|
| `explorer.exe` | `open` | MainViewModel, GitFilesViewModel, FileExplorerViewModel, SettingsView, ProfileTerminalView |
| `explorer.exe /select,` | `open -R` | GitFilesViewModel, FileExplorerViewModel |
| `pwsh.exe` | `/bin/zsh` or `$SHELL` | AppConfiguration, SettingsTabViewModel, ProfilesTabViewModel |
| `cmd.exe /c` | `/bin/sh -c` | GitHubService, TestRunnerService, TerminalControlFactory |
| `%APPDATA%\TerminalHost` | `~/Library/Application Support/TerminalHost` | ConfigurationService, StatisticsService, HelpView.xaml |
| `%USERPROFILE%` | `~` or `$HOME` | Various path handling |

---

# Appendix F: FlowDocument Replacement Inventory (NEW)

## Files Using FlowDocument

| File | Usage | Replacement |
|------|-------|-------------|
| `Services/JsonSyntaxHighlighter.cs` | Syntax-highlighted JSON | TextBlock with Inlines |
| `Services/FilePreviewService.cs` | File preview result | String + HighlightedLine records |
| `Services/SyntaxHighlighting/SyntaxHighlighterBase.cs` | Base highlighter | AvaloniaEdit SelectionColorizer |
| `Services/SyntaxHighlighting/DiffHighlighter.cs` | Diff rendering | ItemsControl with line models |
| `ViewModels/FileViewerViewModel.cs` | Preview document property | String/Lines property |
| `ViewModels/FilePreviewViewModel.cs` | Content property | String/Lines property |
| `Controls/DiffViewer.xaml.cs` | Info/error documents | TextBlock |
| Multiple XAML files | FlowDocumentScrollViewer | ScrollViewer + ItemsControl |

**Total: 8+ files requiring FlowDocument replacement**

---
