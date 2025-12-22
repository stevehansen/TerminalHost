# Stage 2: Service Layer Abstractions

## Overview

| Attribute | Value |
|-----------|-------|
| **Estimated Effort** | 3-5 days |
| **Risk Level** | Low |
| **Dependencies** | Stage 1 complete |
| **Blocking For** | Stages 3, 4, 6 |

## Objective

Create platform-agnostic service interfaces and macOS implementations for all Windows-specific functionality. This enables the rest of the codebase to compile without Windows dependencies.

## Success Criteria

- [ ] All new service interfaces created
- [ ] macOS implementations for platform services
- [ ] DI registration updated
- [ ] Existing unit tests still pass
- [ ] No direct WPF/Windows.Forms references in service layer

---

## New Service Interfaces

### 2.1 IFolderPickerService

**CREATE:** `src/TerminalHost/TerminalHost/Services/IFolderPickerService.cs`

```csharp
namespace TerminalHost.Services;

/// <summary>
/// Abstraction for folder selection dialogs.
/// Replaces System.Windows.Forms.FolderBrowserDialog.
/// </summary>
public interface IFolderPickerService
{
    /// <summary>
    /// Opens a folder picker dialog.
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="initialDirectory">Starting directory (optional)</param>
    /// <returns>Selected folder path, or null if cancelled</returns>
    Task<string?> PickFolderAsync(string? title = null, string? initialDirectory = null);
}
```

**CREATE:** `src/TerminalHost/TerminalHost/Services/FolderPickerService.cs`

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace TerminalHost.Services;

internal sealed class FolderPickerService : IFolderPickerService
{
    public async Task<string?> PickFolderAsync(string? title = null, string? initialDirectory = null)
    {
        var topLevel = GetTopLevel();
        if (topLevel == null)
            return null;

        var options = new FolderPickerOpenOptions
        {
            Title = title ?? "Select Folder",
            AllowMultiple = false
        };

        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
        {
            options.SuggestedStartLocation = await topLevel.StorageProvider
                .TryGetFolderFromPathAsync(initialDirectory);
        }

        var result = await topLevel.StorageProvider.OpenFolderPickerAsync(options);

        return result.Count > 0 ? result[0].Path.LocalPath : null;
    }

    private static TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }
}
```

---

### 2.2 IFilePickerService

**CREATE:** `src/TerminalHost/TerminalHost/Services/IFilePickerService.cs`

```csharp
namespace TerminalHost.Services;

/// <summary>
/// Abstraction for file selection dialogs.
/// Replaces Microsoft.Win32.OpenFileDialog.
/// </summary>
public interface IFilePickerService
{
    /// <summary>
    /// Opens a file picker dialog.
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="filters">File type filters (e.g., "Text Files|*.txt")</param>
    /// <param name="initialDirectory">Starting directory (optional)</param>
    /// <param name="allowMultiple">Allow multiple file selection</param>
    /// <returns>Selected file path(s), or empty if cancelled</returns>
    Task<IReadOnlyList<string>> PickFilesAsync(
        string? title = null,
        IReadOnlyList<FilePickerFilter>? filters = null,
        string? initialDirectory = null,
        bool allowMultiple = false);

    /// <summary>
    /// Opens a single file picker dialog.
    /// </summary>
    Task<string?> PickFileAsync(
        string? title = null,
        IReadOnlyList<FilePickerFilter>? filters = null,
        string? initialDirectory = null);

    /// <summary>
    /// Opens a save file dialog.
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="defaultFileName">Default file name</param>
    /// <param name="filters">File type filters</param>
    /// <param name="initialDirectory">Starting directory (optional)</param>
    /// <returns>Selected save path, or null if cancelled</returns>
    Task<string?> PickSaveFileAsync(
        string? title = null,
        string? defaultFileName = null,
        IReadOnlyList<FilePickerFilter>? filters = null,
        string? initialDirectory = null);
}

