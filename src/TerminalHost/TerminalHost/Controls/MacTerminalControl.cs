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
public class MacTerminalControl : Control, ITerminalControl, IDisposable
{
    private IPtyConnection? _pty;
    private Terminal? _terminal;
    private CancellationTokenSource? _readCts;
    private bool _isDisposed;
    private bool _processExited;

    // Terminal dimensions
    private int _columns = 120;
    private int _rows = 30;

    // Font settings
    private FontFamily _fontFamily = new("SF Mono, Menlo, Monaco, monospace");
    private double _fontSize = 13;
    private double _cellWidth;
    private double _cellHeight;

    // Theme
    private TerminalTheme _theme = TerminalTheme.Campbell;

    // Selection state
    private bool _isSelecting;
    private int _selectionStartX, _selectionStartY;
    private int _selectionEndX, _selectionEndY;

    // Restart info (stored for RestartAsync)
    private string? _command;
    private string? _workingDirectory;

    #region ITerminalControl Properties

    public object NativeControl => this;
    public new bool IsFocused => base.IsFocused;
    public bool IsProcessRunning => _pty != null && !_processExited;
    public int? ExitCode => _pty?.ExitCode;

    #endregion

    #region Events

    public new event EventHandler? Loaded;
    public event Action<string>? OutputReceived;
    public event EventHandler<TerminalMouseEventArgs>? MouseClicked;
    public event EventHandler<int>? ProcessExited;

    #endregion

    public MacTerminalControl()
    {
        Focusable = true;
        ClipToBounds = true;

        CalculateCellSize();
    }

