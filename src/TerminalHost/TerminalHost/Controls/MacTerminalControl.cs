using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Threading;
using TerminalHost.Domain;
using TerminalHost.Services;
using VtNetCore.VirtualTerminal;
using VtNetCore.VirtualTerminal.Layout;
using VtNetCore.XTermParser;

namespace TerminalHost.Controls;

/// <summary>
/// Avalonia control that hosts a terminal emulator using VtNetCore and MacPtyService.
/// </summary>
public class MacTerminalControl : Control, ITerminalControl, IDisposable
{
    private IPtyService? _ptyService;
    private VirtualTerminalController? _terminalController;
    private DataConsumer? _dataConsumer;
    private VirtualTerminalViewPort? _viewPort;
    private CancellationTokenSource? _readCts;
    private bool _disposed;

    // Terminal dimensions
    private int _columns = 80;
    private int _rows = 24;

    // Font settings
    private double _charWidth = 8;
    private double _charHeight = 16;
    private Typeface _typeface;
    private Typeface _fallbackTypeface;
    private double _fontSize = 14;

    // Cursor blinking
    private bool _cursorVisible = true;
    private DispatcherTimer? _cursorTimer;

    // Selection state
    private bool _isSelecting;
    private TextPosition? _selectionStart;
    private TextRange? _selection;

    // Scroll state
    private int _scrollOffset;

    // Restart info
    private string? _command;
    private string? _workingDirectory;

    // Terminal color palette
    private static readonly Dictionary<string, Color> ColorPalette = new()
    {
        { "Black", Color.Parse("#000000") },
        { "Red", Color.Parse("#CD0000") },
        { "Green", Color.Parse("#00CD00") },
        { "Yellow", Color.Parse("#CDCD00") },
        { "Blue", Color.Parse("#0000EE") },
        { "Magenta", Color.Parse("#CD00CD") },
        { "Cyan", Color.Parse("#00CDCD") },
        { "White", Color.Parse("#E5E5E5") },
        { "BrightBlack", Color.Parse("#7F7F7F") },
        { "BrightRed", Color.Parse("#FF0000") },
        { "BrightGreen", Color.Parse("#00FF00") },
        { "BrightYellow", Color.Parse("#FFFF00") },
        { "BrightBlue", Color.Parse("#5C5CFF") },
        { "BrightMagenta", Color.Parse("#FF00FF") },
        { "BrightCyan", Color.Parse("#00FFFF") },
        { "BrightWhite", Color.Parse("#FFFFFF") },
    };

    private static readonly Color DefaultForeground = Color.Parse("#E5E5E5");
    private static readonly Color DefaultBackground = Color.Parse("#1E1E1E");

    // Cached brushes
    private static readonly SolidColorBrush DefaultForegroundBrush = new(DefaultForeground);
    private static readonly SolidColorBrush DefaultBackgroundBrush = new(DefaultBackground);
    private static readonly SolidColorBrush ScrollIndicatorTextBrush = new(Color.Parse("#888888"));
    private static readonly SolidColorBrush ScrollIndicatorBackgroundBrush = new(Color.Parse("#333333"));
    private static readonly Dictionary<Color, SolidColorBrush> BrushCache = new();

    // Render throttling
    private bool _renderPending;
    private DateTime _lastRenderRequest;
    private static readonly TimeSpan RenderThrottleInterval = TimeSpan.FromMilliseconds(16);

    #region ITerminalControl Properties

    public object NativeControl => this;
    public new bool IsFocused => base.IsFocused;
    public bool IsProcessRunning => _ptyService?.IsRunning ?? false;
    public int? ExitCode { get; private set; }

    #endregion

    #region Events

    public new event EventHandler? Loaded;
    public event Action<string>? OutputReceived;
    public event EventHandler<TerminalMouseEventArgs>? MouseClicked;
    public event EventHandler<int>? ProcessExited;

    #endregion

    public MacTerminalControl()
    {
        // Use Menlo on macOS
        _typeface = new Typeface(new FontFamily("Menlo"), FontStyle.Normal, FontWeight.Normal);
        _fallbackTypeface = new Typeface(new FontFamily("STIX Two Math, Arial, Apple Symbols, Arial Unicode MS, LastResort"), FontStyle.Normal, FontWeight.Normal);

        Focusable = true;
        ClipToBounds = true;

        CalculateCharacterSize();
    }