/// <summary>
/// File picker filter definition.
/// </summary>
public record FilePickerFilter(string Name, params string[] Extensions);
```

**CREATE:** `src/TerminalHost/TerminalHost/Services/FilePickerService.cs`

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace TerminalHost.Services;

internal sealed class FilePickerService : IFilePickerService
{
    public async Task<IReadOnlyList<string>> PickFilesAsync(
        string? title = null,
        IReadOnlyList<FilePickerFilter>? filters = null,
        string? initialDirectory = null,
        bool allowMultiple = false)
    {
        var topLevel = GetTopLevel();
        if (topLevel == null)
            return Array.Empty<string>();

        var options = new FilePickerOpenOptions
        {
            Title = title ?? "Select File",
            AllowMultiple = allowMultiple,
            FileTypeFilter = ConvertFilters(filters)
        };

        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
        {
            options.SuggestedStartLocation = await topLevel.StorageProvider
                .TryGetFolderFromPathAsync(initialDirectory);
        }

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(options);

        return result.Select(f => f.Path.LocalPath).ToList();
    }

    public async Task<string?> PickFileAsync(
        string? title = null,
        IReadOnlyList<FilePickerFilter>? filters = null,
        string? initialDirectory = null)
    {
        var files = await PickFilesAsync(title, filters, initialDirectory, false);
        return files.Count > 0 ? files[0] : null;
    }

    public async Task<string?> PickSaveFileAsync(
        string? title = null,
        string? defaultFileName = null,
        IReadOnlyList<FilePickerFilter>? filters = null,
        string? initialDirectory = null)
    {
        var topLevel = GetTopLevel();
        if (topLevel == null)
            return null;

        var options = new FilePickerSaveOptions
        {
            Title = title ?? "Save File",
            SuggestedFileName = defaultFileName,
            FileTypeChoices = ConvertFilters(filters)
        };

        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
        {
            options.SuggestedStartLocation = await topLevel.StorageProvider
                .TryGetFolderFromPathAsync(initialDirectory);
        }

        var result = await topLevel.StorageProvider.SaveFilePickerAsync(options);

        return result?.Path.LocalPath;
    }

    private static IReadOnlyList<Avalonia.Platform.Storage.FilePickerFileType>? ConvertFilters(
        IReadOnlyList<FilePickerFilter>? filters)
    {
        if (filters == null || filters.Count == 0)
            return null;

        return filters.Select(f => new Avalonia.Platform.Storage.FilePickerFileType(f.Name)
        {
            Patterns = f.Extensions.Select(e => e.StartsWith("*.") ? e : $"*.{e}").ToList()
        }).ToList();
    }

    private static TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }
}
```

---

### 2.3 IDispatcherService

**CREATE:** `src/TerminalHost/TerminalHost/Services/IDispatcherService.cs`

```csharp
namespace TerminalHost.Services;

/// <summary>
/// Abstraction for UI thread dispatching.
/// Replaces WPF Dispatcher and Application.Current.Dispatcher.
/// </summary>
public interface IDispatcherService
{
    /// <summary>
    /// Posts an action to be executed on the UI thread.
    /// </summary>
    void Post(Action action);

    /// <summary>
    /// Invokes an action on the UI thread and waits for completion.
    /// </summary>
    Task InvokeAsync(Action action);

    /// <summary>
    /// Invokes a function on the UI thread and returns the result.
    /// </summary>
    Task<T> InvokeAsync<T>(Func<T> func);

    /// <summary>
    /// Checks if currently on the UI thread.
    /// </summary>
    bool CheckAccess();
}
```

**CREATE:** `src/TerminalHost/TerminalHost/Services/DispatcherService.cs`

```csharp
using Avalonia.Threading;

namespace TerminalHost.Services;

internal sealed class DispatcherService : IDispatcherService
{
    public void Post(Action action)
    {
        Dispatcher.UIThread.Post(action);
    }

    public Task InvokeAsync(Action action)
    {
        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }

    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        return Dispatcher.UIThread.InvokeAsync(func).GetTask();
    }

    public bool CheckAccess()
    {
        return Dispatcher.UIThread.CheckAccess();
    }
}
```

---

### 2.4 ITimerService

**CREATE:** `src/TerminalHost/TerminalHost/Services/ITimerService.cs`

