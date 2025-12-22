# Stage 3: Domain Model Platform Independence

## Overview

| Attribute | Value |
|-----------|-------|
| **Estimated Effort** | 2-3 days |
| **Risk Level** | Medium |
| **Dependencies** | Stage 2 complete (service abstractions) |
| **Blocking For** | Stages 4, 6, 7 |

## Objective

Remove all P/Invoke (Win32 API calls) from Domain models and replace with platform-agnostic abstractions. The primary focus is `TerminalSession.cs` which contains significant Windows interop.

## Success Criteria

- [x] No `[DllImport]` attributes remain in Domain/
- [x] No `System.Windows.*` references in Domain/
- [x] TerminalSession uses IClipboardService
- [x] Focus detection uses abstracted interface
- [x] FileSystemNode uses platform-agnostic color representation (Gap Fix)
- [x] All domain models compile independently

---

## Detailed File Changes

### 3.1 TerminalSession.cs - Major Refactoring

**File:** `src/TerminalHost/TerminalHost/Domain/TerminalSession.cs`

This is the most significant change in this stage. The file currently has:
- P/Invoke for user32.dll (lines 461-563)
- WPF Clipboard usage (line 450)
- EasyWindowsTerminalControl dependency

#### 3.1.1 Remove P/Invoke Declarations

**DELETE lines 461-563:**
```csharp
// DELETE ALL OF THIS:
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

[StructLayout(LayoutKind.Sequential)]
private struct POINT { public int X; public int Y; }

[StructLayout(LayoutKind.Sequential)]
private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
```

#### 3.1.2 Remove GetScreenBounds Method

**DELETE lines 521-556:**
```csharp
// DELETE THIS ENTIRE METHOD:
private static System.Windows.Rect? GetScreenBounds(System.Windows.Media.Visual visual)
{
    // ... all Windows-specific screen bounds code
}
```

#### 3.1.3 Replace HasWin32Focus Method

**REPLACE lines 480-519:**

**Before:**
```csharp
public bool HasWin32Focus()
{
    try
    {
        if (_easyTerminalControl == null) return false;
        var focusedHwnd = GetFocus();
        // ... Windows-specific focus detection
    }
    catch { }
    return false;
}
```

**After:**
```csharp
/// <summary>
/// Checks if this terminal has focus.
/// Uses the terminal control's focus state.
/// </summary>
public bool HasFocus()
{
    try
    {
        return _terminalControl?.IsFocused ?? false;
    }
    catch
    {
        return false;
    }
}
```

#### 3.1.4 Add Clipboard Service Dependency

**MODIFY constructor (around line 72):**

**Before:**
```csharp
public TerminalSession(Profile profile, Services.IStatisticsService statisticsService, string terminalType)
{
    Id = Guid.NewGuid();
    // ...
}
```

**After:**
```csharp
private readonly IClipboardService _clipboardService;

public TerminalSession(
    Profile profile,
    Services.IStatisticsService statisticsService,
    IClipboardService clipboardService,
    string terminalType)
{
    Id = Guid.NewGuid();
    _clipboardService = clipboardService;
    // ... rest of initialization
}
```

#### 3.1.5 Replace Clipboard Usage

**REPLACE CopySelectionToClipboard method (lines 443-459):**

**Before:**
```csharp
public bool CopySelectionToClipboard()
{
    var text = GetSelectedText();
    if (!string.IsNullOrEmpty(text))
    {
        try
        {
            System.Windows.Clipboard.SetText(text);
            return true;
        }
        catch
        {
            return false;
        }
    }
    return false;
}
```

**After:**
```csharp
public async Task<bool> CopySelectionToClipboardAsync()
{
    var text = GetSelectedText();
    if (!string.IsNullOrEmpty(text))
    {
        try
        {
            await _clipboardService.SetTextAsync(text);
            return true;
        }
        catch
        {
            return false;
        }
    }
    return false;
}
```

#### 3.1.6 Create Terminal Control Abstraction

**MODIFY terminal control references:**

**Before (line 19):**
```csharp
private EasyTerminalControl? _easyTerminalControl;
```