    private static SolidColorBrush GetCachedBrush(Color color)
    {
        if (color == DefaultForeground) return DefaultForegroundBrush;
        if (color == DefaultBackground) return DefaultBackgroundBrush;

        if (!BrushCache.TryGetValue(color, out var brush))
        {
            brush = new SolidColorBrush(color);
            BrushCache[color] = brush;
        }
        return brush;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        StartCursorBlink();

        // Schedule a render after the control is fully attached and sized
        Dispatcher.UIThread.Post(() =>
        {
            InvalidateVisual();
            Focus();
        }, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        StopCursorBlink();
    }

    private void StartCursorBlink()
    {
        _cursorTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _cursorTimer.Tick += (_, _) =>
        {
            _cursorVisible = !_cursorVisible;
            if (_terminalController != null && !IsScrolledBack && CursorVisible)
            {
                InvalidateVisual();
            }
        };
        _cursorTimer.Start();
    }

    private void StopCursorBlink()
    {
        _cursorTimer?.Stop();
        _cursorTimer = null;
    }

    private void CalculateCharacterSize()
    {
        var formattedText = new FormattedText(
            "M",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            _typeface,
            _fontSize,
            Brushes.White);

        _charWidth = formattedText.Width;
        _charHeight = formattedText.Height;

        if (_charWidth <= 0) _charWidth = 8;
        if (_charHeight <= 0) _charHeight = 16;
    }

    /// <summary>
    /// Initialize the terminal with a PTY process.
    /// </summary>
    public async Task InitializeAsync(string command, string workingDirectory)
    {
        _command = command;
        _workingDirectory = workingDirectory;

        // Calculate size from bounds if available
        if (_charWidth > 0 && _charHeight > 0 && Bounds.Width > 0 && Bounds.Height > 0)
        {
            _columns = Math.Max(1, (int)(Bounds.Width / _charWidth));
            _rows = Math.Max(1, (int)(Bounds.Height / _charHeight));
        }

        // Create VtNetCore terminal controller
        _terminalController = new VirtualTerminalController
        {
            Columns = _columns,
            Rows = _rows,
            VisibleColumns = _columns,
            VisibleRows = _rows
        };
        _dataConsumer = new DataConsumer(_terminalController);
        _viewPort = new VirtualTerminalViewPort(_terminalController);

        // Create PTY service
        _ptyService = new MacPtyService();
        _ptyService.ProcessExited += OnProcessExited;

        await _ptyService.StartAsync(_columns, _rows, workingDirectory, command);

        // Start reading from PTY
        _readCts = new CancellationTokenSource();
        _ = ReadOutputAsync(_readCts.Token);

        Loaded?.Invoke(this, EventArgs.Empty);

        // Trigger initial render - schedule it for when the control might be in the visual tree
        InvalidateVisual();
        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
    }

    private async Task ReadOutputAsync(CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(4096);

        try
        {
            while (!cancellationToken.IsCancellationRequested && _ptyService?.ReaderStream != null)
            {
                var bytesRead = await _ptyService.ReaderStream.ReadAsync(buffer.AsMemory(0, 4096), cancellationToken);
                if (bytesRead == 0)
                    break;

                // Process through VtNetCore
                _dataConsumer?.Push(buffer.AsSpan(0, bytesRead).ToArray());

                // Notify listeners
                var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                OutputReceived?.Invoke(text);

                // Request redraw
                RequestRender();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void RequestRender()
    {
        if (_renderPending)
            return;

        var now = DateTime.UtcNow;
        var elapsed = now - _lastRenderRequest;

        if (elapsed >= RenderThrottleInterval)
        {
            _lastRenderRequest = now;
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        }
        else
        {
            _renderPending = true;
            Dispatcher.UIThread.Post(() =>
            {
                _renderPending = false;
                _lastRenderRequest = DateTime.UtcNow;
                InvalidateVisual();
            }, DispatcherPriority.Background);
        }
    }

    private void OnProcessExited(object? sender, int exitCode)
    {
        ExitCode = exitCode;
        Dispatcher.UIThread.Post(() =>
        {
            ProcessExited?.Invoke(this, exitCode);
        });
    }

    #region Properties

    private int TopRow => _viewPort?.TopRow ?? 0;
    private bool CursorVisible => _terminalController?.CursorState.ShowCursor ?? false;
    private int MaxScrollBack => TopRow;
    private bool IsScrolledBack => _scrollOffset > 0;

    #endregion

    #region ITerminalControl Methods

    public void WriteToTerminal(string text)
    {
        if (_ptyService != null && _ptyService.IsRunning)
        {
            _ = _ptyService.WriteAsync(text);
        }
    }

    public void WriteToTerminal(ReadOnlySpan<char> text)
    {
        WriteToTerminal(text.ToString());
    }

    public string GetSelectedText()
    {
        if (_selection == null || _viewPort == null)
            return string.Empty;

        var topLeft = _selection.TopLeft;
        var bottomRight = _selection.BottomRight;

        var sb = new StringBuilder();

        for (int row = topLeft.Row; row <= bottomRight.Row; row++)
        {
            var spans = _viewPort.GetPageSpans(row, 1, _columns);
            if (spans.Count == 0 || spans[0]?.Spans == null)
            {
                if (row < bottomRight.Row)
                    sb.AppendLine();
                continue;
            }

            var rowSpans = spans[0].Spans;
            int col = 0;

            foreach (var span in rowSpans)
            {
                if (string.IsNullOrEmpty(span.Text))
                    continue;

                foreach (var c in span.Text)
                {
                    bool inSelection;
                    if (row == topLeft.Row && row == bottomRight.Row)
                    {
                        inSelection = col >= topLeft.Column && col <= bottomRight.Column;
                    }
                    else if (row == topLeft.Row)
                    {
                        inSelection = col >= topLeft.Column;
                    }
                    else if (row == bottomRight.Row)
                    {
                        inSelection = col <= bottomRight.Column;
                    }
                    else
                    {
                        inSelection = true;
                    }

                    if (inSelection)
                        sb.Append(c);

                    col++;
                }
            }

            if (row < bottomRight.Row)
                sb.AppendLine();
        }

        var lines = sb.ToString().Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd();
        }
        return string.Join("\n", lines).TrimEnd();
    }

    void ITerminalControl.Focus()
    {
        base.Focus();
    }

    public async Task RestartAsync()
    {
        Kill();
        await Task.Delay(100);

        if (!string.IsNullOrEmpty(_command) && !string.IsNullOrEmpty(_workingDirectory))
        {
            await InitializeAsync(_command, _workingDirectory);
        }
    }

    public void Kill()
    {
        _readCts?.Cancel();
        _ptyService?.Kill();
    }

    #endregion

    #region Scrolling

    public void ScrollUp(int lines = 1)
    {
        var newOffset = Math.Min(_scrollOffset + lines, MaxScrollBack);
        if (newOffset != _scrollOffset)
        {
            _scrollOffset = newOffset;
            InvalidateVisual();
        }
    }

    public void ScrollDown(int lines = 1)
    {
        var newOffset = Math.Max(_scrollOffset - lines, 0);
        if (newOffset != _scrollOffset)
        {
            _scrollOffset = newOffset;
            InvalidateVisual();
        }
    }

    public void ScrollToBottom()
    {
        if (_scrollOffset != 0)
        {
            _scrollOffset = 0;
            InvalidateVisual();
        }
    }

    #endregion

    #region Rendering

    private List<LayoutRow> GetVisibleRows()
    {
        if (_viewPort == null)
            return new List<LayoutRow>();

        var startRow = Math.Max(0, TopRow - _scrollOffset);
        return _viewPort.GetPageSpans(startRow, _rows, _columns, _selection);
    }

    private TextPosition GetCursorPosition()
    {
        return _viewPort?.CursorPosition ?? new TextPosition { Column = 0, Row = 0 };
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(DefaultBackgroundBrush, new Rect(Bounds.Size));

        if (_terminalController == null || _viewPort == null)
            return;

        var rows = GetVisibleRows();
        var cursorPos = GetCursorPosition();
        var cursorScreenRow = cursorPos.Row;

        double y = 0;

        for (int rowIndex = 0; rowIndex < _rows && rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            double x = 0;

            if (row?.Spans != null)
            {
                foreach (var span in row.Spans)
                {
                    if (string.IsNullOrEmpty(span.Text))
                        continue;

                    var foreground = ParseColor(span.ForgroundColor, DefaultForeground);
                    var background = ParseColor(span.BackgroundColor, DefaultBackground);

                    if (background != DefaultBackground)
                    {
                        var spanWidth = span.Text.Length * _charWidth;
                        context.FillRectangle(
                            GetCachedBrush(background),
                            new Rect(x, y, spanWidth, _charHeight));
                    }

                    var fontStyle = span.Italic ? FontStyle.Italic : FontStyle.Normal;
                    var fontWeight = span.Bold ? FontWeight.Bold : FontWeight.Normal;
                    var typeface = new Typeface(_typeface.FontFamily, fontStyle, fontWeight);
                    var foregroundBrush = GetCachedBrush(foreground);

                    RenderTextWithFallback(context, span.Text, x, y, typeface, foregroundBrush);

                    if (span.Underline)
                    {
                        var underlineY = y + _charHeight - 2;
                        context.DrawLine(
                            new Pen(GetCachedBrush(foreground), 1),
                            new Point(x, underlineY),
                            new Point(x + span.Text.Length * _charWidth, underlineY));
                    }

                    x += span.Text.Length * _charWidth;
                }
            }

            // Draw cursor
            var shouldShowCursor = _cursorVisible && CursorVisible && !IsScrolledBack;
            if (shouldShowCursor && rowIndex == cursorScreenRow && IsProcessRunning)
            {
                var cursorX = cursorPos.Column * _charWidth;
                context.FillRectangle(
                    DefaultForegroundBrush,
                    new Rect(cursorX, y, Math.Max(_charWidth, 2), _charHeight));
            }

            y += _charHeight;
        }

        // Draw scroll indicator
        if (IsScrolledBack)
        {
            var indicatorText = $"↑ {_scrollOffset} lines";
            var formattedIndicator = new FormattedText(
                indicatorText,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                _typeface,
                _fontSize * 0.9,
                ScrollIndicatorTextBrush);

            var indicatorX = Bounds.Width - formattedIndicator.Width - 10;
            var indicatorY = 5;

            context.FillRectangle(
                ScrollIndicatorBackgroundBrush,
                new Rect(indicatorX - 5, indicatorY - 2, formattedIndicator.Width + 10, formattedIndicator.Height + 4));

            context.DrawText(formattedIndicator, new Point(indicatorX, indicatorY));
        }
    }

    private Color ParseColor(string? colorString, Color defaultColor)
    {
        if (string.IsNullOrEmpty(colorString))
            return defaultColor;

        if (ColorPalette.TryGetValue(colorString, out var paletteColor))
            return paletteColor;

        if (colorString.StartsWith("#"))
        {
            try
            {
                return Color.Parse(colorString);
            }
            catch
            {
            }
        }

        return defaultColor;
    }

    // Fallback font names in order of preference
    private static readonly string[] FallbackFontNames =
    {
        // Embedded Nerd Font (bundled with app) - for Powerline and icon glyphs
        "avares://host/Assets/Fonts#Symbols Nerd Font Mono",
        // Nerd Fonts (if installed) - have Powerline and icon glyphs used by Claude CLI
        "JetBrainsMono Nerd Font",
        "JetBrainsMono NF",
        "FiraCode Nerd Font",
        "Hack Nerd Font",
        "MesloLGS NF",
        "Symbols Nerd Font Mono",
        "Symbols Nerd Font",
        // Standard fonts
        "Apple Color Emoji",  // Emoji support
        "STIX Two Math",      // Mathematical symbols
        "Arial",              // Good for block elements
        "Arial Unicode MS",   // Wide Unicode coverage
        "Apple Symbols",      // macOS symbols
        "Menlo",              // Primary terminal font (retry in case it has the glyph)
        "Helvetica Neue",     // Common macOS font
        "LastResort",         // Ultimate fallback
    };

    // Cache for typeface/glyphTypeface pairs
    private static readonly Lazy<List<(Typeface typeface, IGlyphTypeface glyphTypeface)>> FallbackFonts = new(() =>
    {
        var result = new List<(Typeface, IGlyphTypeface)>();
        var fontManager = FontManager.Current;

        foreach (var fontName in FallbackFontNames)
        {
            try
            {
                var typeface = new Typeface(new FontFamily(fontName), FontStyle.Normal, FontWeight.Normal);
                if (fontManager.TryGetGlyphTypeface(typeface, out var glyphTypeface))
                {
                    result.Add((typeface, glyphTypeface!));
                }
            }
            catch
            {
                // Font not available, skip
            }
        }
        return result;
    });

    // Cache for character -> typeface mapping to avoid repeated lookups
    private static readonly Dictionary<char, Typeface?> CharacterTypefaceCache = new();

    private static bool NeedsFallbackFont(char c)
    {
        // Unicode blocks that Menlo doesn't support well
        if (c >= '\u2300' && c <= '\u23FF') return true; // Miscellaneous Technical
        if (c >= '\u25A0' && c <= '\u25FF') return true; // Geometric Shapes
        if (c >= '\u2600' && c <= '\u26FF') return true; // Miscellaneous Symbols
        if (c >= '\u2700' && c <= '\u27BF') return true; // Dingbats
        if (c >= '\u2800' && c <= '\u28FF') return true; // Braille Patterns
        if (c >= '\u2B00' && c <= '\u2BFF') return true; // Miscellaneous Symbols and Arrows
        if (c >= '\u2580' && c <= '\u259F') return true; // Block Elements
        if (c >= '\u2500' && c <= '\u257F') return true; // Box Drawing
        if (c >= '\u27F0' && c <= '\u27FF') return true; // Supplemental Arrows-A
        if (c >= '\uE000' && c <= '\uF8FF') return true; // Private Use Area (Nerd Fonts, etc.)
        if (char.IsHighSurrogate(c)) return true; // Surrogate pairs (emoji and other non-BMP characters)
        if (char.IsLowSurrogate(c)) return true; // Low surrogates are part of emoji pairs
        return false;
    }

    private static bool ContainsFallbackCharacters(string text)
    {
        foreach (var c in text)
        {
            if (NeedsFallbackFont(c)) return true;
        }
        return false;
    }

    /// <summary>
    /// Gets the best typeface for rendering a specific character by checking actual glyph availability.
    /// </summary>
    private Typeface GetTypefaceForCharacter(char c)
    {
        // Check cache first
        if (CharacterTypefaceCache.TryGetValue(c, out var cachedTypeface))
        {
            return cachedTypeface ?? _fallbackTypeface;
        }

        uint codepoint = c;

        // Try each fallback font to find one that has the glyph
        foreach (var (typeface, glyphTypeface) in FallbackFonts.Value)
        {
            try
            {
                if (glyphTypeface.TryGetGlyph(codepoint, out var glyphIndex) && glyphIndex != 0)
                {
                    CharacterTypefaceCache[c] = typeface;
                    return typeface;
                }
            }
            catch
            {
                // Skip this font if there's an error
            }
        }

        // No font found - cache null and return default fallback
        CharacterTypefaceCache[c] = null;
        return _fallbackTypeface;
    }

    private void RenderTextWithFallback(DrawingContext context, string text, double x, double y,
        Typeface primaryTypeface, IBrush foregroundBrush)
    {
        if (!ContainsFallbackCharacters(text))
        {
            // Fast path: no fallback needed, render entire string at once
            var formattedText = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                primaryTypeface,
                _fontSize,
                foregroundBrush);
            context.DrawText(formattedText, new Point(x, y));
            return;
        }

        // Slow path: render with font fallback support
        double currentX = x;
        int runStart = 0;
        bool currentRunNeedsFallback = NeedsFallbackFont(text[0]);

        for (int i = 1; i <= text.Length; i++)
        {
            bool needsFallback = i < text.Length && NeedsFallbackFont(text[i]);

            if (i == text.Length || needsFallback != currentRunNeedsFallback)
            {
                string run = text.Substring(runStart, i - runStart);

                if (currentRunNeedsFallback)
                {
                    // Render fallback characters one at a time with the best available font
                    foreach (char c in run)
                    {
                        var charTypeface = GetTypefaceForCharacter(c);
                        var formattedText = new FormattedText(
                            c.ToString(),
                            CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            charTypeface,
                            _fontSize,
                            foregroundBrush);
                        context.DrawText(formattedText, new Point(currentX, y));
                        currentX += _charWidth;
                    }
                }
                else
                {
                    // Render normal text run
                    var formattedText = new FormattedText(
                        run,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        primaryTypeface,
                        _fontSize,
                        foregroundBrush);
                    context.DrawText(formattedText, new Point(currentX, y));
                    currentX += run.Length * _charWidth;
                }

                runStart = i;
                currentRunNeedsFallback = needsFallback;
            }
        }
    }

    #endregion

    #region Input Handling

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);

        if (!string.IsNullOrEmpty(e.Text))
        {
            ClearSelection();
            if (IsScrolledBack)
            {
                ScrollToBottom();
            }
            WriteToTerminal(e.Text);
            e.Handled = true;
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var lines = (int)(e.Delta.Y * 3);
        if (lines > 0)
        {
            ScrollUp(lines);
        }
        else if (lines < 0)
        {
            ScrollDown(-lines);
        }

        e.Handled = true;
    }

    private TextPosition? ScreenToBufferPosition(Point screenPos)
    {
        var column = Math.Max(0, (int)(screenPos.X / _charWidth));
        var row = Math.Max(0, (int)(screenPos.Y / _charHeight));

        column = Math.Min(column, _columns - 1);
        row = Math.Min(row, _rows - 1);

        var bufferRow = row + TopRow - _scrollOffset;

        return new TextPosition { Column = column, Row = bufferRow };
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();

        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                // Ctrl+Click for link detection
                var col = (int)(point.Position.X / _charWidth);
                var row = (int)(point.Position.Y / _charHeight);
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
                var bufferPos = ScreenToBufferPosition(point.Position);
                if (bufferPos != null)
                {
                    _isSelecting = true;
                    _selectionStart = bufferPos;
                    ClearSelection();
                    e.Pointer.Capture(this);
                    e.Handled = true;
                }
            }
        }
        else if (point.Properties.IsRightButtonPressed)
        {
            var col = (int)(point.Position.X / _charWidth);
            var row = (int)(point.Position.Y / _charHeight);
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

        if (!_isSelecting || _selectionStart == null)
            return;

        var bufferPos = ScreenToBufferPosition(e.GetPosition(this));
        if (bufferPos != null)
        {
            SetSelection(_selectionStart, bufferPos);
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_isSelecting)
        {
            _isSelecting = false;
            e.Pointer.Capture(null);

            if (_selectionStart != null)
            {
                var bufferPos = ScreenToBufferPosition(e.GetPosition(this));
                if (bufferPos != null)
                {
                    if (_selectionStart.Column == bufferPos.Column && _selectionStart.Row == bufferPos.Row)
                    {
                        ClearSelection();
                    }
                    else
                    {
                        SetSelection(_selectionStart, bufferPos);
                    }
                }
            }

            _selectionStart = null;
            e.Handled = true;
        }
    }