```csharp
namespace TerminalHost.Services;

/// <summary>
/// Abstraction for timer functionality.
/// Replaces WPF DispatcherTimer.
/// </summary>
public interface ITimerService
{
    /// <summary>
    /// Creates a new timer that executes on the UI thread.
    /// </summary>
    /// <param name="interval">Timer interval</param>
    /// <param name="callback">Callback to execute on each tick</param>
    /// <returns>A controllable timer instance</returns>
    ITimer CreateTimer(TimeSpan interval, Action callback);

    /// <summary>
    /// Creates a new async timer.
    /// </summary>
    ITimer CreateTimer(TimeSpan interval, Func<Task> asyncCallback);
}

/// <summary>
/// A controllable timer instance.
/// </summary>
public interface ITimer : IDisposable
{
    /// <summary>
    /// Starts the timer.
    /// </summary>
    void Start();

    /// <summary>
    /// Stops the timer.
    /// </summary>
    void Stop();

    /// <summary>
    /// Gets whether the timer is currently running.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Gets or sets the timer interval.
    /// </summary>
    TimeSpan Interval { get; set; }
}
```

**CREATE:** `src/TerminalHost/TerminalHost/Services/TimerService.cs`

```csharp
using Avalonia.Threading;

namespace TerminalHost.Services;

internal sealed class TimerService : ITimerService
{
    public ITimer CreateTimer(TimeSpan interval, Action callback)
    {
        return new AvaloniaTimer(interval, callback);
    }

    public ITimer CreateTimer(TimeSpan interval, Func<Task> asyncCallback)
    {
        return new AvaloniaTimer(interval, () =>
        {
            // Fire and forget, but ensure exceptions are logged
            _ = Task.Run(async () =>
            {
                try
                {
                    await asyncCallback();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Timer callback error: {ex}");
                }
            });
        });
    }

    private sealed class AvaloniaTimer : ITimer
    {
        private readonly DispatcherTimer _timer;
        private readonly Action _callback;

        public AvaloniaTimer(TimeSpan interval, Action callback)
        {
            _callback = callback;
            _timer = new DispatcherTimer
            {
                Interval = interval
            };
            _timer.Tick += OnTick;
        }

        private void OnTick(object? sender, EventArgs e)
        {
            _callback();
        }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();
        public bool IsRunning => _timer.IsEnabled;

        public TimeSpan Interval
        {
            get => _timer.Interval;
            set => _timer.Interval = value;
        }

        public void Dispose()
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
        }
    }
}
```

---

### 2.5 IClipboardService

**CREATE:** `src/TerminalHost/TerminalHost/Services/IClipboardService.cs`

```csharp
namespace TerminalHost.Services;

/// <summary>
/// Abstraction for clipboard operations.
/// Replaces System.Windows.Clipboard.
/// </summary>
public interface IClipboardService
{
    /// <summary>
    /// Sets text to the clipboard.
    /// </summary>
    Task SetTextAsync(string text);

    /// <summary>
    /// Gets text from the clipboard.
    /// </summary>
    Task<string?> GetTextAsync();

    /// <summary>
    /// Checks if clipboard contains text.
    /// </summary>
    Task<bool> ContainsTextAsync();

    /// <summary>
    /// Clears the clipboard.
    /// </summary>
    Task ClearAsync();
}
```

**CREATE:** `src/TerminalHost/TerminalHost/Services/ClipboardService.cs`

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;

namespace TerminalHost.Services;

internal sealed class ClipboardService : IClipboardService
{
    public async Task SetTextAsync(string text)
    {
        var clipboard = GetClipboard();
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    public async Task<string?> GetTextAsync()
    {
        var clipboard = GetClipboard();
        if (clipboard != null)
        {
            return await clipboard.GetTextAsync();
        }
        return null;
    }

    public async Task<bool> ContainsTextAsync()
    {
        var text = await GetTextAsync();
        return !string.IsNullOrEmpty(text);
    }

    public async Task ClearAsync()
    {
        var clipboard = GetClipboard();
        if (clipboard != null)
        {
            await clipboard.ClearAsync();
        }
    }

    private static IClipboard? GetClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.Clipboard;
        }
        return null;
    }
}
```

---

### 2.6 ISystemInfoService

**CREATE:** `src/TerminalHost/TerminalHost/Services/ISystemInfoService.cs`

```csharp
namespace TerminalHost.Services;

/// <summary>
/// Abstraction for system information.
/// Replaces direct Environment and font enumeration calls.
/// </summary>
public interface ISystemInfoService
{
    /// <summary>
    /// Gets the application data directory path.
    /// macOS: ~/Library/Application Support/TerminalHost
    /// </summary>
    string GetApplicationDataPath();