**After:**
```csharp
private ITerminalControl? _terminalControl;
```

**REPLACE SetTerminalControl method (lines 83-109):**

**Before:**
```csharp
public void SetTerminalControl(EasyTerminalControl control)
{
    _easyTerminalControl = control;
    TerminalControl = control;

    control.Loaded += (s, e) =>
    {
        control.Dispatcher.InvokeAsync(() =>
        {
            if (control.ConPTYTerm != null)
            {
                control.ConPTYTerm.InterceptOutputToUITerminal = OnTerminalOutput;
            }
        }, System.Windows.Threading.DispatcherPriority.Background);
    };

    control.PreviewMouseDown += OnTerminalMouseDown;
}
```

**After:**
```csharp
public void SetTerminalControl(ITerminalControl control)
{
    _terminalControl = control;
    TerminalControl = control.NativeControl;

    control.Loaded += OnTerminalLoaded;
    control.OutputReceived += OnTerminalOutput;
    control.MouseClicked += OnTerminalMouseDown;
}

private void OnTerminalLoaded(object? sender, EventArgs e)
{
    // Terminal is ready for use
}

private void OnTerminalOutput(string output)
{
    // Increment character count for statistics
    _statisticsService.IncrementCharCount(_workingDirectory, _terminalType, output.Length);

    _lastOutputTime = DateTime.Now;

    if (!_wasActive)
    {
        _wasActive = true;
        ActivityChanged?.Invoke(this, EventArgs.Empty);
    }

    ParseOscSequences(output.AsSpan());
    AppendToOutputBuffer(output);
}
```

#### 3.1.7 Remove Using Statements

**DELETE these using statements at the top:**
```csharp
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using EasyWindowsTerminalControl;
```

**ADD:**
```csharp
using TerminalHost.Services;
```

---

### 3.2 Create ITerminalControl Interface

**CREATE:** `src/TerminalHost/TerminalHost/Domain/ITerminalControl.cs`

```csharp
namespace TerminalHost.Domain;

/// <summary>
/// Abstraction for terminal control implementations.
/// Allows different terminal backends (XtermSharp, native, etc.)
/// </summary>
public interface ITerminalControl
{
    /// <summary>
    /// The native control to embed in the UI.
    /// </summary>
    object NativeControl { get; }

    /// <summary>
    /// Whether the control currently has keyboard focus.
    /// </summary>
    bool IsFocused { get; }

    /// <summary>
    /// Write text to the terminal input.
    /// </summary>
    void WriteToTerminal(string text);

    /// <summary>
    /// Write a span of characters to the terminal input.
    /// </summary>
    void WriteToTerminal(ReadOnlySpan<char> text);

    /// <summary>
    /// Get the currently selected text.
    /// </summary>
    string GetSelectedText();

    /// <summary>
    /// Focus the terminal control.
    /// </summary>
    void Focus();

    /// <summary>
    /// Restart the terminal process.
    /// </summary>
    Task RestartAsync();

    /// <summary>
    /// Kill the terminal process.
    /// </summary>
    void Kill();

    /// <summary>
    /// Check if the terminal process is running.
    /// </summary>
    bool IsProcessRunning { get; }

    /// <summary>
    /// Get the terminal process exit code (if exited).
    /// </summary>
    int? ExitCode { get; }

    /// <summary>
    /// Fired when the control is loaded and ready.
    /// </summary>
    event EventHandler? Loaded;

    /// <summary>
    /// Fired when output is received from the terminal.
    /// </summary>
    event Action<string>? OutputReceived;

    /// <summary>
    /// Fired when a mouse click occurs.
    /// </summary>
    event EventHandler<TerminalMouseEventArgs>? MouseClicked;

    /// <summary>
    /// Fired when the terminal process exits.
    /// </summary>
    event EventHandler<int>? ProcessExited;
}

/// <summary>
/// Mouse event arguments for terminal control.
/// </summary>
public class TerminalMouseEventArgs : EventArgs
{
    public int X { get; init; }
    public int Y { get; init; }
    public bool IsLeftButton { get; init; }
    public bool IsRightButton { get; init; }
    public bool IsCtrlPressed { get; init; }
    public bool IsShiftPressed { get; init; }
}
```

