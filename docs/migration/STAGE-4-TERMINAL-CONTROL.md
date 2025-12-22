# Stage 4: Terminal Control Integration

## Overview

| Attribute | Value |
|-----------|-------|
| **Estimated Effort** | 5-7 days |
| **Risk Level** | **High** |
| **Dependencies** | Stage 2, Stage 3 complete |
| **Blocking For** | Stages 5, 6, 7 |

## Objective

Implement the terminal emulation stack for macOS using XtermSharp (or alternative) and Pty.Net. This replaces EasyWindowsTerminalControl which is Windows-only.

## Success Criteria

- [ ] PTY processes spawn correctly on macOS
- [ ] Terminal output renders in Avalonia control
- [ ] Keyboard input works correctly
- [ ] Mouse selection works
- [ ] Terminal themes apply
- [ ] Process exit detection works
- [ ] Multiple terminal instances supported

---

## Technology Selection

### Recommended Stack

| Component | Library | Purpose |
|-----------|---------|---------|
| PTY Management | **Pty.Net** (Microsoft) | Cross-platform pseudo-terminal |
| Terminal Emulation | **XtermSharp** | VT100/xterm parser and state |
| Rendering | Custom Avalonia control | Visual rendering |

### Alternative Options

If XtermSharp doesn't work well:
1. **AvalonStudio.TerminalEmulator** - Avalonia-native terminal
2. **Custom xterm.js wrapper** - WebView-based terminal
3. **Native macOS Terminal.app interop** - AppleScript/automation

---

## Detailed Implementation

### 4.1 Add XtermSharp as Submodule

XtermSharp may need to be added as a source reference:

```bash
cd /path/to/TerminalHost
mkdir -p external
git submodule add https://github.com/migueldeicaza/XtermSharp.git external/XtermSharp
```

**Alternative:** If a NuGet package is available, add to .csproj instead.

**UPDATE TerminalHost.csproj:**
```xml
<ItemGroup>
  <ProjectReference Include="..\..\..\..\external\XtermSharp\XtermSharp\XtermSharp.csproj" />
</ItemGroup>
```

---

### 4.2 Create MacTerminalControl

**CREATE:** `src/TerminalHost/TerminalHost/Controls/MacTerminalControl.cs`