    private void SetSelection(TextPosition start, TextPosition end)
    {
        _selection = new TextRange { Start = start, End = end };
        InvalidateVisual();
    }

    private void ClearSelection()
    {
        if (_selection != null)
        {
            _selection = null;
            InvalidateVisual();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var meta = e.KeyModifiers.HasFlag(KeyModifiers.Meta);

        // Cmd+C (macOS) or Ctrl+Shift+C to copy
        if ((meta && e.Key == Key.C) || (control && shift && e.Key == Key.C))
        {
            if (_selection != null)
            {
                _ = CopySelectionToClipboardAsync();
                e.Handled = true;
                return;
            }
        }

        // Cmd+V (macOS) or Ctrl+Shift+V to paste
        if ((meta && e.Key == Key.V) || (control && shift && e.Key == Key.V))
        {
            _ = PasteFromClipboardAsync();
            e.Handled = true;
            return;
        }

        // Shift+PageUp/PageDown for scrollback
        if (shift && e.Key == Key.PageUp)
        {
            ScrollUp(_rows - 1);
            e.Handled = true;
            return;
        }
        if (shift && e.Key == Key.PageDown)
        {
            ScrollDown(_rows - 1);
            e.Handled = true;
            return;
        }
        if (shift && e.Key == Key.Home)
        {
            ScrollUp(MaxScrollBack);
            e.Handled = true;
            return;
        }
        if (shift && e.Key == Key.End)
        {
            ScrollToBottom();
            e.Handled = true;
            return;
        }

        string? sequence = GetKeySequence(e.Key, control, shift);
        if (sequence != null)
        {
            ClearSelection();
            if (IsScrolledBack)
            {
                ScrollToBottom();
            }
            WriteToTerminal(sequence);
            e.Handled = true;
            return;
        }

        if (control && e.Key >= Key.A && e.Key <= Key.Z)
        {
            ClearSelection();
            if (IsScrolledBack)
            {
                ScrollToBottom();
            }
            var ctrlChar = (char)(e.Key - Key.A + 1);
            WriteToTerminal(ctrlChar.ToString());
            e.Handled = true;
        }
    }

    private string? GetKeySequence(Key key, bool control, bool shift)
    {
        var appMode = _terminalController?.CursorState.ApplicationCursorKeysMode ?? false;

        return key switch
        {
            Key.Up => appMode ? "\x1bOA" : "\x1b[A",
            Key.Down => appMode ? "\x1bOB" : "\x1b[B",
            Key.Right => appMode ? "\x1bOC" : "\x1b[C",
            Key.Left => appMode ? "\x1bOD" : "\x1b[D",
            Key.Home => "\x1b[H",
            Key.End => "\x1b[F",
            Key.PageUp => "\x1b[5~",
            Key.PageDown => "\x1b[6~",
            Key.Insert => "\x1b[2~",
            Key.Delete => "\x1b[3~",
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
            Key.Back => "\x7f",
            Key.Tab => shift ? "\x1b[Z" : "\t",
            Key.Escape => "\x1b",
            Key.Enter => "\r",
            _ => null
        };
    }

    private async Task CopySelectionToClipboardAsync()
    {
        if (_selection == null)
            return;

        var selectedText = GetSelectedText();
        if (string.IsNullOrEmpty(selectedText))
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard != null)
        {
            await topLevel.Clipboard.SetTextAsync(selectedText);
        }
    }

