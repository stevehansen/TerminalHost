# Stage 6: ViewModels Platform Independence

## Overview

| Attribute | Value |
|-----------|-------|
| **Estimated Effort** | 3-5 days |
| **Risk Level** | Medium |
| **Dependencies** | Stages 2, 3, 5 complete |
| **Blocking For** | Stages 7, 8 |

## Objective

Update all ViewModels to use the new platform-agnostic service abstractions, removing all direct Windows API usage.

## Success Criteria

- [ ] No WPF references in ViewModels
- [ ] All ViewModels use injected services
- [ ] DispatcherTimer replaced with ITimerService
- [ ] File dialogs use picker services
- [ ] Unit tests pass

---

## Deferred from Stage 5

The following items were deferred from Stage 5 and must be completed in this stage:

### 6.0.1 MainWindow Keyboard Shortcuts with ViewModels

**File:** `src/TerminalHost/TerminalHost/MainWindow.axaml.cs`

Stage 5 created a placeholder MainWindow with basic keyboard handling. Once MainViewModel is migrated, update MainWindow to use ViewModel commands:

```csharp
// After MainViewModel is migrated, update MainWindow constructor:
public MainWindow()
{
    InitializeComponent();

    // Get services from DI
    _viewModel = App.Current.Services.GetRequiredService<MainViewModel>();
    _configService = App.Current.Services.GetRequiredService<IConfigurationService>();

    DataContext = _viewModel;

    // Event handlers
    Opened += OnOpened;
    Closing += OnClosing;
}

// Update OnOpened to call ViewModel.Initialize()
private void OnOpened(object? sender, EventArgs e)
{
    _viewModel.Initialize();
}
```

### 6.0.2 Full Keyboard Shortcut Implementation

Once MainViewModel has all commands migrated, add proper keybindings to `MainWindow.axaml`:

```xml
<Window.KeyBindings>
    <!-- Tab Navigation -->
    <KeyBinding Gesture="Ctrl+PageDown" Command="{Binding CycleTabCommand}" CommandParameter="True"/>
    <KeyBinding Gesture="Ctrl+PageUp" Command="{Binding CycleTabCommand}" CommandParameter="False"/>
    <KeyBinding Gesture="Ctrl+W" Command="{Binding CloseTabCommand}" CommandParameter="{Binding SelectedTab}"/>

    <!-- Application -->
    <KeyBinding Gesture="Ctrl+N" Command="{Binding OpenNewProjectCommand}"/>
    <KeyBinding Gesture="Ctrl+OemComma" Command="{Binding OpenSettingsCommand}"/>
    <KeyBinding Gesture="Ctrl+P" Command="{Binding OpenProfilesCommand}"/>
    <KeyBinding Gesture="Ctrl+E" Command="{Binding OpenInExplorerCommand}"/>

    <!-- Terminal -->
    <KeyBinding Gesture="Ctrl+OemTilde" Command="{Binding SwitchActiveTerminalCommand}"/>
</Window.KeyBindings>
```

---

## ViewModel Changes Summary

| ViewModel | Changes Required |
|-----------|------------------|
| MainViewModel | FolderBrowserDialog, DispatcherTimer (x4), explorer.exe |
| TerminalPairTabViewModel | HasWin32Focus, GridLength properties |
| FileViewerViewModel | OpenFileDialog, Application.Resources |
| FilePreviewViewModel | OpenFileDialog, Application.Resources |
| GitFilesViewModel | explorer.exe /select |
| FileExplorerViewModel | Application.Dispatcher |
| ScratchPadViewModel | DispatcherTimer |
| MarkdownPreviewViewModel | Application.Dispatcher |
| SetupViewModel | Fonts.SystemFontFamilies |

---

## Detailed Changes

### 6.1 MainViewModel.cs

**File:** `src/TerminalHost/TerminalHost/ViewModels/MainViewModel.cs`

#### 6.1.1 Add New Dependencies

```csharp
private readonly IFolderPickerService _folderPickerService;
private readonly ITimerService _timerService;
private readonly IDispatcherService _dispatcherService;

// Replace DispatcherTimer fields with ITimer
private ITimer? _gitStatusTimer;
private ITimer? _activityTimer;
private ITimer? _linkDetectionTimer;
private ITimer? _runUrlDetectionTimer;
```

#### 6.1.2 Update Constructor