```csharp
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Pty.Net;
using TerminalHost.Domain;
using XtermSharp;

namespace TerminalHost.Controls;

/// <summary>
/// Avalonia control that hosts a terminal emulator using XtermSharp and Pty.Net.
/// </summary>
public class MacTerminalControl : Control, ITerminalControl
{
    private IPtyConnection? _pty;
    private Terminal? _terminal;
    private CancellationTokenSource? _readCts;
    private bool _isLoaded;

    // Terminal dimensions
    private int _columns = 120;
    private int _rows = 30;

    // Font settings
    private FontFamily _fontFamily = new("SF Mono, Menlo, Monaco, monospace");
    private double _fontSize = 13;
    private double _cellWidth;
    private double _cellHeight;

    // Colors (Campbell theme)
    private readonly Color[] _ansiColors = new Color[]
    {
        Color.FromRgb(0x0C, 0x0C, 0x0C), // Black
        Color.FromRgb(0xC5, 0x0F, 0x1F), // Red
        Color.FromRgb(0x13, 0xA1, 0x0E), // Green
        Color.FromRgb(0xC1, 0x9C, 0x00), // Yellow
        Color.FromRgb(0x00, 0x37, 0xDA), // Blue
        Color.FromRgb(0x88, 0x17, 0x98), // Magenta
        Color.FromRgb(0x3A, 0x96, 0xDD), // Cyan
        Color.FromRgb(0xCC, 0xCC, 0xCC), // White
        Color.FromRgb(0x76, 0x76, 0x76), // Bright Black
        Color.FromRgb(0xE7, 0x48, 0x56), // Bright Red
        Color.FromRgb(0x16, 0xC6, 0x0C), // Bright Green
        Color.FromRgb(0xF9, 0xF1, 0xA5), // Bright Yellow
        Color.FromRgb(0x3B, 0x78, 0xFF), // Bright Blue
        Color.FromRgb(0xB4, 0x00, 0x9E), // Bright Magenta
        Color.FromRgb(0x61, 0xD6, 0xD6), // Bright Cyan
        Color.FromRgb(0xF2, 0xF2, 0xF2), // Bright White
    };

    private Color _backgroundColor = Color.FromRgb(0x0C, 0x0C, 0x0C);
    private Color _foregroundColor = Color.FromRgb(0xCC, 0xCC, 0xCC);

    // Selection state
    private bool _isSelecting;
    private int _selectionStartX, _selectionStartY;
    private int _selectionEndX, _selectionEndY;

    // ITerminalControl implementation
    public object NativeControl => this;
    public bool IsFocused => this.IsFocused;
    public bool IsProcessRunning => _pty != null && !_pty.HasExited;
    public int? ExitCode => _pty?.ExitCode;

    public event EventHandler? Loaded;
    public event Action<string>? OutputReceived;
    public event EventHandler<TerminalMouseEventArgs>? MouseClicked;
    public event EventHandler<int>? ProcessExited;

    public MacTerminalControl()
    {
        Focusable = true;
        ClipToBounds = true;

        CalculateCellSize();
    }

    private void CalculateCellSize()
    {
        // Calculate cell dimensions based on font
        var typeface = new Typeface(_fontFamily);
        var formattedText = new FormattedText(
            "M",
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            _fontSize,
            Brushes.White);

        _cellWidth = formattedText.Width;
        _cellHeight = formattedText.Height;
    }

    /// <summary>
    /// Initialize the terminal with a PTY process.
    /// </summary>
    public async Task InitializeAsync(string command, string workingDirectory)
    {
        var options = new PtyOptions
        {
            Name = "xterm-256color",
            Cols = _columns,
            Rows = _rows,
            Cwd = workingDirectory,
            App = command,
            Environment = GetEnvironmentVariables()
        };

        _pty = await PtyProvider.SpawnAsync(options, CancellationToken.None);

        _terminal = new Terminal(null, new TerminalOptions
        {
            Cols = _columns,
            Rows = _rows,
        });

        StartReadingOutput();

        _isLoaded = true;
        Loaded?.Invoke(this, EventArgs.Empty);

        InvalidateVisual();
    }

    private Dictionary<string, string> GetEnvironmentVariables()
    {
        var env = new Dictionary<string, string>();

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key && entry.Value is string value)
            {
                env[key] = value;
            }
        }

        // Terminal-specific settings
        env["TERM"] = "xterm-256color";
        env["COLORTERM"] = "truecolor";
        env["LANG"] = "en_US.UTF-8";

        return env;
    }

    private void StartReadingOutput()
    {
        _readCts = new CancellationTokenSource();

        Task.Run(async () =>
        {
            var buffer = new byte[4096];

            while (!_readCts.Token.IsCancellationRequested && _pty != null && !_pty.HasExited)
            {
                try
                {
                    var bytesRead = await _pty.ReaderStream.ReadAsync(
                        buffer, 0, buffer.Length, _readCts.Token);

                    if (bytesRead > 0)
                    {
                        var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                        // Feed to terminal emulator
                        _terminal?.Feed(text);

                        // Notify listeners
                        OutputReceived?.Invoke(text);

                        // Request redraw on UI thread
                        await Dispatcher.UIThread.InvokeAsync(InvalidateVisual);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Terminal read error: {ex}");
                }
            }

            // Process exited
            if (_pty != null)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ProcessExited?.Invoke(this, _pty.ExitCode);
                });
            }
        });
    }

    public void WriteToTerminal(string text)
    {
        if (_pty != null && !_pty.HasExited)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            _pty.WriterStream.Write(bytes, 0, bytes.Length);
            _pty.WriterStream.Flush();
        }
    }

    public void WriteToTerminal(ReadOnlySpan<char> text)
    {
        WriteToTerminal(text.ToString());
    }

    public string GetSelectedText()
    {
        if (_terminal == null || !HasSelection())
            return string.Empty;

        // Extract text from selection range
        var sb = new StringBuilder();
        // TODO: Implement proper selection extraction from XtermSharp buffer
        return sb.ToString();
    }

    public new void Focus()
    {
        base.Focus();
    }

    public async Task RestartAsync()
    {
        Kill();

        // Wait a moment for cleanup
        await Task.Delay(100);

        // Reinitialize would need the original command/working directory
        // This should be tracked and reused
    }

    public void Kill()
    {
        _readCts?.Cancel();

        if (_pty != null && !_pty.HasExited)
        {
            _pty.Kill();
        }
    }

    private bool HasSelection()
    {
        return _selectionStartX != _selectionEndX || _selectionStartY != _selectionEndY;
    }

    #region Rendering

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // Draw background
        context.FillRectangle(
            new SolidColorBrush(_backgroundColor),
            new Rect(0, 0, Bounds.Width, Bounds.Height));

        if (_terminal == null)
            return;

        var typeface = new Typeface(_fontFamily);

        // Render each cell
        for (int row = 0; row < _rows && row < _terminal.Rows; row++)
        {
            for (int col = 0; col < _columns && col < _terminal.Cols; col++)
            {
                RenderCell(context, typeface, col, row);
            }
        }

        // Render cursor
        RenderCursor(context);
    }

    private void RenderCell(DrawingContext context, Typeface typeface, int col, int row)
    {
        // Get character data from terminal buffer
        // Note: XtermSharp API may differ - adjust as needed
        var buffer = _terminal?.Buffer;
        if (buffer == null) return;

        // Get the line and character
        // XtermSharp uses a different buffer structure - adapt as needed
        var x = col * _cellWidth;
        var y = row * _cellHeight;

        // For now, render placeholder
        // TODO: Proper XtermSharp buffer access
    }

    private void RenderCursor(DrawingContext context)
    {
        if (_terminal == null) return;

        var cursorX = _terminal.Buffer.X * _cellWidth;
        var cursorY = _terminal.Buffer.Y * _cellHeight;

        // Blinking bar cursor
        context.FillRectangle(
            new SolidColorBrush(_foregroundColor),
            new Rect(cursorX, cursorY, 2, _cellHeight));
    }

    #endregion

    #region Input Handling

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);

        if (!string.IsNullOrEmpty(e.Text))
        {
            WriteToTerminal(e.Text);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var sequence = GetKeySequence(e);
        if (!string.IsNullOrEmpty(sequence))
        {
            WriteToTerminal(sequence);
            e.Handled = true;
        }
    }

    private string? GetKeySequence(KeyEventArgs e)
    {
        // Map Avalonia keys to terminal escape sequences
        return e.Key switch
        {
            Key.Enter => "\r",
            Key.Escape => "\x1b",
            Key.Tab => "\t",
            Key.Back => "\x7f",
            Key.Delete => "\x1b[3~",
            Key.Up => "\x1b[A",
            Key.Down => "\x1b[B",
            Key.Right => "\x1b[C",
            Key.Left => "\x1b[D",
            Key.Home => "\x1b[H",
            Key.End => "\x1b[F",
            Key.PageUp => "\x1b[5~",
            Key.PageDown => "\x1b[6~",
            Key.Insert => "\x1b[2~",
            Key.F1 => "\x1bOP",
            Key.F2 => "\x1bOQ",
            Key.F3 => "\x1bOR",
            Key.F4 => "\x1bOS",
            Key.F5 => "\x1b[15~",
            Key.F6 => "\x1b[17~",
            Key.F7 => "\x1b[18~",
            Key.F8 => "\x1b[19~",
            Key.F9 => "\x1b[20~",
            Key.F10 => "\x1b[21~",
            Key.F11 => "\x1b[23~",
            Key.F12 => "\x1b[24~",

            // Ctrl+key combinations
            _ when e.KeyModifiers.HasFlag(KeyModifiers.Control) =>
                GetCtrlKeySequence(e.Key),

            _ => null
        };
    }

    private string? GetCtrlKeySequence(Key key)
    {
        // Ctrl+A through Ctrl+Z
        if (key >= Key.A && key <= Key.Z)
        {
            return ((char)(key - Key.A + 1)).ToString();
        }

        return key switch
        {
            Key.OemOpenBrackets => "\x1b", // Ctrl+[
            Key.OemCloseBrackets => "\x1d", // Ctrl+]
            Key.OemBackslash => "\x1c", // Ctrl+\
            _ => null
        };
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        Focus();

        var point = e.GetCurrentPoint(this);
        var position = point.Position;

        var col = (int)(position.X / _cellWidth);
        var row = (int)(position.Y / _cellHeight);

        if (point.Properties.IsLeftButtonPressed)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                // Ctrl+Click for link detection
                MouseClicked?.Invoke(this, new TerminalMouseEventArgs
                {
                    X = col,
                    Y = row,
                    IsLeftButton = true,
                    IsCtrlPressed = true
                });
            }
            else
            {
                // Start selection
                _isSelecting = true;
                _selectionStartX = _selectionEndX = col;
                _selectionStartY = _selectionEndY = row;
            }
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_isSelecting)
        {
            var position = e.GetPosition(this);
            _selectionEndX = (int)(position.X / _cellWidth);
            _selectionEndY = (int)(position.Y / _cellHeight);
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isSelecting = false;
    }

    #endregion

    #region Resizing

    protected override Size MeasureOverride(Size availableSize)
    {
        return availableSize;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);

        if (_cellWidth > 0 && _cellHeight > 0)
        {
            var newCols = (int)(e.NewSize.Width / _cellWidth);
            var newRows = (int)(e.NewSize.Height / _cellHeight);

            if (newCols != _columns || newRows != _rows)
            {
                _columns = Math.Max(1, newCols);
                _rows = Math.Max(1, newRows);

                _terminal?.Resize(_columns, _rows);
                _pty?.Resize(_columns, _rows);
            }
        }
    }

    #endregion

    public void Dispose()
    {
        _readCts?.Cancel();
        _pty?.Kill();
        _pty?.Dispose();
    }
}
```