---

### 3.3 Update TerminalPair.cs

**File:** `src/TerminalHost/TerminalHost/Domain/TerminalPair.cs`

This file creates TerminalSession instances. Update to pass the clipboard service.

**MODIFY CreateTerminal methods to accept IClipboardService:**

```csharp
public TerminalSession CreateCustomTerminal(
    Profile profile,
    IStatisticsService statisticsService,
    IClipboardService clipboardService)
{
    CustomTerminal = new TerminalSession(profile, statisticsService, clipboardService, "custom");
    return CustomTerminal;
}

public TerminalSession CreateShellTerminal(
    Profile profile,
    IStatisticsService statisticsService,
    IClipboardService clipboardService)
{
    ShellTerminal = new TerminalSession(profile, statisticsService, clipboardService, "shell");
    return ShellTerminal;
}

public TerminalSession CreateRunTerminal(
    Profile profile,
    IStatisticsService statisticsService,
    IClipboardService clipboardService)
{
    RunTerminal = new TerminalSession(profile, statisticsService, clipboardService, "run");
    return RunTerminal;
}
```

---

### 3.4 Refactored TerminalSession.cs (Complete)

Here's the complete refactored file structure:

```csharp
using System.Text;
using TerminalHost.Services;

namespace TerminalHost.Domain;

public class TerminalSession : IDisposable
{
    public Guid Id { get; }
    public Profile Profile { get; }
    public SessionState State { get; private set; }
    public int? ExitCode { get; private set; }
    public object? TerminalControl { get; set; }

    private ITerminalControl? _terminalControl;
    private readonly IClipboardService _clipboardService;
    private readonly IStatisticsService _statisticsService;
    private readonly string _workingDirectory;
    private readonly string _terminalType;

    // Activity tracking
    private DateTime? _lastOutputTime;
    private bool _wasActive;

    // Terminal title tracking
    private string _terminalTitle = string.Empty;
    private readonly StringBuilder _oscBuffer = new();
    private bool _parsingOsc;

    // Output buffer for link detection
    private readonly StringBuilder _outputBuffer = new();
    private const int MaxOutputBufferSize = 50000;

    public DateTime? LastOutputTime => _lastOutputTime;
    public bool IsActive => _lastOutputTime.HasValue &&
        (DateTime.Now - _lastOutputTime.Value).TotalSeconds < 2;

    public event EventHandler<int>? ProcessExited;
    public event EventHandler? ActivityChanged;
    public string TerminalTitle => _terminalTitle;
    public event EventHandler<string>? TitleChanged;
    public event EventHandler<string>? LinkClicked;

    public TerminalSession(
        Profile profile,
        IStatisticsService statisticsService,
        IClipboardService clipboardService,
        string terminalType)
    {
        Id = Guid.NewGuid();
        Profile = profile;
        State = SessionState.Running;
        _statisticsService = statisticsService;
        _clipboardService = clipboardService;
        _workingDirectory = profile.WorkingDir;
        _terminalType = terminalType;
    }

    public void SetTerminalControl(ITerminalControl control)
    {
        _terminalControl = control;
        TerminalControl = control.NativeControl;

        control.Loaded += OnTerminalLoaded;
        control.OutputReceived += OnTerminalOutput;
        control.MouseClicked += OnTerminalMouseClicked;
        control.ProcessExited += OnProcessExited;
    }

    private void OnTerminalLoaded(object? sender, EventArgs e)
    {
        // Terminal is ready
    }

    private void OnTerminalOutput(string output)
    {
        _statisticsService.IncrementCharCount(_workingDirectory, _terminalType, output.Length);
        _lastOutputTime = DateTime.Now;

        if (!_wasActive)
        {
            _wasActive = true;
            ActivityChanged?.Invoke(this, EventArgs.Empty);
        }

        ParseOscSequences(output.AsSpan());
        AppendToOutputBuffer(output);
    }

    private void OnTerminalMouseClicked(object? sender, TerminalMouseEventArgs e)
    {
        if (e.IsLeftButton && e.IsCtrlPressed)
        {
            var clickedText = GetTextForLinkDetection();
            if (!string.IsNullOrEmpty(clickedText))
            {
                LinkClicked?.Invoke(this, clickedText);
            }
        }
    }

    private void OnProcessExited(object? sender, int exitCode)
    {
        State = SessionState.Exited;
        ExitCode = exitCode;
        ProcessExited?.Invoke(this, exitCode);
    }

    public bool HasFocus()
    {
        return _terminalControl?.IsFocused ?? false;
    }

    public string? GetTextForLinkDetection()
    {
        lock (_outputBuffer)
        {
            if (_outputBuffer.Length == 0)
                return null;

            var startIndex = Math.Max(0, _outputBuffer.Length - 1000);
            return _outputBuffer.ToString(startIndex, _outputBuffer.Length - startIndex);
        }
    }

    public string GetRecentOutput(int maxChars = 5000)
    {
        lock (_outputBuffer)
        {
            if (_outputBuffer.Length == 0)
                return string.Empty;

            var startIndex = Math.Max(0, _outputBuffer.Length - maxChars);
            return _outputBuffer.ToString(startIndex, _outputBuffer.Length - startIndex);
        }
    }

    private void AppendToOutputBuffer(string output)
    {
        lock (_outputBuffer)
        {
            _outputBuffer.Append(output);

            if (_outputBuffer.Length > MaxOutputBufferSize)
            {
                var keepFrom = _outputBuffer.Length - (MaxOutputBufferSize / 2);
                var kept = _outputBuffer.ToString(keepFrom, _outputBuffer.Length - keepFrom);
                _outputBuffer.Clear();
                _outputBuffer.Append(kept);
            }
        }
    }

    private void ParseOscSequences(ReadOnlySpan<char> str)
    {
        for (int i = 0; i < str.Length; i++)
        {
            char c = str[i];

            if (_parsingOsc)
            {
                if (c == '\x07') // BEL
                {
                    ProcessOscSequence();
                    continue;
                }
                else if (c == '\x1b' && i + 1 < str.Length && str[i + 1] == '\\')
                {
                    ProcessOscSequence();
                    i++;
                    continue;
                }
                else
                {
                    _oscBuffer.Append(c);
                    if (_oscBuffer.Length > 1024)
                    {
                        _parsingOsc = false;
                        _oscBuffer.Clear();
                    }
                }
            }
            else if (c == '\x1b' && i + 1 < str.Length && str[i + 1] == ']')
            {
                _parsingOsc = true;
                _oscBuffer.Clear();
                i++;
            }
        }
    }

    private void ProcessOscSequence()
    {
        _parsingOsc = false;
        var content = _oscBuffer.ToString();
        _oscBuffer.Clear();

        var semicolonIndex = content.IndexOf(';');
        if (semicolonIndex > 0)
        {
            var paramStr = content[..semicolonIndex];
            if (int.TryParse(paramStr, out int param))
            {
                if (param == 0 || param == 2)
                {
                    var newTitle = content[(semicolonIndex + 1)..];
                    if (newTitle != _terminalTitle)
                    {
                        _terminalTitle = newTitle;
                        TitleChanged?.Invoke(this, newTitle);
                    }
                }
            }
        }
    }

    public void CheckActivityState()
    {
        var currentlyActive = IsActive;
        if (_wasActive && !currentlyActive)
        {
            _wasActive = false;
            ActivityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void MarkAsExited(int exitCode)
    {
        State = SessionState.Exited;
        ExitCode = exitCode;
        ProcessExited?.Invoke(this, exitCode);
    }

    public void Terminate()
    {
        try
        {
            if (State == SessionState.Running)
            {
                _terminalControl?.Kill();
            }
        }
        catch { }
        finally
        {
            State = SessionState.Exited;
        }
    }

    public bool IsProcessRunning()
    {
        if (State == SessionState.Exited)
            return false;

        return _terminalControl?.IsProcessRunning ?? false;
    }

    public void SendText(string text, bool appendNewline = true, string newlineChar = "\r", bool useUserInput = false)
    {
        try
        {
            if (_terminalControl == null || !IsProcessRunning())
                return;

            var textToSend = appendNewline ? text + newlineChar : text;

            if (useUserInput)
            {
                _terminalControl.Focus();
            }

            _terminalControl.WriteToTerminal(textToSend);
        }
        catch { }
    }

    public void Focus()
    {
        try
        {
            _terminalControl?.Focus();
        }
        catch { }
    }

    public string GetSelectedText()
    {
        try
        {
            return _terminalControl?.GetSelectedText() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public async Task<bool> CopySelectionToClipboardAsync()
    {
        var text = GetSelectedText();
        if (!string.IsNullOrEmpty(text))
        {
            try
            {
                await _clipboardService.SetTextAsync(text);
                return true;
            }
            catch
            {
                return false;
            }
        }
        return false;
    }

    public void Dispose()
    {
        Terminate();
    }
}
```