```csharp
public MainViewModel(
    IProfileRegistry profileRegistry,
    ISessionManager sessionManager,
    ITerminalControlFactory terminalFactory,
    IConfigurationService configService,
    // ... existing services ...
    IFolderPickerService folderPickerService,  // NEW
    ITimerService timerService,                 // NEW
    IDispatcherService dispatcherService)       // NEW
{
    _folderPickerService = folderPickerService;
    _timerService = timerService;
    _dispatcherService = dispatcherService;

    // Initialize timers using service
    _gitStatusTimer = _timerService.CreateTimer(
        TimeSpan.FromSeconds(5),
        async () => await RefreshSelectedTabGitStatusAsync());

    _activityTimer = _timerService.CreateTimer(
        TimeSpan.FromSeconds(1),
        RefreshActivityState);

    _linkDetectionTimer = _timerService.CreateTimer(
        TimeSpan.FromSeconds(3),
        RefreshDetectedLinks);

    _runUrlDetectionTimer = _timerService.CreateTimer(
        TimeSpan.FromSeconds(2),
        RefreshRunUrlDetection);
}
```

#### 6.1.3 Replace Folder Picker

**Before (lines 609-621):**
```csharp
var dialog = new FolderBrowserDialog
{
    Description = "Select Project Directory",
    ShowNewFolderButton = true,
    UseDescriptionForTitle = true
};

if (dialog.ShowDialog() == DialogResult.OK)
{
    OpenProjectTab(dialog.SelectedPath);
}
```

**After:**
```csharp
private async Task OpenNewProjectAsync()
{
    var path = await _folderPickerService.PickFolderAsync("Select Project Directory");
    if (!string.IsNullOrEmpty(path))
    {
        OpenProjectTab(path);
    }
}

// Update command to use async
[RelayCommand]
private async Task OpenNewProject() => await OpenNewProjectAsync();
```

#### 6.1.4 Replace Explorer.exe Call

**Before (lines 1332-1341):**
```csharp
Process.Start(new ProcessStartInfo
{
    FileName = "explorer.exe",
    Arguments = $"\"{path}\"",
    UseShellExecute = true
});
```

**After:**
```csharp
_processService.OpenFolder(path);
```

#### 6.1.5 Update Timer Start/Stop

```csharp
public void Initialize()
{
    // Load config and setup...

    // Start timers
    _gitStatusTimer?.Start();
    _activityTimer?.Start();
    _linkDetectionTimer?.Start();
    _runUrlDetectionTimer?.Start();
}

public void Shutdown()
{
    // Stop and dispose timers
    _gitStatusTimer?.Dispose();
    _activityTimer?.Dispose();
    _linkDetectionTimer?.Dispose();
    _runUrlDetectionTimer?.Dispose();

    // ... rest of shutdown
}
```

---

### 6.2 TerminalPairTabViewModel.cs

**File:** `src/TerminalHost/TerminalHost/ViewModels/TerminalPairTabViewModel.cs`

#### 6.2.1 Replace HasWin32Focus

**Before (lines 762-776):**
```csharp
public Domain.TerminalSession? GetFocusedSession()
{
    if (Pair.RunTerminal != null && IsRunTerminalVisible && Pair.RunTerminal.HasWin32Focus())
        return Pair.RunTerminal;
    if (Pair.ShellTerminal.HasWin32Focus())
        return Pair.ShellTerminal;
    if (Pair.CustomTerminal.HasWin32Focus())
        return Pair.CustomTerminal;

    return ActiveTerminal == TerminalType.Custom ? Pair.CustomTerminal : Pair.ShellTerminal;
}
```

**After:**
```csharp
public Domain.TerminalSession? GetFocusedSession()
{
    // Use the abstracted HasFocus method
    if (Pair.RunTerminal != null && IsRunTerminalVisible && Pair.RunTerminal.HasFocus())
        return Pair.RunTerminal;
    if (Pair.ShellTerminal.HasFocus())
        return Pair.ShellTerminal;
    if (Pair.CustomTerminal.HasFocus())
        return Pair.CustomTerminal;

    // Fallback to tracked property
    return ActiveTerminal == TerminalType.Custom ? Pair.CustomTerminal : Pair.ShellTerminal;
}
```

#### 6.2.2 Remove GridLength Properties

The GridLength computed properties are WPF-specific. In Avalonia, handle this in the View.