    /// <summary>
    /// Gets the user's home directory.
    /// </summary>
    string GetUserHomePath();

    /// <summary>
    /// Gets the temporary directory path.
    /// </summary>
    string GetTempPath();

    /// <summary>
    /// Gets installed system font family names.
    /// </summary>
    IEnumerable<string> GetInstalledFontFamilies();

    /// <summary>
    /// Gets the default shell command.
    /// </summary>
    string GetDefaultShell();

    /// <summary>
    /// Checks if a font family is installed.
    /// </summary>
    bool IsFontInstalled(string fontFamilyName);
}
```

**CREATE:** `src/TerminalHost/TerminalHost/Services/SystemInfoService.cs`

```csharp
using Avalonia.Media;

namespace TerminalHost.Services;

internal sealed class SystemInfoService : ISystemInfoService
{
    private const string AppName = "TerminalHost";

    public string GetApplicationDataPath()
    {
        // macOS: ~/Library/Application Support/TerminalHost
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "Library", "Application Support", AppName);
    }

    public string GetUserHomePath()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    public string GetTempPath()
    {
        return Path.GetTempPath();
    }

    public IEnumerable<string> GetInstalledFontFamilies()
    {
        return FontManager.Current.SystemFonts.Select(f => f.Name);
    }

    public string GetDefaultShell()
    {
        // Check SHELL environment variable first
        var shell = Environment.GetEnvironmentVariable("SHELL");
        if (!string.IsNullOrEmpty(shell) && File.Exists(shell))
        {
            return shell;
        }

        // macOS default is zsh
        if (File.Exists("/bin/zsh"))
            return "/bin/zsh";

        // Fallback to bash
        if (File.Exists("/bin/bash"))
            return "/bin/bash";

        return "/bin/sh";
    }

    public bool IsFontInstalled(string fontFamilyName)
    {
        return FontManager.Current.SystemFonts
            .Any(f => f.Name.Equals(fontFamilyName, StringComparison.OrdinalIgnoreCase));
    }
}
```

---

## Updated Existing Services

### 2.7 Update ProcessService

**MODIFY:** `src/TerminalHost/TerminalHost/Services/ProcessService.cs`

Add new methods for macOS-specific operations:

```csharp
namespace TerminalHost.Services;

public interface IProcessService
{
    // Existing methods...
    void Start(string fileName, string? arguments = null);
    void Start(ProcessStartInfo startInfo);

    // New macOS-specific methods
    void OpenFolder(string path);
    void RevealInFinder(string filePath);
    void OpenUrl(string url);
    void OpenWithDefaultApp(string filePath);
}

internal sealed class ProcessService : IProcessService
{
    public void Start(string fileName, string? arguments = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments ?? "",
            UseShellExecute = false
        };
        Process.Start(startInfo);
    }

    public void Start(ProcessStartInfo startInfo)
    {
        Process.Start(startInfo);
    }

    public void OpenFolder(string path)
    {
        // macOS: use 'open' command
        Process.Start(new ProcessStartInfo
        {
            FileName = "open",
            Arguments = $"\"{path}\"",
            UseShellExecute = false
        });
    }

    public void RevealInFinder(string filePath)
    {
        // macOS: open -R reveals file in Finder
        Process.Start(new ProcessStartInfo
        {
            FileName = "open",
            Arguments = $"-R \"{filePath}\"",
            UseShellExecute = false
        });
    }

    public void OpenUrl(string url)
    {
        // macOS: open command handles URLs
        Process.Start(new ProcessStartInfo
        {
            FileName = "open",
            Arguments = url,
            UseShellExecute = false
        });
    }

    public void OpenWithDefaultApp(string filePath)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "open",
            Arguments = $"\"{filePath}\"",
            UseShellExecute = false
        });
    }
}
```

---

### 2.7a Update LinkDetectionService (Gap Fix)

**File:** `src/TerminalHost/TerminalHost/Services/LinkDetectionService.cs`

This service contains direct `explorer.exe` calls that need to be replaced.

**Current (around line 89):**
```csharp
Process.Start(new ProcessStartInfo
{
    FileName = "explorer.exe",
    Arguments = $"/select, \"{filePath}\"",
    UseShellExecute = true
});
```

**Replace with IProcessService injection and call:**
```csharp
private readonly IProcessService _processService;

