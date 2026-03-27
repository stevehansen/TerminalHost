// Suppress nullable warnings caused by VtNetCore types lacking proper annotations
#pragma warning disable CS8604 // Possible null reference argument (VtNetCore operators)
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type (VtNetCore comparisons)
#pragma warning disable CS8601 // Possible null reference assignment (VtNetCore types)

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Domain;
#if MACOS
using TerminalHost.macOS.Services;
#elif LINUX
using TerminalHost.Linux.Services;
#endif
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
    private bool _needsInitialResize;

    // Terminal dimensions
    private int _columns = 80;
    private int _rows = 24;

    // Font settings
    private double _charWidth = 8;
    private double _charHeight = 16;
    private double _baselineOffset;
    private Typeface _typeface;
    private Typeface _fallbackTypeface;
    private IGlyphTypeface? _primaryGlyphTypeface;
    private double _fontSize = 14;

    // GlyphTypeface cache for GlyphRun rendering (maps Typeface variants to their IGlyphTypeface)
    private readonly Dictionary<Typeface, IGlyphTypeface?> _glyphTypefaceCache = new();

    // Emoji overlay system - TextBlocks for emoji since DrawingContext doesn't support emoji fallback
    private readonly List<TextBlock> _emojiOverlays = new();
    private readonly List<(double x, double y, string emoji, IBrush foreground)> _pendingEmojis = new();
    private int _lastEmojiHash; // Track if emoji state changed to avoid unnecessary layout updates

    // Intel Mac detection - Intel Macs have significant performance issues with complex rendering
    private static readonly bool IsIntelMac = DetectIntelMac();

    private static bool DetectIntelMac()
    {
        if (!OperatingSystem.IsMacOS())
            return false;

        // Check if running on Intel (x64) vs Apple Silicon (arm64)
        return System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==
               System.Runtime.InteropServices.Architecture.X64;
    }

    // Cursor blinking
    private bool _cursorVisible = true;
    private DispatcherTimer? _cursorTimer;

    // Selection state
    private bool _isSelecting;
    private TextPosition? _selectionStart;
    private VtNetCore.VirtualTerminal.TextRange? _selection;

    // Scroll state
    private int _scrollOffset;

    // Restart info
    private string? _command;
    private string? _workingDirectory;
    private IEnumerable<string>? _customPaths;

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
    private const int MaxBrushCacheSize = 512; // Limit cache to prevent unbounded growth with true color

    // Render throttling - adjust based on platform for optimal performance
    // Intel Mac needs lower frame rate due to Rosetta overhead
    private bool _renderPending;
    private DateTime _lastRenderRequest;
    private static readonly TimeSpan RenderThrottleInterval = TimeSpan.FromMilliseconds(IsIntelMac ? 50 : 33);

    // Typeface cache to avoid creating new Typeface objects on every render
    private Typeface? _cachedNormalTypeface;
    private Typeface? _cachedBoldTypeface;
    private Typeface? _cachedItalicTypeface;
    private Typeface? _cachedBoldItalicTypeface;

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
    public event EventHandler? Resized;

    #endregion

    public MacTerminalControl()
    {
        // On Intel Mac, use Menlo for better performance (no embedded font loading, no Rosetta overhead)
        // On Apple Silicon, use Cascadia Code NF (Nerd Font with icons built-in)
        // On Linux, use Cascadia Code NF with Linux-appropriate fallback fonts
        if (IsIntelMac)
        {
            _typeface = new Typeface(new FontFamily("Menlo"), FontStyle.Normal, FontWeight.Normal);
            _fallbackTypeface = new Typeface(new FontFamily("Apple Symbols, Arial Unicode MS, LastResort"), FontStyle.Normal, FontWeight.Normal);
        }
        else if (OperatingSystem.IsLinux())
        {
            _typeface = new Typeface(new FontFamily("Cascadia Code NF, avares://host/Assets/Fonts#Cascadia Code NF"), FontStyle.Normal, FontWeight.Normal);
            _fallbackTypeface = new Typeface(new FontFamily("DejaVu Sans Mono, Noto Sans Mono, Liberation Mono, monospace"), FontStyle.Normal, FontWeight.Normal);
        }
        else
        {
            _typeface = new Typeface(new FontFamily("Cascadia Code NF, avares://host/Assets/Fonts#Cascadia Code NF"), FontStyle.Normal, FontWeight.Normal);
            _fallbackTypeface = new Typeface(new FontFamily("STIX Two Math, Arial, Apple Symbols, Arial Unicode MS, LastResort"), FontStyle.Normal, FontWeight.Normal);
        }

        // Get glyph typeface for the primary font to check glyph availability
        FontManager.Current.TryGetGlyphTypeface(_typeface, out _primaryGlyphTypeface);

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
            // Prevent unbounded cache growth - clear when too large
            // This is a simple strategy; a proper LRU would be more sophisticated
            if (BrushCache.Count >= MaxBrushCacheSize)
            {
                BrushCache.Clear();
            }
            brush = new SolidColorBrush(color);
            BrushCache[color] = brush;
        }
        return brush;
    }

    /// <summary>
    /// Gets a cached Typeface for the given style, avoiding allocation on every render.
    /// </summary>
    private Typeface GetCachedTypeface(bool bold, bool italic)
    {
        if (bold && italic)
        {
            return _cachedBoldItalicTypeface ??= new Typeface(_typeface.FontFamily, FontStyle.Italic, FontWeight.Bold);
        }
        if (bold)
        {
            return _cachedBoldTypeface ??= new Typeface(_typeface.FontFamily, FontStyle.Normal, FontWeight.Bold);
        }
        if (italic)
        {
            return _cachedItalicTypeface ??= new Typeface(_typeface.FontFamily, FontStyle.Italic, FontWeight.Normal);
        }
        return _cachedNormalTypeface ??= _typeface;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        StartCursorBlink();

        // Schedule a render after the control is fully attached and sized
        Dispatcher.UIThread.Post(() =>
        {
            // Force a resize update in case the control was initialized before being attached
            // This ensures the PTY gets the correct size when the Run terminal is shown lazily
            if (Bounds.Width > 0 && Bounds.Height > 0)
            {
                UpdateTerminalSize(Bounds.Size);
            }
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

        // Use the font's actual glyph advance for accurate character width.
        // FormattedText.Width returns the ink/bounding width which can be narrower than the
        // advance width, causing column count to be too high and right-edge text to be clipped.
        if (_primaryGlyphTypeface != null
            && _primaryGlyphTypeface.TryGetGlyph('M', out var glyphIndex))
        {
            var metrics = _primaryGlyphTypeface.Metrics;
            if (metrics.DesignEmHeight > 0)
            {
                _charWidth = _primaryGlyphTypeface.GetGlyphAdvance(glyphIndex)
                    * _fontSize / metrics.DesignEmHeight;
            }
        }

        if (_charWidth <= 0)
            _charWidth = formattedText.Width;

        _charHeight = formattedText.Height;
        _baselineOffset = formattedText.Baseline;

        // Menlo has tighter line spacing than Cascadia Code NF
        // Add padding to match expected terminal line height
        if (IsIntelMac)
        {
            _charHeight = Math.Ceiling(_charHeight * 1.15); // ~15% increase for proper line spacing
        }

        if (_charWidth <= 0) _charWidth = 8;
        if (_charHeight <= 0) _charHeight = 16;
    }

    /// <summary>
    /// Initialize the terminal with a PTY process.
    /// </summary>
    public async Task InitializeAsync(string command, string workingDirectory, IEnumerable<string>? customPaths = null)
    {
        _command = command;
        _workingDirectory = workingDirectory;
        _customPaths = customPaths;

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

        // Subscribe to SendData event to handle terminal responses (e.g., cursor position reports)
        _terminalController.SendData += OnTerminalSendData;

        _dataConsumer = new DataConsumer(_terminalController);
        _viewPort = new VirtualTerminalViewPort(_terminalController);

        // If the control hasn't been laid out yet (Bounds are 0), defer PTY start until
        // the first ArrangeOverride gives us actual dimensions. PtySharp starts the shell
        // instantly (unlike the old Python helper which had startup latency), so starting
        // at wrong dimensions causes the shell to flood output that scrolls past the viewport.
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            _needsInitialResize = true;
            _pendingPtyStart = true;
        }
        else
        {
            await StartPtyAsync();
        }

        Loaded?.Invoke(this, EventArgs.Empty);

        // Trigger initial render - schedule it for when the control might be in the visual tree
        InvalidateVisual();
        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
    }

    private bool _pendingPtyStart;

    private async Task StartPtyAsync()
    {
        if (_ptyService != null)
            return; // Already started

        // Create PTY service
#if MACOS
        _ptyService = new MacPtyService();
#elif LINUX
        _ptyService = new LinuxPtyService();
#endif
        _ptyService.ProcessExited += OnProcessExited;

        await _ptyService.StartAsync(_columns, _rows, _workingDirectory, _command, _customPaths);

        // Start reading from PTY
        _readCts = new CancellationTokenSource();
        _ = ReadOutputAsync(_readCts.Token);
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

                // Process through VtNetCore - use overload to avoid array copy
                _dataConsumer?.Push(buffer, 0, bytesRead);

                // Notify listeners only if there are subscribers (avoid string creation otherwise)
                if (OutputReceived != null)
                {
                    var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    OutputReceived.Invoke(text);
                }

                // Request redraw
                RequestRender();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
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

    private void OnTerminalSendData(object? sender, SendDataEventArgs e)
    {
        // Forward terminal responses (like cursor position reports) back to the PTY
        // Write synchronously to ensure immediate response for CLI tools like codex
        if (_ptyService != null && _ptyService.IsRunning && e.Data != null && _ptyService.WriterStream != null)
        {
            try
            {
                _ptyService.WriterStream.Write(e.Data, 0, e.Data.Length);
                _ptyService.WriterStream.Flush();
            }
            catch
            {
                // Ignore write errors - PTY may have closed
            }
        }
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
            await InitializeAsync(_command, _workingDirectory, _customPaths);
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
        // Clear pending emojis from previous render
        _pendingEmojis.Clear();

        context.FillRectangle(DefaultBackgroundBrush, new Rect(Bounds.Size));

        if (_terminalController == null || _viewPort == null)
        {
            Dispatcher.UIThread.Post(UpdateEmojiOverlays, DispatcherPriority.Render);
            return;
        }

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

                    var typeface = GetCachedTypeface(span.Bold, span.Italic);
                    var foregroundBrush = GetCachedBrush(foreground);

                    // RenderTextWithFallback handles emoji detection and adds to _pendingEmojis
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
            var indicatorText = $"Γåæ {_scrollOffset} lines";
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

        // Schedule emoji overlay update after render pass completes
        // (Can't modify controls during Render - causes InvalidOperationException)
        Dispatcher.UIThread.Post(UpdateEmojiOverlays, DispatcherPriority.Render);
    }

    /// <summary>
    /// Updates TextBlock overlays for emoji rendering.
    /// TextBlock correctly handles emoji font fallback, while DrawingContext doesn't.
    /// Must be called after render pass, not during.
    /// </summary>
    private void UpdateEmojiOverlays()
    {
        // On Intel Mac, skip emoji overlays entirely for performance
        // (Emoji will show as tofu/placeholder but terminal remains responsive)
        if (IsIntelMac)
        {
            ClearAllEmojiOverlays();
            return;
        }

        // Compute a hash to detect if emoji state actually changed
        var currentHash = ComputeEmojiHash();
        if (currentHash == _lastEmojiHash && _pendingEmojis.Count == _emojiOverlays.Count)
            return;
        _lastEmojiHash = currentHash;

        // Skip if no changes needed
        if (_pendingEmojis.Count == 0 && _emojiOverlays.Count == 0)
            return;

        bool needsLayoutUpdate = false;

        // Remove excess overlays if we have fewer emojis now
        while (_emojiOverlays.Count > _pendingEmojis.Count)
        {
            var overlay = _emojiOverlays[^1];
            _emojiOverlays.RemoveAt(_emojiOverlays.Count - 1);
            VisualChildren.Remove(overlay);
            LogicalChildren.Remove(overlay);
            needsLayoutUpdate = true;
        }

        // Update existing overlays and create new ones as needed
        for (int i = 0; i < _pendingEmojis.Count; i++)
        {
            var (x, y, emoji, foreground) = _pendingEmojis[i];

            TextBlock overlay;
            if (i < _emojiOverlays.Count)
            {
                // Reuse existing overlay - only update if changed
                overlay = _emojiOverlays[i];
                if (overlay.Text != emoji)
                {
                    overlay.Text = emoji;
                }
                if (overlay.Foreground != foreground)
                {
                    overlay.Foreground = foreground;
                }
            }
            else
            {
                // Create new overlay
                overlay = new TextBlock
                {
                    FontSize = _fontSize,
                    Text = emoji,
                    Foreground = foreground
                };
                _emojiOverlays.Add(overlay);
                VisualChildren.Add(overlay);
                LogicalChildren.Add(overlay);
                needsLayoutUpdate = true;
            }

            // Position the overlay using RenderTransform for absolute positioning
            overlay.RenderTransform = new TranslateTransform(x, y);
        }

        // Only force layout update if overlays were added/removed
        if (needsLayoutUpdate)
        {
            InvalidateMeasure();
            InvalidateArrange();
        }
    }

    private int ComputeEmojiHash()
    {
        if (_pendingEmojis.Count == 0)
            return 0;

        unchecked
        {
            int hash = 17;
            foreach (var (x, y, emoji, _) in _pendingEmojis)
            {
                hash = hash * 31 + x.GetHashCode();
                hash = hash * 31 + y.GetHashCode();
                hash = hash * 31 + emoji.GetHashCode();
            }
            return hash;
        }
    }

    private void ClearAllEmojiOverlays()
    {
        if (_emojiOverlays.Count == 0)
            return;

        foreach (var overlay in _emojiOverlays)
        {
            VisualChildren.Remove(overlay);
            LogicalChildren.Remove(overlay);
        }
        _emojiOverlays.Clear();
    }

    // Cache for parsed hex colors to avoid repeated parsing
    private static readonly Dictionary<string, Color> ParsedColorCache = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxParsedColorCacheSize = 256;

    private Color ParseColor(string? colorString, Color defaultColor)
    {
        if (string.IsNullOrEmpty(colorString))
            return defaultColor;

        if (ColorPalette.TryGetValue(colorString, out var paletteColor))
            return paletteColor;

        if (colorString.StartsWith("#"))
        {
            // Check cache first
            if (ParsedColorCache.TryGetValue(colorString, out var cachedColor))
                return cachedColor;

            try
            {
                var color = Color.Parse(colorString);
                // Cache the result
                if (ParsedColorCache.Count >= MaxParsedColorCacheSize)
                {
                    ParsedColorCache.Clear();
                }
                ParsedColorCache[colorString] = color;
                return color;
            }
            catch
            {
            }
        }

        return defaultColor;
    }

    // Fallback font names in order of preference
    // Platform-specific: Intel Mac uses minimal list, Linux uses Linux fonts, Apple Silicon uses full macOS list
    private static readonly string[] FallbackFontNames = GetFallbackFontNames();

    private static string[] GetFallbackFontNames()
    {
        if (IsIntelMac)
        {
            return new[]
            {
                // Minimal list for Intel Mac - just the essentials
                "Menlo",              // Primary terminal font
                "Apple Symbols",      // macOS symbols
                "LastResort",         // Ultimate fallback
            };
        }

        if (OperatingSystem.IsLinux())
        {
            return new[]
            {
                // Nerd Fonts FIRST - required for CLI icons (Claude logo, spinners, etc.)
                "avares://host/Assets/Fonts#Symbols Nerd Font Mono", // Embedded Nerd Font (bundled with app)
                "Cascadia Code NF",        // Bundled Nerd Font
                "JetBrainsMono Nerd Font", // Popular Nerd Font
                "JetBrainsMono NF",
                "FiraCode Nerd Font",      // Popular Nerd Font
                "Hack Nerd Font",          // Popular Nerd Font
                "MesloLGS NF",
                "Symbols Nerd Font Mono",
                "Symbols Nerd Font",
                // Linux monospace fonts - for Unicode characters Nerd Fonts don't cover
                "DejaVu Sans Mono",        // Standard Linux font with good Unicode coverage
                "Liberation Mono",         // Common on RHEL/Fedora
                "Ubuntu Mono",             // Ubuntu default
                "Noto Sans Mono",          // Google Noto family
                "Droid Sans Mono",         // Android/Linux
                "Fira Mono",              // Mozilla font
                "Source Code Pro",         // Adobe font
                "Inconsolata",            // Popular monospace
                // Emoji and symbol fonts
                "Noto Color Emoji",       // Emoji support on Linux
                "Segoe UI Emoji",         // Emoji fallback
                "Symbola",                // Wide Unicode coverage
                "DejaVu Math TeX Gyre",   // Mathematical symbols
                "STIX Two Math",          // Mathematical symbols
                "DejaVu Sans",            // General Unicode fallback
                "monospace",              // Generic fallback
            };
        }

        // Apple Silicon (macOS default)
        return new[]
        {
            // Nerd Fonts FIRST - required for CLI icons (Claude logo, spinners, etc.)
            // These must come before standard fonts to ensure Private Use Area glyphs render correctly
            "avares://host/Assets/Fonts#Symbols Nerd Font Mono", // Embedded Nerd Font (bundled with app)
            "JetBrainsMono Nerd Font",
            "JetBrainsMono NF",
            "FiraCode Nerd Font",
            "Hack Nerd Font",
            "MesloLGS NF",
            "Symbols Nerd Font Mono",
            "Symbols Nerd Font",
            // macOS monospace fonts - for Unicode characters Nerd Fonts don't cover
            "SF Mono",            // Apple's modern monospace font with good Unicode coverage
            "Monaco",             // Classic macOS monospace
            // Standard fonts
            "Apple Color Emoji",  // Emoji support
            "STIX Two Math",      // Mathematical symbols
            "Arial Unicode MS",   // Wide Unicode coverage
            "Apple Symbols",      // macOS symbols
            "Menlo",              // Fallback terminal font
            "LastResort",         // Ultimate fallback
        };
    }

    // Cache for typeface/glyphTypeface pairs - initialized lazily
    private static List<(Typeface typeface, IGlyphTypeface glyphTypeface)>? _fallbackFontsCache;
    private static readonly object _fallbackFontsLock = new();

    private static List<(Typeface typeface, IGlyphTypeface glyphTypeface)> FallbackFonts
    {
        get
        {
            if (_fallbackFontsCache != null)
                return _fallbackFontsCache;

            lock (_fallbackFontsLock)
            {
                if (_fallbackFontsCache != null)
                    return _fallbackFontsCache;

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

                _fallbackFontsCache = result;
                return result;
            }
        }
    }

    // Cache for character -> typeface mapping to avoid repeated lookups
    private static readonly Dictionary<char, Typeface?> CharacterTypefaceCache = new();
    private const int MaxCharacterTypefaceCacheSize = 1024; // Limit cache size

    private bool NeedsFallbackFont(char c)
    {
        if (c < '\u0080') return false; // ASCII - always in primary font
        if (c >= '\uE000' && c <= '\uF8FF') return true; // Private Use Area (Nerd Fonts icons)
        if (char.IsHighSurrogate(c)) return true; // Emoji and other non-BMP characters
        if (char.IsLowSurrogate(c)) return true;

        // For other non-ASCII characters (symbols, dingbats, etc.), check if the
        // primary font actually has the glyph. Characters like ❄ (U+2744) and
        // variation selectors (U+FE0F) need fallback when the primary font lacks them.
        if (_primaryGlyphTypeface != null)
        {
            if (!_primaryGlyphTypeface.TryGetGlyph((uint)c, out var gi) || gi == 0)
                return true;
        }

        return false;
    }

    private bool ContainsFallbackCharacters(string text)
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

        // Prevent unbounded cache growth
        if (CharacterTypefaceCache.Count >= MaxCharacterTypefaceCacheSize)
        {
            CharacterTypefaceCache.Clear();
        }

        uint codepoint = c;

        // Always check primary font first (Cascadia Code NF has Nerd Font glyphs built-in)
        // This also handles box-drawing and block element characters with correct cell alignment.
        if (_primaryGlyphTypeface != null)
        {
            try
            {
                if (_primaryGlyphTypeface.TryGetGlyph(codepoint, out var glyphIndex) && glyphIndex != 0)
                {
                    CharacterTypefaceCache[c] = _typeface;
                    return _typeface;
                }
            }
            catch
            {
                // Continue to fallback fonts
            }
        }

        // Try each fallback font to find one that has the glyph
        foreach (var (typeface, glyphTypeface) in FallbackFonts)
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

    /// <summary>
    /// Quick check if text is pure ASCII (no special characters needed).
    /// </summary>
    private static bool IsPureAscii(string text)
    {
        foreach (var c in text)
        {
            if (c >= 0x80) return false;
        }
        return true;
    }

    /// <summary>
    /// Gets or creates a cached IGlyphTypeface for the given Typeface.
    /// </summary>
    private IGlyphTypeface? GetCachedGlyphTypeface(Typeface typeface)
    {
        if (!_glyphTypefaceCache.TryGetValue(typeface, out var glyphTypeface))
        {
            FontManager.Current.TryGetGlyphTypeface(typeface, out glyphTypeface);
            _glyphTypefaceCache[typeface] = glyphTypeface;
        }
        return glyphTypeface;
    }

    /// <summary>
    /// Renders text using GlyphRun with explicit advance widths set to _charWidth.
    /// This ensures every glyph is positioned at its exact grid cell, preventing
    /// accumulated drift from font-engine glyph positioning.
    /// </summary>
    private bool TryRenderWithGlyphRun(DrawingContext context, string text, double x, double y,
        Typeface typeface, IBrush foregroundBrush)
    {
        var glyphTypeface = GetCachedGlyphTypeface(typeface);
        if (glyphTypeface == null)
            return false;

        var glyphInfos = new GlyphInfo[text.Length];
        for (int i = 0; i < text.Length; i++)
        {
            if (!glyphTypeface.TryGetGlyph((uint)text[i], out var gi) || gi == 0)
            {
                // Font doesn't have this glyph - bail out so caller uses fallback path
                if (text[i] >= ' ')
                    return false;
                gi = 0;
            }
            glyphInfos[i] = new GlyphInfo(gi, i, _charWidth, default);
        }

        var glyphRun = new GlyphRun(
            glyphTypeface,
            _fontSize,
            text.AsMemory(),
            glyphInfos,
            new Point(x, y + _baselineOffset));

        context.DrawGlyphRun(foregroundBrush, glyphRun);
        return true;
    }

    private void RenderTextWithFallback(DrawingContext context, string text, double x, double y,
        Typeface primaryTypeface, IBrush foregroundBrush)
    {
        // On Intel Mac, use the simplest possible rendering path for performance
        // This sacrifices emoji and nerd font icons but keeps the terminal responsive
        if (IsIntelMac)
        {
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

        // Grid-aligned rendering: use GlyphRun with explicit _charWidth advances
        // to prevent accumulated drift from font-engine glyph positioning.
        // This ensures text at the right edge of the terminal isn't clipped.
        if (!ContainsFallbackCharacters(text))
        {
            if (TryRenderWithGlyphRun(context, text, x, y, primaryTypeface, foregroundBrush))
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
                    // Render fallback characters with proper handling for surrogate pairs (emoji)
                    // and single-char icons (Nerd Font Private Use Area)
                    for (int j = 0; j < run.Length; j++)
                    {
                        char c = run[j];

                        if (char.IsHighSurrogate(c) && j + 1 < run.Length && char.IsLowSurrogate(run[j + 1]))
                        {
                            // Surrogate pair (emoji) - collect for TextBlock overlay
                            // TextBlock renders emoji correctly, DrawingContext doesn't
                            var emojiStr = run.Substring(j, 2);
                            j++; // Skip the low surrogate

                            // Add to pending emojis for overlay creation after render
                            _pendingEmojis.Add((currentX, y, emojiStr, foregroundBrush));
                            currentX += _charWidth * 2; // Emoji = 2 cells
                        }
                        else if (char.IsHighSurrogate(c))
                        {
                            // Orphaned high surrogate - look ahead in the NEXT character position
                            // This handles the case where the pair is split across runs
                            // Just skip it here and collect it with the span-level detection
                            currentX += _charWidth;
                        }
                        else if (char.IsLowSurrogate(c))
                        {
                            // Orphaned low surrogate - skip, it was part of a pair
                            currentX += _charWidth;
                        }
                        else
                        {
                            // Single character (Nerd Font icon or other)
                            var charStr = c.ToString();
                            var charTypeface = GetTypefaceForCharacter(c);

                            var formattedText = new FormattedText(
                                charStr,
                                CultureInfo.CurrentCulture,
                                FlowDirection.LeftToRight,
                                charTypeface,
                                _fontSize,
                                foregroundBrush);
                            context.DrawText(formattedText, new Point(currentX, y));
                            currentX += _charWidth;
                        }
                    }
                }
                else
                {
                    // Render normal text run with grid-aligned GlyphRun
                    if (!TryRenderWithGlyphRun(context, run, currentX, y, primaryTypeface, foregroundBrush))
                    {
                        var textLayout = new TextLayout(
                            run,
                            primaryTypeface,
                            _fontSize,
                            foregroundBrush,
                            TextAlignment.Left);
                        textLayout.Draw(context, new Point(currentX, y));
                    }
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
        _selection = new VtNetCore.VirtualTerminal.TextRange { Start = start, End = end };
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

        // Skip if already handled by window-level shortcuts
        if (e.Handled)
            return;

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

#pragma warning disable CS0618 // IClipboard.GetTextAsync is obsolete in newer Avalonia
        var text = await topLevel.Clipboard.GetTextAsync();
#pragma warning restore CS0618
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
        // Measure all visual children (emoji overlays)
        foreach (var child in VisualChildren)
        {
            if (child is Control control)
            {
                control.Measure(availableSize);
            }
        }
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Arrange all visual children (emoji overlays)
        foreach (var child in VisualChildren)
        {
            if (child is Control control)
            {
                control.Arrange(new Rect(finalSize));
            }
        }

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

        // Force resize through when the PTY was started before the control was sized
        // (common on startup with default 80x24). This ensures the PTY and terminal
        // controller get the correct dimensions even if they happen to match the defaults.
        var forceResize = _needsInitialResize;
        _needsInitialResize = false;

        if (forceResize || columns != _columns || rows != _rows)
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

            // If PTY start was deferred until we had real dimensions, start it now
            if (_pendingPtyStart)
            {
                _pendingPtyStart = false;
                _ = StartPtyAsync();
            }
            else
            {
                _ptyService?.Resize(columns, rows);
            }

            // Force a redraw after resize
            InvalidateVisual();

            // Notify listeners of resize
            Resized?.Invoke(this, EventArgs.Empty);
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Stop the cursor timer first
        StopCursorBlink();

        // Cancel and dispose the read task
        _readCts?.Cancel();
        _readCts?.Dispose();
        _readCts = null;

        // Unsubscribe from PTY events before disposing
        if (_ptyService != null)
        {
            _ptyService.ProcessExited -= OnProcessExited;
            _ptyService.Dispose();
            _ptyService = null;
        }

        // Unsubscribe from terminal controller events
        if (_terminalController != null)
        {
            _terminalController.SendData -= OnTerminalSendData;
            _terminalController = null;
        }

        // Clear other references
        _dataConsumer = null;
        _viewPort = null;
    }

    #endregion
}