---

### 3.5 FileSystemNode.cs - WPF Media Types (Gap Fix)

**File:** `src/TerminalHost/TerminalHost/Domain/FileSystemNode.cs`

This file uses WPF-specific media types in the Domain layer, violating platform-agnostic domain design.

**Current Issue (lines using WPF types):**
```csharp
using System.Windows.Media;

// Properties using WPF types:
public Brush StatusBackground { get; }
public SolidColorBrush TextColor { get; }
public Color IconColor { get; }
```

**Solution: Remove WPF types from Domain**

The domain model should not contain UI-specific types. Two approaches:

**Option A: Return color strings (Recommended)**
```csharp
// Use hex strings or color names instead of WPF Brush types
public string StatusBackgroundHex { get; }  // e.g., "#FF0000"
public string TextColorHex { get; }          // e.g., "#CCCCCC"
public string IconColorHex { get; }          // e.g., "#569CD6"
```

**Option B: Use enums and convert in UI layer**
```csharp
public enum FileStatusColor { Normal, Modified, Added, Deleted, Untracked }

public FileStatusColor StatusColorType { get; }

// In View or ViewModel, convert to actual brush:
// Brush = StatusColorType switch { ... }
```

**Refactored FileSystemNode.cs:**
```csharp
namespace TerminalHost.Domain;

public class FileSystemNode
{
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public bool IsExpanded { get; set; }
    public ObservableCollection<FileSystemNode> Children { get; } = new();

    // Git status info
    public GitFileStatus? GitStatus { get; init; }

    // Color as hex string (UI layer converts to brushes)
    public string StatusBackgroundHex => GetStatusBackgroundHex();
    public string TextColorHex => GetTextColorHex();

    private string GetStatusBackgroundHex()
    {
        return GitStatus switch
        {
            null => "Transparent",
            { Status: "M" } => "#26264F78",  // Modified - blue tint
            { Status: "A" } => "#2613A10E",  // Added - green tint
            { Status: "D" } => "#26C50F1F",  // Deleted - red tint
            { Status: "?" } => "#26C19C00",  // Untracked - yellow tint
            _ => "Transparent"
        };
    }

    private string GetTextColorHex()
    {
        return GitStatus switch
        {
            null => "#CCCCCC",      // Normal
            { Status: "M" } => "#9CDCFE",  // Modified
            { Status: "A" } => "#4EC9B0",  // Added
            { Status: "D" } => "#808080",  // Deleted (grayed)
            { Status: "?" } => "#DCDCAA",  // Untracked
            _ => "#CCCCCC"
        };
    }
}
```