public LinkDetectionService(IProcessService processService)
{
    _processService = processService;
}

// In method that opens file location:
_processService.RevealInFinder(filePath);
```

---

### 2.8 Update ConfigurationService

**MODIFY:** `src/TerminalHost/TerminalHost/Services/ConfigurationService.cs`

Update default paths for macOS (around lines 22-24):

```csharp
internal sealed class ConfigurationService : IConfigurationService
{
    private readonly IFileSystem _fileSystem;
    private readonly string _configDir;
    private readonly string _configPath;

    public ConfigurationService(IFileSystem fileSystem, string? userDataDir = null)
    {
        _fileSystem = fileSystem;

        if (!string.IsNullOrEmpty(userDataDir))
        {
            _configDir = userDataDir;
        }
        else
        {
            // macOS: ~/Library/Application Support/TerminalHost
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            _configDir = Path.Combine(home, "Library", "Application Support", "TerminalHost");
        }

        _configPath = Path.Combine(_configDir, "config.json");
    }

    // ... rest of implementation
}
```

Also update the built-in shell commands detection (around line 154-159):

```csharp
private static bool IsBuiltInCommand(string command)
{
    var builtIns = new[]
    {
        // macOS shells
        "zsh", "/bin/zsh",
        "bash", "/bin/bash",
        "sh", "/bin/sh",
        "fish", "/usr/local/bin/fish", "/opt/homebrew/bin/fish"
    };

    return builtIns.Any(b => command.EndsWith(b, StringComparison.OrdinalIgnoreCase) ||
                             command.Equals(b, StringComparison.OrdinalIgnoreCase));
}
```

---

### 2.9 Update SingleInstanceService

**MODIFY:** `src/TerminalHost/TerminalHost/Services/SingleInstanceService.cs`

Named pipes work on macOS but use different path conventions:

```csharp
internal sealed class SingleInstanceService : ISingleInstanceService
{
    private const string MutexName = "TerminalHost_SingleInstance_Mutex";

    // macOS: Use /tmp for named pipes
    private static readonly string PipeName = Path.Combine(
        Path.GetTempPath(),
        "TerminalHost_IPC_Pipe");

    private Mutex? _mutex;
    private CancellationTokenSource? _pipeServerCts;
    private Task? _pipeServerTask;

    // ... rest of implementation stays the same, but update TryAcquireLock:

    public bool TryAcquireLock()
    {
        try
        {
            // On macOS, Mutex with named prefix works differently
            // Use a file-based lock as fallback
            _mutex = new Mutex(true, MutexName, out bool createdNew);
            return createdNew;
        }
        catch (PlatformNotSupportedException)
        {
            // Fallback: use a lock file
            return TryAcquireFileLock();
        }
    }