    private async Task PasteFromClipboardAsync()
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard == null)
            return;

        var text = await topLevel.Clipboard.GetTextAsync();
        if (!string.IsNullOrEmpty(text))
        {
            ClearSelection();
            if (IsScrolledBack)
            {
                ScrollToBottom();
            }
            WriteToTerminal(text);
        }
    }

    #endregion

    #region Resizing

    protected override Size MeasureOverride(Size availableSize)
    {
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        UpdateTerminalSize(finalSize);
        return finalSize;
    }

    private void UpdateTerminalSize(Size size)
    {
        if (_charWidth <= 0 || _charHeight <= 0)
            return;

        if (size.Width <= 0 || size.Height <= 0)
            return;

        var columns = Math.Max(1, (int)(size.Width / _charWidth));
        var rows = Math.Max(1, (int)(size.Height / _charHeight));

        if (columns != _columns || rows != _rows)
        {
            _columns = columns;
            _rows = rows;

            if (_terminalController != null)
            {
                _terminalController.Columns = columns;
                _terminalController.Rows = rows;
                _terminalController.VisibleColumns = columns;
                _terminalController.VisibleRows = rows;
            }

            _ptyService?.Resize(columns, rows);

            // Force a redraw after resize
            InvalidateVisual();
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopCursorBlink();
        _readCts?.Cancel();
        _readCts?.Dispose();
        _ptyService?.Dispose();
    }

    #endregion
}