    /// <summary>
    /// Gets or sets the terminal color theme.
    /// </summary>
    public new TerminalTheme Theme
    {
        get => _theme;
        set
        {
            _theme = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Gets or sets the font family.
    /// </summary>
    public FontFamily TerminalFontFamily
    {
        get => _fontFamily;
        set
        {
            _fontFamily = value;
            CalculateCellSize();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Gets or sets the font size.
    /// </summary>
    public double TerminalFontSize
    {
        get => _fontSize;
        set
        {
            _fontSize = value;
            CalculateCellSize();
            InvalidateVisual();
        }
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
        _command = command;
        _workingDirectory = workingDirectory;

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

        // Subscribe to ProcessExited event
        _pty.ProcessExited += OnPtyProcessExited;
        _processExited = false;

        _terminal = new Terminal(null, new TerminalOptions
        {
            Cols = _columns,
            Rows = _rows,
        });

        StartReadingOutput();

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

    private void OnPtyProcessExited(object? sender, PtyExitedEventArgs e)
    {
        _processExited = true;
        Dispatcher.UIThread.Post(() =>
        {
            ProcessExited?.Invoke(this, e.ExitCode);
        });
    }

    private void StartReadingOutput()
    {
        _readCts = new CancellationTokenSource();

        Task.Run(async () =>
        {
            var buffer = new byte[4096];

            while (!_readCts.Token.IsCancellationRequested && _pty != null && !_processExited)
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
                    else if (bytesRead == 0)
                    {
                        // Stream closed, process likely exited
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Terminal read error: {ex}");
                    break;
                }
            }
        });
    }

    #region ITerminalControl Methods

    public void WriteToTerminal(string text)
    {
        if (_pty != null && !_processExited)
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
        // This requires accessing the buffer lines and extracting characters
        // between _selectionStart and _selectionEnd positions
        return sb.ToString();
    }

    void ITerminalControl.Focus()
    {
        base.Focus();
    }

    public async Task RestartAsync()
    {
        Kill();

        // Wait a moment for cleanup
        await Task.Delay(100);

        // Reinitialize with stored command/working directory
        if (!string.IsNullOrEmpty(_command) && !string.IsNullOrEmpty(_workingDirectory))
        {
            await InitializeAsync(_command, _workingDirectory);
        }
    }

    public void Kill()
    {
        _readCts?.Cancel();

        if (_pty != null && !_processExited)
        {
            _pty.Kill();
            _processExited = true;
        }
    }

    #endregion

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
            new SolidColorBrush(_theme.Background),
            new Rect(0, 0, Bounds.Width, Bounds.Height));

        if (_terminal == null)
            return;

        var typeface = new Typeface(_fontFamily);

        // Render each cell
        for (int row = 0; row < _rows && row < _terminal.Rows; row++)
        {
            RenderLine(context, typeface, row);
        }

        // Render cursor
        RenderCursor(context);
    }

    private void RenderLine(DrawingContext context, Typeface typeface, int row)
    {
        var buffer = _terminal?.Buffer;
        if (buffer == null) return;

        var y = row * _cellHeight;

        // Get the line from the buffer
        var line = buffer.Lines[row + buffer.YDisp];
        if (line == null) return;

        // Build text for the entire line
        var sb = new StringBuilder();
        for (int col = 0; col < Math.Min(_columns, line.Length); col++)
        {
            var charData = line[col];

            // Get the character to render using the Code property
            // Code 0 is null, Code 32 is space
            if (charData.Code == 0 || charData.Code == 32)
            {
                sb.Append(' ');
            }
            else
            {
                sb.Append(charData.Rune.ToString());
            }
        }

        // Render the line text
        var lineText = sb.ToString().TrimEnd();
        if (!string.IsNullOrEmpty(lineText))
        {
            var formattedText = new FormattedText(
                lineText,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                _fontSize,
                new SolidColorBrush(_theme.Foreground));

            context.DrawText(formattedText, new Point(0, y));
        }

        // Render selection highlight if applicable
        if (HasSelection() && IsLineInSelection(row))
        {
            RenderSelectionForLine(context, row, y);
        }
    }

    private bool IsLineInSelection(int row)
    {
        var minY = Math.Min(_selectionStartY, _selectionEndY);
        var maxY = Math.Max(_selectionStartY, _selectionEndY);
        return row >= minY && row <= maxY;
    }

    private void RenderSelectionForLine(DrawingContext context, int row, double y)
    {
        var minY = Math.Min(_selectionStartY, _selectionEndY);
        var maxY = Math.Max(_selectionStartY, _selectionEndY);
        var startX = _selectionStartY < _selectionEndY ? _selectionStartX : _selectionEndX;
        var endX = _selectionStartY < _selectionEndY ? _selectionEndX : _selectionStartX;

        double selStartX = 0;
        double selEndX = _columns * _cellWidth;

        if (row == minY)
        {
            selStartX = (_selectionStartY <= _selectionEndY ? _selectionStartX : _selectionEndX) * _cellWidth;
        }
        if (row == maxY)
        {
            selEndX = (_selectionStartY <= _selectionEndY ? _selectionEndX : _selectionStartX) * _cellWidth;
        }

        context.FillRectangle(
            new SolidColorBrush(_theme.SelectionBackground),
            new Rect(selStartX, y, selEndX - selStartX, _cellHeight));
    }

    private void RenderCursor(DrawingContext context)
    {
        if (_terminal == null) return;

        var buffer = _terminal.Buffer;
        var cursorX = buffer.X * _cellWidth;
        var cursorY = (buffer.Y - buffer.YDisp + buffer.YBase) * _cellHeight;

        // Ensure cursor is visible
        if (cursorY >= 0 && cursorY < Bounds.Height)
        {
            // Blinking bar cursor
            context.FillRectangle(
                new SolidColorBrush(_theme.CursorColor),
                new Rect(cursorX, cursorY, 2, _cellHeight));
        }
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
        // Handle Ctrl+key combinations first
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            var ctrlSeq = GetCtrlKeySequence(e.Key);
            if (ctrlSeq != null)
                return ctrlSeq;
        }

        // Map Avalonia keys to terminal escape sequences
        return e.Key switch
        {
            Key.Enter => "\r",
            Key.Escape => "\x1b",
            Key.Tab => "\t",
            Key.Back => "\x7f",
            Key.Delete => "\x1b[3~",
            Key.Up => _terminal?.ApplicationCursor == true ? "\x1bOA" : "\x1b[A",
            Key.Down => _terminal?.ApplicationCursor == true ? "\x1bOB" : "\x1b[B",
            Key.Right => _terminal?.ApplicationCursor == true ? "\x1bOC" : "\x1b[C",
            Key.Left => _terminal?.ApplicationCursor == true ? "\x1bOD" : "\x1b[D",
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
            Key.Space => "\x00", // Ctrl+Space (NUL)
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
        else if (point.Properties.IsRightButtonPressed)
        {
            MouseClicked?.Invoke(this, new TerminalMouseEventArgs
            {
                X = col,
                Y = row,
                IsRightButton = true,
                IsCtrlPressed = e.KeyModifiers.HasFlag(KeyModifiers.Control),
                IsShiftPressed = e.KeyModifiers.HasFlag(KeyModifiers.Shift)
            });
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_isSelecting)
        {
            var position = e.GetPosition(this);
            _selectionEndX = Math.Max(0, Math.Min(_columns - 1, (int)(position.X / _cellWidth)));
            _selectionEndY = Math.Max(0, Math.Min(_rows - 1, (int)(position.Y / _cellHeight)));
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

    #region IDisposable

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _readCts?.Cancel();
        _pty?.Kill();
        _pty?.Dispose();
    }

    #endregion
}