**Before:**
```csharp
public GridLength CustomColumnWidth => new GridLength(SplitRatio, GridUnitType.Star);
public GridLength ShellColumnWidth => new GridLength(1 - SplitRatio, GridUnitType.Star);
```

**After:**
```csharp
// These properties become simple doubles used by the View
public double CustomColumnRatio => SplitRatio;
public double ShellColumnRatio => 1 - SplitRatio;
```

#### 6.2.3 Update CopySelection Command

**Before:**
```csharp
private void CopySelection()
{
    var session = GetFocusedSession();
    session?.CopySelectionToClipboard();
}
```

**After:**
```csharp
private async Task CopySelectionAsync()
{
    var session = GetFocusedSession();
    if (session != null)
    {
        await session.CopySelectionToClipboardAsync();
    }
}
```

---

### 6.3 FileViewerViewModel.cs

**File:** `src/TerminalHost/TerminalHost/ViewModels/FileViewerViewModel.cs`

#### 6.3.1 Add Dependencies

```csharp
private readonly IFilePickerService _filePickerService;
```

#### 6.3.2 Replace OpenFileDialog

**Before (line 192):**
```csharp
var dialog = new Microsoft.Win32.OpenFileDialog
{
    Title = "Select File",
    Filter = "All Files (*.*)|*.*",
    InitialDirectory = initialDir
};

if (dialog.ShowDialog() == true)
{
    Open(dialog.FileName, mode);
}
```

**After:**
```csharp
var filters = new List<FilePickerFilter>
{
    new("All Files", "*")
};

var path = await _filePickerService.PickFileAsync("Select File", filters, initialDir);
if (!string.IsNullOrEmpty(path))
{
    Open(path, mode);
}
```

#### 6.3.3 Replace Application.Resources Access

**Before (lines 197-199):**
```csharp
FontFamily = Application.Current?.Resources["FontFamilyMonospace"] as FontFamily
    ?? new FontFamily("Consolas");
```

**After:**
```csharp
// Use a constant or inject via IThemeService
FontFamily = new FontFamily("SF Mono, Menlo, Monaco, monospace");

// Or add IThemeService for runtime font resolution
```

---

### 6.4 GitFilesViewModel.cs

**File:** `src/TerminalHost/TerminalHost/ViewModels/GitFilesViewModel.cs`

#### 6.4.1 Replace Explorer Call

**Before (lines 180-197):**
```csharp
Process.Start(new ProcessStartInfo
{
    FileName = "explorer.exe",
    Arguments = $"/select, \"{filePath}\"",
    UseShellExecute = true
});
```

**After:**
```csharp
_processService.RevealInFinder(filePath);
```

---

### 6.5 FileExplorerViewModel.cs

**File:** `src/TerminalHost/TerminalHost/ViewModels/FileExplorerViewModel.cs`

#### 6.5.1 Replace Application.Dispatcher

**Before (line 148):**
```csharp
var app = Application.Current;
if (app == null) return;
await app.Dispatcher.InvokeAsync(() =>
{
    // Update UI
});
```

**After:**
```csharp
await _dispatcherService.InvokeAsync(() =>
{
    // Update UI
});
```

#### 6.5.2 Replace Clipboard Usage (Gap Fix)

**File:** `src/TerminalHost/TerminalHost/ViewModels/FileExplorerViewModel.cs`

**Current (copy path to clipboard):**
```csharp
// Direct clipboard access
System.Windows.Clipboard.SetText(node.FullPath);
```

**After (use IClipboardService):**
```csharp
private readonly IClipboardService _clipboardService;

[RelayCommand]
private async Task CopyPathAsync()
{
    if (SelectedNode != null)
    {
        await _clipboardService.SetTextAsync(SelectedNode.FullPath);
        _toastService.Show("Path copied to clipboard", ToastType.Info);
    }
}
```

---

### 6.6 ScratchPadViewModel.cs

**File:** `src/TerminalHost/TerminalHost/ViewModels/ScratchPadViewModel.cs`

#### 6.6.1 Replace DispatcherTimer

**Before (line 124):**
```csharp
_saveDebounceTimer = new DispatcherTimer
{
    Interval = TimeSpan.FromMilliseconds(500)
};
_saveDebounceTimer.Tick += (_, _) =>
{
    _saveDebounceTimer.Stop();
    SaveNotes();
};
```