---

### 4.3 Update TerminalControlFactory

**REWRITE:** `src/TerminalHost/TerminalHost/Services/TerminalControlFactory.cs`

```csharp
using TerminalHost.Controls;
using TerminalHost.Domain;

namespace TerminalHost.Services;

public interface ITerminalControlFactory
{
    /// <summary>
    /// Creates a terminal control for the given session.
    /// </summary>
    Task<ITerminalControl> CreateTerminalControlAsync(TerminalSession session);
}

internal sealed class TerminalControlFactory : ITerminalControlFactory
{
    private readonly IFileSystem _fileSystem;
    private readonly IDialogService _dialogService;
    private readonly ISystemInfoService _systemInfoService;

    public TerminalControlFactory(
        IFileSystem fileSystem,
        IDialogService dialogService,
        ISystemInfoService systemInfoService)
    {
        _fileSystem = fileSystem;
        _dialogService = dialogService;
        _systemInfoService = systemInfoService;
    }

    public async Task<ITerminalControl> CreateTerminalControlAsync(TerminalSession session)
    {
        var profile = session.Profile;
        var workingDir = profile.GetExpandedWorkingDir();
        var command = GetCommand(profile);

        // Verify command exists
        if (!IsValidCommand(command))
        {
            await ShowCommandWarningAsync(command);
            command = _systemInfoService.GetDefaultShell();
        }

        // Ensure working directory exists
        if (string.IsNullOrEmpty(workingDir) || !_fileSystem.DirectoryExists(workingDir))
        {
            workingDir = _systemInfoService.GetUserHomePath();
        }

        var control = new MacTerminalControl();
        await control.InitializeAsync(command, workingDir);

        return control;
    }

    private string GetCommand(Profile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Command))
        {
            return _systemInfoService.GetDefaultShell();
        }

        // Expand environment variables
        return Environment.ExpandEnvironmentVariables(profile.Command);
    }

    private bool IsValidCommand(string command)
    {
        // Check if it's a full path that exists
        if (File.Exists(command))
            return true;

        // Check if it's in PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var paths = pathEnv.Split(':');

        foreach (var path in paths)
        {
            var fullPath = Path.Combine(path, command);
            if (File.Exists(fullPath))
                return true;
        }

        // Check common locations
        var commonPaths = new[]
        {
            "/bin", "/usr/bin", "/usr/local/bin",
            "/opt/homebrew/bin", // Apple Silicon Homebrew
        };

        foreach (var path in commonPaths)
        {
            var fullPath = Path.Combine(path, command);
            if (File.Exists(fullPath))
                return true;
        }

        return false;
    }

    private async Task ShowCommandWarningAsync(string command)
    {
        await Task.Run(() =>
        {
            // Use dispatcher to show on UI thread
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                _dialogService.ShowWarning(
                    $"Command not found: {command}\n\nFalling back to default shell.",
                    "Terminal Warning");
            });
        });
    }
}
```