    private bool TryAcquireFileLock()
    {
        var lockFile = Path.Combine(Path.GetTempPath(), "TerminalHost.lock");
        try
        {
            // Try to create exclusive lock file
            _lockFileStream = new FileStream(
                lockFile,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private FileStream? _lockFileStream;

    // Update Dispose to clean up lock file
    public void Dispose()
    {
        _pipeServerCts?.Cancel();
        // ... existing cleanup ...
        _lockFileStream?.Dispose();
    }
}
```

---

### 2.10 Delete DarkModeHelper

**DELETE:** `src/TerminalHost/TerminalHost/Services/DarkModeHelper.cs`

This file uses Windows-specific `dwmapi.dll` P/Invoke. Avalonia handles dark mode automatically based on system settings.

---

### 2.11 Create Stub SystemTrayService

**REWRITE:** `src/TerminalHost/TerminalHost/Services/SystemTrayService.cs`

System tray (menu bar extras) on macOS requires native interop. For now, create a no-op implementation:

```csharp
namespace TerminalHost.Services;

/// <summary>
/// macOS implementation of system tray service.
/// Note: Full implementation requires native macOS interop for NSStatusBar.
/// This is a stub that can be enhanced later.
/// </summary>
internal sealed class SystemTrayService : ISystemTrayService
{
    private bool _isEnabled;

    public event EventHandler? ShowRequested;
    public event EventHandler? ExitRequested;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => _isEnabled = value; // No-op for now
    }

    public void Initialize(object mainWindow)
    {
        // No-op: macOS menu bar implementation would go here
        // Could use Avalonia.Native or direct ObjC interop
    }

    public void ShowBalloonTip(string title, string text, int icon = 0)
    {
        // macOS notification could be implemented via NSUserNotificationCenter
        // or the newer UNUserNotificationCenter
        System.Diagnostics.Debug.WriteLine($"[Notification] {title}: {text}");
    }

    public void Dispose()
    {
        // Nothing to dispose in stub
    }
}
```

---

## DI Registration Updates

### 2.12 Update DI Configuration

When App.axaml.cs is fully implemented (Stage 5), register new services:

```csharp
private void ConfigureServices(IServiceCollection services, CommandLineArgs args)
{
    // Platform services (NEW)
    services.AddSingleton<IFolderPickerService, FolderPickerService>();
    services.AddSingleton<IFilePickerService, FilePickerService>();
    services.AddSingleton<IDispatcherService, DispatcherService>();
    services.AddSingleton<ITimerService, TimerService>();
    services.AddSingleton<IClipboardService, ClipboardService>();
    services.AddSingleton<ISystemInfoService, SystemInfoService>();

    // Existing services (some updated)
    services.AddSingleton<ISingleInstanceService, SingleInstanceService>();
    services.AddSingleton<IConfigurationService>(sp =>
        new ConfigurationService(sp.GetRequiredService<IFileSystem>(), args.UserDataDir));
    services.AddSingleton<ISystemTrayService, SystemTrayService>();
    services.AddSingleton<IProcessService, ProcessService>();
    services.AddSingleton<IFileSystem, FileSystem>();
    // ... rest of existing registrations
}
```

---

## File Change Summary

| Action | File | Notes |
|--------|------|-------|
| **CREATE** | `Services/IFolderPickerService.cs` | New interface |
| **CREATE** | `Services/FolderPickerService.cs` | Avalonia implementation |
| **CREATE** | `Services/IFilePickerService.cs` | New interface |
| **CREATE** | `Services/FilePickerService.cs` | Avalonia implementation |
| **CREATE** | `Services/IDispatcherService.cs` | New interface |
| **CREATE** | `Services/DispatcherService.cs` | Avalonia implementation |
| **CREATE** | `Services/ITimerService.cs` | New interface + ITimer |
| **CREATE** | `Services/TimerService.cs` | Avalonia implementation |
| **CREATE** | `Services/IClipboardService.cs` | New interface |
| **CREATE** | `Services/ClipboardService.cs` | Avalonia implementation |
| **CREATE** | `Services/ISystemInfoService.cs` | New interface |
| **CREATE** | `Services/SystemInfoService.cs` | macOS implementation |
| **MODIFY** | `Services/IProcessService.cs` | Add new methods |
| **MODIFY** | `Services/ProcessService.cs` | macOS implementations |
| **MODIFY** | `Services/ConfigurationService.cs` | macOS paths |
| **MODIFY** | `Services/SingleInstanceService.cs` | macOS compatibility |
| **DELETE** | `Services/DarkModeHelper.cs` | Windows-only |
| **REWRITE** | `Services/SystemTrayService.cs` | Stub for macOS |

---

### 2.13 Create IScreenService (NEW - Gap Fix)

**CREATE:** `src/TerminalHost/TerminalHost/Services/IScreenService.cs`

```csharp
namespace TerminalHost.Services;

/// <summary>
/// Abstraction for screen/monitor information.
/// Replaces System.Windows.Forms.Screen and SystemParameters.
/// </summary>
public interface IScreenService
{
    /// <summary>
    /// Gets the primary screen bounds.
    /// </summary>
    ScreenBounds GetPrimaryScreenBounds();

    /// <summary>
    /// Gets the screen containing the specified point.
    /// </summary>
    ScreenBounds GetScreenFromPoint(double x, double y);

    /// <summary>
    /// Gets the working area (excluding taskbar/dock) of the primary screen.
    /// </summary>
    ScreenBounds GetPrimaryWorkingArea();

    /// <summary>
    /// Gets all available screens.
    /// </summary>
    IReadOnlyList<ScreenBounds> GetAllScreens();
}

public record ScreenBounds(double X, double Y, double Width, double Height);
```

**CREATE:** `src/TerminalHost/TerminalHost/Services/ScreenService.cs`

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace TerminalHost.Services;

internal sealed class ScreenService : IScreenService
{
    public ScreenBounds GetPrimaryScreenBounds()
    {
        var screen = GetPrimaryScreen();
        if (screen == null)
            return new ScreenBounds(0, 0, 1920, 1080); // Fallback

        var bounds = screen.Bounds;
        return new ScreenBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    public ScreenBounds GetScreenFromPoint(double x, double y)
    {
        var screens = GetScreens();
        if (screens == null)
            return GetPrimaryScreenBounds();

        var point = new PixelPoint((int)x, (int)y);
        var screen = screens.ScreenFromPoint(point);

        if (screen == null)
            return GetPrimaryScreenBounds();

        var bounds = screen.Bounds;
        return new ScreenBounds(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    public ScreenBounds GetPrimaryWorkingArea()
    {
        var screen = GetPrimaryScreen();
        if (screen == null)
            return new ScreenBounds(0, 0, 1920, 1080);

        var workArea = screen.WorkingArea;
        return new ScreenBounds(workArea.X, workArea.Y, workArea.Width, workArea.Height);
    }

    public IReadOnlyList<ScreenBounds> GetAllScreens()
    {
        var screens = GetScreens();
        if (screens == null)
            return new[] { GetPrimaryScreenBounds() };

        return screens.All
            .Select(s => new ScreenBounds(s.Bounds.X, s.Bounds.Y, s.Bounds.Width, s.Bounds.Height))
            .ToList();
    }

    private static Screens? GetScreens()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.Screens;
        }
        return null;
    }

    private static Screen? GetPrimaryScreen()
    {
        return GetScreens()?.Primary;
    }
}
```

---

### 2.14 Update GitHubService for macOS (Gap Fix)

**MODIFY:** `src/TerminalHost/TerminalHost/Services/GitHubService.cs`

The GitHubService uses `cmd.exe` for running git commands. Update for macOS:

**Lines 486, 494, 498 - Replace cmd.exe usage:**

```csharp
// BEFORE (Windows):
psi.FileName = "cmd.exe";
psi.Arguments = $"/c {command}";

// AFTER (macOS):
psi.FileName = "/bin/sh";
psi.Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"";

// Or use direct execution:
psi.FileName = command.Split(' ')[0];
psi.Arguments = string.Join(' ', command.Split(' ').Skip(1));
psi.UseShellExecute = false;
```

**Better approach - add shell abstraction method:**

```csharp
private ProcessStartInfo CreateShellProcessInfo(string command, string? workingDir = null)
{
    var psi = new ProcessStartInfo
    {
        WorkingDirectory = workingDir ?? Environment.CurrentDirectory,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true
    };

    // macOS uses /bin/sh or /bin/zsh
    psi.FileName = "/bin/sh";
    psi.Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"";

    return psi;
}
```

---

### 2.15 Update TestRunnerService for macOS (Gap Fix)

**MODIFY:** `src/TerminalHost/TerminalHost/Services/TestRunnerService.cs`

**Line 220 - Replace cmd.exe:**

```csharp
// BEFORE (Windows):
psi.FileName = "cmd.exe";
psi.Arguments = $"/c dotnet test {projectPath}";

// AFTER (macOS):
psi.FileName = "/bin/sh";
psi.Arguments = $"-c \"dotnet test {projectPath}\"";

// Or direct execution (preferred):
psi.FileName = "dotnet";
psi.Arguments = $"test {projectPath}";
```

---

### 2.16 Update AppConfiguration Default Shell (Gap Fix)

**MODIFY:** `src/TerminalHost/TerminalHost/Domain/AppConfiguration.cs`

**Line 300:**

```csharp
// BEFORE:
public string ShellCommand { get; set; } = "pwsh.exe";

// AFTER:
public string ShellCommand { get; set; } = GetDefaultShell();

private static string GetDefaultShell()
{
    // Check for environment variable first
    var shell = Environment.GetEnvironmentVariable("SHELL");
    if (!string.IsNullOrEmpty(shell) && File.Exists(shell))
        return shell;

    // macOS defaults
    if (File.Exists("/bin/zsh")) return "/bin/zsh";
    if (File.Exists("/bin/bash")) return "/bin/bash";

    return "/bin/sh";
}
```

---

### 2.17 Update Additional Shell References (Gap Fix)

**Files requiring shell command updates:**

| File | Line | Current | Update To |
|------|------|---------|-----------|
| `ViewModels/SettingsTabViewModel.cs` | 833 | `"pwsh.exe"` | `SystemInfoService.GetDefaultShell()` |
| `ViewModels/ProfilesTabViewModel.cs` | 82 | `"pwsh.exe"` | `SystemInfoService.GetDefaultShell()` |
| `Services/ConfigurationService.cs` | 155-156 | Shell detection | Add macOS shells |

**Update ConfigurationService.cs (lines 155-174):**

```csharp
private static bool IsBuiltInCommand(string command)
{
    var builtIns = new[]
    {
        // macOS shells
        "zsh", "/bin/zsh",
        "bash", "/bin/bash",
        "sh", "/bin/sh",
        "fish", "/usr/local/bin/fish", "/opt/homebrew/bin/fish",
        // Common utilities
        "tmux", "screen"
    };

    var commandName = Path.GetFileName(command);
    return builtIns.Any(b =>
        commandName.Equals(b, StringComparison.OrdinalIgnoreCase) ||
        command.Equals(b, StringComparison.OrdinalIgnoreCase));
}
```

---

## Updated File Change Summary

| Action | File | Notes |
|--------|------|-------|
| **CREATE** | `Services/IFolderPickerService.cs` | New interface |
| **CREATE** | `Services/FolderPickerService.cs` | Avalonia implementation |
| **CREATE** | `Services/IFilePickerService.cs` | New interface |
| **CREATE** | `Services/FilePickerService.cs` | Avalonia implementation |
| **CREATE** | `Services/IDispatcherService.cs` | New interface |
| **CREATE** | `Services/DispatcherService.cs` | Avalonia implementation |
| **CREATE** | `Services/ITimerService.cs` | New interface + ITimer |
| **CREATE** | `Services/TimerService.cs` | Avalonia implementation |
| **CREATE** | `Services/IClipboardService.cs` | New interface |
| **CREATE** | `Services/ClipboardService.cs` | Avalonia implementation |
| **CREATE** | `Services/ISystemInfoService.cs` | New interface |
| **CREATE** | `Services/SystemInfoService.cs` | macOS implementation |
| **CREATE** | `Services/IScreenService.cs` | **NEW** - Screen info interface |
| **CREATE** | `Services/ScreenService.cs` | **NEW** - Avalonia implementation |
| **MODIFY** | `Services/IProcessService.cs` | Add new methods |
| **MODIFY** | `Services/ProcessService.cs` | macOS implementations |
| **MODIFY** | `Services/ConfigurationService.cs` | macOS paths + shell detection |
| **MODIFY** | `Services/SingleInstanceService.cs` | macOS compatibility |
| **MODIFY** | `Services/LinkDetectionService.cs` | **NEW** - Replace explorer.exe |
| **MODIFY** | `Services/GitHubService.cs` | **NEW** - Replace cmd.exe |
| **MODIFY** | `Services/TestRunnerService.cs` | **NEW** - Replace cmd.exe |
| **MODIFY** | `Domain/AppConfiguration.cs` | **NEW** - Default shell |
| **DELETE** | `Services/DarkModeHelper.cs` | Windows-only |
| **REWRITE** | `Services/SystemTrayService.cs` | Stub for macOS |

---

## Verification Steps

### Unit Test Verification
```bash
cd tests/TerminalHost.Tests
dotnet test
```

Expected: All existing tests pass (they mock these interfaces)

### Build Verification
```bash
dotnet build
```

Expected: No compilation errors in Services directory

### Interface Contract Check
- All service interfaces have XML documentation
- All implementations are internal sealed
- All async methods follow naming convention

### Shell Command Verification (NEW)
Test these commands work on macOS:
- Default shell detection returns valid path
- Git commands execute via /bin/sh
- Test runner uses dotnet directly

---

## Next Stage

After completing Stage 2, proceed to **Stage 3: Domain Model Platform Independence** which removes P/Invoke from domain classes.