**After:**
```csharp
private readonly ITimerService _timerService;
private ITimer? _saveDebounceTimer;

// In constructor:
_saveDebounceTimer = _timerService.CreateTimer(
    TimeSpan.FromMilliseconds(500),
    () =>
    {
        _saveDebounceTimer?.Stop();
        SaveNotes();
    });
```

---

### 6.7 MarkdownPreviewViewModel.cs

**File:** `src/TerminalHost/TerminalHost/ViewModels/MarkdownPreviewViewModel.cs`

#### 6.7.1 Replace Dispatcher

**Before (line 130):**
```csharp
Application.Current.Dispatcher.InvokeAsync(async () =>
{
    await ReloadContentAsync();
});
```

**After:**
```csharp
_dispatcherService.Post(async () =>
{
    await ReloadContentAsync();
});
```

---

### 6.8 SetupViewModel.cs

**File:** `src/TerminalHost/TerminalHost/ViewModels/SetupViewModel.cs`

#### 6.8.1 Replace Font Enumeration

**Before (lines 140-146):**
```csharp
var fonts = System.Windows.Media.Fonts.SystemFontFamilies;
var nerdFonts = fonts.Where(f => f.Source.Contains("Nerd", StringComparison.OrdinalIgnoreCase));
```

**After:**
```csharp
private readonly ISystemInfoService _systemInfoService;

// In check method:
var fonts = _systemInfoService.GetInstalledFontFamilies();
var nerdFonts = fonts.Where(f => f.Contains("Nerd", StringComparison.OrdinalIgnoreCase));
```

#### 6.8.2 Replace PowerShell Execution (Gap Fix)

**File:** `src/TerminalHost/TerminalHost/ViewModels/SetupViewModel.cs`

**Current (around lines 89-95):**
```csharp
// Invokes PowerShell to check for installed tools
var psi = new ProcessStartInfo
{
    FileName = "powershell.exe",
    Arguments = "-NoProfile -Command \"...\""
};
```

**macOS Replacement:**
```csharp
// Use /bin/sh or execute tools directly
var psi = new ProcessStartInfo
{
    FileName = "/bin/sh",
    Arguments = "-c \"which git && git --version\""
};

// Or execute directly:
var psi = new ProcessStartInfo
{
    FileName = "git",
    Arguments = "--version"
};
```

**Tools to check on macOS:**
| Windows | macOS |
|---------|-------|
| `where git` | `which git` |
| `git --version` | `git --version` (same) |
| `dotnet --version` | `dotnet --version` (same) |
| `gh --version` | `gh --version` (same) |

#### 6.8.3 Note on DI

SetupViewModel is created with `new` in startup code. Either:
1. Move to DI
2. Pass services via constructor
3. Use service locator pattern (less preferred)

```csharp
// Option: Create factory or resolve from DI
public class SetupViewModel
{
    public SetupViewModel(ISystemInfoService? systemInfoService = null)
    {
        _systemInfoService = systemInfoService ?? new SystemInfoService();
    }
}
```

---

### 6.9 DashboardTabViewModel.cs (Gap Fix - MISSING)

**File:** `src/TerminalHost/TerminalHost/ViewModels/DashboardTabViewModel.cs`

This ViewModel was missing from the original migration plan but has DispatcherTimer usage.

#### 6.9.1 Replace DispatcherTimer

**Line 24:**
```csharp
// BEFORE:
private readonly DispatcherTimer _refreshTimer;

// AFTER:
private readonly ITimerService _timerService;
private ITimer? _refreshTimer;
```

**Line 124:**
```csharp
// BEFORE:
_refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
_refreshTimer.Tick += async (_, _) => await RefreshAsync();
_refreshTimer.Start();

// AFTER:
_refreshTimer = _timerService.CreateTimer(
    TimeSpan.FromMinutes(1),
    async () => await RefreshAsync());
_refreshTimer.Start();
```

---

### 6.10 FilePreviewViewModel.cs (Gap Fix - MISSING)

**File:** `src/TerminalHost/TerminalHost/ViewModels/FilePreviewViewModel.cs`

This ViewModel was listed but not fully documented.

#### 6.10.1 Add Dependencies

```csharp
private readonly IFilePickerService _filePickerService;
```

#### 6.10.2 Replace OpenFileDialog