---

### 4.4 Terminal Theme Support

**CREATE:** `src/TerminalHost/TerminalHost/Domain/TerminalTheme.cs`

```csharp
using Avalonia.Media;

namespace TerminalHost.Domain;

/// <summary>
/// Terminal color theme definition.
/// </summary>
public class TerminalTheme
{
    public string Name { get; set; } = "Default";
    public Color Background { get; set; }
    public Color Foreground { get; set; }
    public Color SelectionBackground { get; set; }
    public Color CursorColor { get; set; }
    public Color[] AnsiColors { get; set; } = new Color[16];

    /// <summary>
    /// Campbell theme (Windows Terminal default).
    /// </summary>
    public static TerminalTheme Campbell => new()
    {
        Name = "Campbell",
        Background = Color.FromRgb(0x0C, 0x0C, 0x0C),
        Foreground = Color.FromRgb(0xCC, 0xCC, 0xCC),
        SelectionBackground = Color.FromRgb(0x26, 0x4F, 0x78),
        CursorColor = Color.FromRgb(0xCC, 0xCC, 0xCC),
        AnsiColors = new[]
        {
            Color.FromRgb(0x0C, 0x0C, 0x0C), // Black
            Color.FromRgb(0xC5, 0x0F, 0x1F), // Red
            Color.FromRgb(0x13, 0xA1, 0x0E), // Green
            Color.FromRgb(0xC1, 0x9C, 0x00), // Yellow
            Color.FromRgb(0x00, 0x37, 0xDA), // Blue
            Color.FromRgb(0x88, 0x17, 0x98), // Magenta
            Color.FromRgb(0x3A, 0x96, 0xDD), // Cyan
            Color.FromRgb(0xCC, 0xCC, 0xCC), // White
            Color.FromRgb(0x76, 0x76, 0x76), // Bright Black
            Color.FromRgb(0xE7, 0x48, 0x56), // Bright Red
            Color.FromRgb(0x16, 0xC6, 0x0C), // Bright Green
            Color.FromRgb(0xF9, 0xF1, 0xA5), // Bright Yellow
            Color.FromRgb(0x3B, 0x78, 0xFF), // Bright Blue
            Color.FromRgb(0xB4, 0x00, 0x9E), // Bright Magenta
            Color.FromRgb(0x61, 0xD6, 0xD6), // Bright Cyan
            Color.FromRgb(0xF2, 0xF2, 0xF2), // Bright White
        }
    };

    /// <summary>
    /// One Dark theme.
    /// </summary>
    public static TerminalTheme OneDark => new()
    {
        Name = "One Dark",
        Background = Color.FromRgb(0x28, 0x2C, 0x34),
        Foreground = Color.FromRgb(0xAB, 0xB2, 0xBF),
        SelectionBackground = Color.FromRgb(0x3E, 0x44, 0x51),
        CursorColor = Color.FromRgb(0x52, 0x8B, 0xFF),
        AnsiColors = new[]
        {
            Color.FromRgb(0x28, 0x2C, 0x34), // Black
            Color.FromRgb(0xE0, 0x6C, 0x75), // Red
            Color.FromRgb(0x98, 0xC3, 0x79), // Green
            Color.FromRgb(0xE5, 0xC0, 0x7B), // Yellow
            Color.FromRgb(0x61, 0xAF, 0xEF), // Blue
            Color.FromRgb(0xC6, 0x78, 0xDD), // Magenta
            Color.FromRgb(0x56, 0xB6, 0xC2), // Cyan
            Color.FromRgb(0xAB, 0xB2, 0xBF), // White
            Color.FromRgb(0x5C, 0x63, 0x70), // Bright Black
            Color.FromRgb(0xE0, 0x6C, 0x75), // Bright Red
            Color.FromRgb(0x98, 0xC3, 0x79), // Bright Green
            Color.FromRgb(0xE5, 0xC0, 0x7B), // Bright Yellow
            Color.FromRgb(0x61, 0xAF, 0xEF), // Bright Blue
            Color.FromRgb(0xC6, 0x78, 0xDD), // Bright Magenta
            Color.FromRgb(0x56, 0xB6, 0xC2), // Bright Cyan
            Color.FromRgb(0xFF, 0xFF, 0xFF), // Bright White
        }
    };
}
```