**View Layer Conversion:**

In the View or with a converter, convert hex strings to brushes:

```csharp
// Converter for Avalonia
public class HexToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrEmpty(hex))
        {
            if (hex == "Transparent")
                return Brushes.Transparent;

            return new SolidColorBrush(Color.Parse(hex));
        }
        return Brushes.Transparent;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
```

---

### 3.6 Domain Files Summary

| File | Action | Changes |
|------|--------|---------|
| `TerminalSession.cs` | **MAJOR REWRITE** | Remove P/Invoke, add service deps |
| `ITerminalControl.cs` | **CREATE** | New interface |
| `TerminalMouseEventArgs.cs` | **CREATE** | (in ITerminalControl.cs) |
| `TerminalPair.cs` | **MODIFY** | Pass clipboard service |
| `Profile.cs` | No change | Platform-agnostic |
| `AppConfiguration.cs` | No change | Platform-agnostic |
| `GitStatus.cs` | No change | Platform-agnostic |
| `GitFileStatus.cs` | No change | Platform-agnostic |
| `GitBranch.cs` | No change | Platform-agnostic |
| `QuickCommand.cs` | No change | Platform-agnostic |
| `LinkPattern.cs` | No change | Platform-agnostic |
| `PaletteCommand.cs` | No change | Platform-agnostic |
| `RunConfiguration.cs` | No change | Platform-agnostic |
| `ProjectType.cs` | No change | Platform-agnostic |
| `ClaudeCommand.cs` | No change | Platform-agnostic |
| `RunState.cs` | No change | Platform-agnostic |
| `SessionState.cs` | No change | Platform-agnostic |
| `FileSystemNode.cs` | **MODIFY** | **NEW** - Remove WPF media types |
| `FileIconMapper.cs` | No change | Platform-agnostic |