**Line 152:**
```csharp
// BEFORE:
var dialog = new Microsoft.Win32.OpenFileDialog
{
    Title = "Select File to Preview",
    Filter = "All Files (*.*)|*.*",
    InitialDirectory = initialDir
};

if (dialog.ShowDialog() == true)
{
    LoadFile(dialog.FileName);
}

// AFTER:
var filters = new List<FilePickerFilter>
{
    new("All Files", "*")
};

var path = await _filePickerService.PickFileAsync("Select File to Preview", filters, initialDir);
if (!string.IsNullOrEmpty(path))
{
    LoadFile(path);
}
```

#### 6.10.3 Replace FontFamily Casting

**Lines 197, 210:**
```csharp
// BEFORE:
FontFamily = (System.Windows.Media.FontFamily)Application.Current?.Resources["FontFamilyMonospace"]
    ?? new System.Windows.Media.FontFamily("Consolas");

// AFTER:
FontFamily = new Avalonia.Media.FontFamily("SF Mono, Menlo, Monaco, Consolas, monospace");
```

---

### 6.11 ProfileTerminalTabViewModel.cs (Gap Fix - MISSING)

**File:** `src/TerminalHost/TerminalHost/ViewModels/ProfileTerminalTabViewModel.cs`

This ViewModel uses EasyWindowsTerminalControl.

#### 6.11.1 Remove EasyWindowsTerminalControl Reference

**Line 4:**
```csharp
// BEFORE:
using EasyWindowsTerminalControl;

// AFTER:
using TerminalHost.Domain; // For ITerminalControl
```

#### 6.11.2 Update Terminal Control Type

```csharp
// BEFORE:
public EasyTerminalControl? TerminalControl { get; set; }

// AFTER:
public object? TerminalControl { get; set; } // Native control from ITerminalControl
```

---

### 6.12 ToastViewModel.cs Updates

**File:** `src/TerminalHost/TerminalHost/ViewModels/ToastViewModel.cs`

If using DispatcherTimer for auto-close:

```csharp
// Use ITimerService instead of DispatcherTimer
private readonly ITimerService _timerService;
private ITimer? _autoCloseTimer;

public void StartAutoClose(TimeSpan delay)
{
    _autoCloseTimer = _timerService.CreateTimer(delay, () =>
    {
        _autoCloseTimer?.Stop();
        CloseCommand.Execute(null);
    });
    _autoCloseTimer.Start();
}
```

---

## Remove Using Statements

All ViewModels should have these WPF usings removed:

```csharp
// REMOVE:
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using EasyWindowsTerminalControl;
```

---

## Updated File Change Summary

| File | Changes |
|------|---------|
| `MainViewModel.cs` | Timer service, folder picker, dispatcher |
| `TerminalPairTabViewModel.cs` | HasFocus, GridLength, async clipboard |
| `FileViewerViewModel.cs` | File picker service, DispatcherTimer |
| `FilePreviewViewModel.cs` | **NEW** - File picker service, FontFamily |
| `GitFilesViewModel.cs` | RevealInFinder |
| `FileExplorerViewModel.cs` | Dispatcher service, Clipboard |
| `ScratchPadViewModel.cs` | Timer service |
| `MarkdownPreviewViewModel.cs` | Dispatcher service |
| `SetupViewModel.cs` | System info service, **NEW** - Shell execution replacement |
| `DashboardTabViewModel.cs` | **NEW** - Timer service |
| `ProfileTerminalTabViewModel.cs` | **NEW** - Remove EasyWindowsTerminalControl |
| `ToastViewModel.cs` | **NEW** - Timer service for auto-close |
| All other ViewModels | Remove WPF usings |

---

## DI Registration Updates

Ensure all ViewModels receive new dependencies in `App.axaml.cs`:

```csharp
// ViewModels now receive platform services
services.AddSingleton<MainViewModel>(sp => new MainViewModel(
    sp.GetRequiredService<IProfileRegistry>(),
    // ... existing ...
    sp.GetRequiredService<IFolderPickerService>(),
    sp.GetRequiredService<ITimerService>(),
    sp.GetRequiredService<IDispatcherService>()
));
```

---

## Verification Steps

### Build Check
```bash
dotnet build
```
All ViewModels should compile without WPF references.

### Unit Tests
```bash
dotnet test
```
Tests should pass with mocked services.

### Manual Verification
- Open folder picker works
- Timer-based updates work
- File preview/edit dialogs work
- Explorer integration uses Finder

---

## Next Stage

After completing Stage 6, proceed to **Stage 7: Views & Controls Migration** which converts all XAML views to Avalonia.