---

### 4.5 Alternative: AvalonStudio Terminal

If XtermSharp proves difficult, here's a simpler alternative using AvalonStudio's terminal:

**Option B - Add NuGet package:**
```xml
<PackageReference Include="AvalonStudio.Terminal" Version="x.x.x" />
```

The AvalonStudio terminal control is designed for Avalonia and may be easier to integrate.

---

## File Change Summary

| Action | File | Notes |
|--------|------|-------|
| **CREATE** | `Controls/MacTerminalControl.cs` | Main terminal control |
| **CREATE** | `Domain/TerminalTheme.cs` | Theme definitions |
| **REWRITE** | `Services/TerminalControlFactory.cs` | Async creation |
| **ADD** | External XtermSharp reference | Submodule or package |
| **UPDATE** | `TerminalHost.csproj` | Add Pty.Net, XtermSharp |

---

## Testing Strategy

### Manual Testing Checklist

1. **Basic Shell**
   - [ ] Open terminal with default shell (zsh)
   - [ ] Type commands and see output
   - [ ] Verify colors work

2. **Keyboard Input**
   - [ ] Regular typing
   - [ ] Enter key executes commands
   - [ ] Arrow keys for history/navigation
   - [ ] Tab completion
   - [ ] Ctrl+C interrupts

3. **Mouse**
   - [ ] Click to focus
   - [ ] Selection with drag
   - [ ] Ctrl+Click for links

4. **Resize**
   - [ ] Terminal resizes with window
   - [ ] Content reflows correctly

5. **Process Management**
   - [ ] Process exit detected
   - [ ] Can restart terminal
   - [ ] Multiple terminals work

---

## Known Issues & Mitigations

### Issue 1: XtermSharp Buffer API
XtermSharp's buffer API may differ from expected. Need to study XtermSharp source to correctly read character/attribute data.

**Mitigation:** Start with simpler terminal rendering, enhance incrementally.

### Issue 2: Pty.Net on Apple Silicon
Pty.Net should work on Apple Silicon but needs verification.

**Mitigation:** Test early on both Intel and Apple Silicon Macs.

### Issue 3: Performance
Custom rendering may be slower than native terminal.

**Mitigation:** Profile rendering, use dirty region tracking, batch updates.

---

## Next Stage

After completing Stage 4, proceed to **Stage 5: Core UI Migration (Avalonia)** which migrates App.xaml and MainWindow.xaml to Avalonia.