---

## Verification Steps

### Compilation Check
```bash
# After temporarily excluding ViewModels that depend on old TerminalSession
dotnet build
```

### Interface Verification
- ITerminalControl has all methods needed by TerminalSession
- No circular dependencies between Domain and Services

### Unit Test Updates
Any tests that directly test TerminalSession will need updates to mock IClipboardService.

---

## Breaking Changes

The following changes will require updates in dependent code:

1. **TerminalSession constructor** now requires `IClipboardService`
2. **CopySelectionToClipboard** is now async (`CopySelectionToClipboardAsync`)
3. **HasWin32Focus** renamed to `HasFocus`
4. **SetTerminalControl** parameter type changed to `ITerminalControl`

These will be addressed in Stages 4 (terminal control) and 6 (ViewModels).

---

## Completion Notes

**Stage 3 completed on 2025-12-22**

### Summary of Changes Made

1. **ITerminalControl.cs** - Created new interface at `Domain/ITerminalControl.cs`
   - Includes `TerminalMouseEventArgs` class

2. **TerminalSession.cs** - Major refactoring completed
   - Removed 5 P/Invoke declarations (GetFocus, GetParent, WindowFromPoint, GetCursorPos, GetWindowRect)
   - Removed GetScreenBounds method
   - Replaced `HasWin32Focus()` with `HasFocus()` using ITerminalControl.IsFocused
   - Added IClipboardService constructor parameter
   - Replaced sync `CopySelectionToClipboard()` with async `CopySelectionToClipboardAsync()`
   - Replaced `_easyTerminalControl` with `_terminalControl` (ITerminalControl)
   - Removed all Windows-specific using statements

3. **TerminalPair.cs** - Updated to pass IClipboardService
   - Added `_clipboardService` field
   - Updated constructor to accept IClipboardService
   - Updated `CreateRunTerminal` to pass clipboard service

4. **FileSystemNode.cs** - Removed WPF types
   - Removed `using System.Windows.Media`
   - Replaced `Brush? RowBackground` with `string? RowBackgroundHex`
   - Updated `FileExplorerView.xaml` to use `HexToBrushConverter`

### Additional Changes (to maintain build success)

The breaking changes documented above required updates to dependent code:

- **TerminalPairTabViewModel.cs**: Updated `HasWin32Focus()` → `HasFocus()`, `CopySelectionToClipboard()` → async
- **MainWindow.xaml.cs**: Added IClipboardService, updated clipboard copying to async
- **MainViewModel.cs**: Added IClipboardService, updated TerminalPair/TerminalSession creation
- **SessionManager.cs**: Added IClipboardService constructor parameter
- **ProfileTerminalTabViewModel.cs**: Added IClipboardService constructor parameter

### Build Status

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## Next Stage

After completing Stage 3, proceed to **Stage 4: Terminal Control Integration** which implements the ITerminalControl interface using XtermSharp and Pty.Net.
