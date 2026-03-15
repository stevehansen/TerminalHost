using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using EasyWindowsTerminalControl;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;

namespace TerminalHost.Domain;

public class TerminalSession : IDisposable
{
    public Guid Id { get; }
    public Profile Profile { get; }
    public SessionState State { get; private set; }
    public int? ExitCode { get; private set; }
    public ContentControl? TerminalControl { get; set; }

    private EasyTerminalControl? _easyTerminalControl;

    // Activity tracking
    private DateTime? _lastOutputTime;
    private bool _wasActive;

    // Terminal title tracking (parsed from OSC escape sequences)
    private string _terminalTitle = string.Empty;
    private readonly StringBuilder _oscBuffer = new();
    private bool _parsingOsc;

    // Output buffer for link detection (circular buffer of recent lines)
    private readonly StringBuilder _outputBuffer = new();
    private const int MaxOutputBufferSize = 50000; // ~50KB of recent output

    private readonly IStatisticsService _statisticsService;
    private readonly string _workingDirectory;
    private readonly string _terminalType;

    /// <summary>
    /// The last time output was received from the terminal.
    /// </summary>
    public DateTime? LastOutputTime => _lastOutputTime;

    /// <summary>
    /// Returns true if the terminal has produced output within the last 2 seconds.
    /// </summary>
    public bool IsActive => _lastOutputTime.HasValue &&
        (DateTime.Now - _lastOutputTime.Value).TotalSeconds < 2;

    public event EventHandler<int>? ProcessExited;

    /// <summary>
    /// Fired when the terminal transitions from idle to active or vice versa.
    /// </summary>
    public event EventHandler? ActivityChanged;

    /// <summary>
    /// The current terminal title (parsed from OSC escape sequences).
    /// </summary>
    public string TerminalTitle => _terminalTitle;

    /// <summary>
    /// Fired when the terminal title changes.
    /// </summary>
    public event EventHandler<string>? TitleChanged;

    /// <summary>
    /// Fired when a Ctrl+Click is detected and a link should be opened.
    /// The string parameter is the text that was clicked (for link detection).
    /// </summary>
    public event EventHandler<string>? LinkClicked;

    /// <summary>
    /// Fired when output is received from the terminal.
    /// The string parameter is the output text (may contain ANSI escape sequences).
    /// </summary>
    public event EventHandler<string>? OutputReceived;

    public TerminalSession(Profile profile, IStatisticsService statisticsService, string terminalType)
    {
        Id = Guid.NewGuid();
        Profile = profile;
        State = SessionState.Running;

        _statisticsService = statisticsService;
        _workingDirectory = profile.WorkingDir;
        _terminalType = terminalType;
    }

    public void SetTerminalControl(EasyTerminalControl control)
    {
        _easyTerminalControl = control;
        TerminalControl = control;

        // Hook output interception for activity tracking after control is loaded
        control.Loaded += (s, e) =>
        {
            control.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (control.ConPTYTerm != null)
                    {
                        control.ConPTYTerm.InterceptOutputToUITerminal = OnTerminalOutput;
                    }
                }
                catch
                {
                    // Ignore errors during hook setup
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
        };

        // Hook Ctrl+Click for link detection
        control.PreviewMouseDown += OnTerminalMouseDown;
    }

    /// <summary>
    /// Handles Ctrl+Click on the terminal for link detection.
    /// </summary>
    private void OnTerminalMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Only handle Ctrl+Click
        if (e.LeftButton != MouseButtonState.Pressed ||
            !Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))
        {
            return;
        }

        // Try to get selected text or find a link near the click
        var clickedText = GetTextForLinkDetection();
        if (!string.IsNullOrEmpty(clickedText))
        {
            LinkClicked?.Invoke(this, clickedText);
            // Don't mark as handled - let the terminal process the click normally too
        }
    }

    /// <summary>
    /// Gets text for link detection, trying selection first, then recent output.
    /// </summary>
    private string? GetTextForLinkDetection()
    {
        // First try to get any selected text (user may have selected a URL/path)
        // This requires the user to double-click first to select, then Ctrl+click
        // Unfortunately, EasyTerminalControl doesn't expose selection APIs directly

        // For now, we'll look at the last few lines of output to find potential links
        // The user can also select text before Ctrl+clicking
        Interlocked.Increment(ref IoCounters.OutputBufferLocks);
        lock (_outputBuffer)
        {
            if (_outputBuffer.Length == 0)
                return null;

            // Get last ~1000 chars which likely contains the visible area
            var startIndex = Math.Max(0, _outputBuffer.Length - 1000);
            return _outputBuffer.ToString(startIndex, _outputBuffer.Length - startIndex);
        }
    }

    /// <summary>
    /// Gets recent terminal output for link detection.
    /// </summary>
    public string GetRecentOutput(int maxChars = 5000)
    {
        Interlocked.Increment(ref IoCounters.OutputBufferLocks);
        lock (_outputBuffer)
        {
            if (_outputBuffer.Length == 0)
                return string.Empty;

            var startIndex = Math.Max(0, _outputBuffer.Length - maxChars);
            return _outputBuffer.ToString(startIndex, _outputBuffer.Length - startIndex);
        }
    }

    /// <summary>
    /// Called when terminal output is received. Updates activity tracking and output buffer.
    /// </summary>
    private void OnTerminalOutput(ref Span<char> str)
    {
        // Increment character count for statistics
        _statisticsService.IncrementCharCount(_workingDirectory, _terminalType, str.Length);

        // Fire output received event for external subscribers (e.g., Claude task detection)
        // Note: This allocates a string copy - only fire if there are subscribers
        if (OutputReceived != null)
        {
            OutputReceived.Invoke(this, str.ToString());
        }

        // Don't modify the output, just track timing
        _lastOutputTime = DateTime.Now;

        // Fire activity changed if we transitioned from idle to active
        if (!_wasActive)
        {
            _wasActive = true;
            ActivityChanged?.Invoke(this, EventArgs.Empty);
        }

        // Parse OSC escape sequences for title changes
        ParseOscSequences(str);

        // Append to output buffer for link detection
        Interlocked.Increment(ref IoCounters.OutputBufferLocks);
        lock (_outputBuffer)
        {
            _outputBuffer.Append(str);

            // Trim if too large (keep last half)
            if (_outputBuffer.Length > MaxOutputBufferSize)
            {
                var keepFrom = _outputBuffer.Length - (MaxOutputBufferSize / 2);
                var kept = _outputBuffer.ToString(keepFrom, _outputBuffer.Length - keepFrom);
                _outputBuffer.Clear();
                _outputBuffer.Append(kept);
            }
        }
    }

    /// <summary>
    /// Parses OSC escape sequences for terminal title changes.
    /// OSC format: ESC ] Ps ; Pt BEL  or  ESC ] Ps ; Pt ST
    /// Where Ps = 0 (set icon name and window title), 2 (set window title)
    /// </summary>
    private void ParseOscSequences(Span<char> str)
    {
        for (int i = 0; i < str.Length; i++)
        {
            char c = str[i];

            if (_parsingOsc)
            {
                // Check for sequence termination
                if (c == '\x07') // BEL
                {
                    ProcessOscSequence();
                    continue;
                }
                else if (c == '\x1b' && i + 1 < str.Length && str[i + 1] == '\\') // ST (ESC \)
                {
                    ProcessOscSequence();
                    i++; // Skip the backslash
                    continue;
                }
                else
                {
                    _oscBuffer.Append(c);
                    // Safety limit to prevent buffer overflow on malformed sequences
                    if (_oscBuffer.Length > 1024)
                    {
                        _parsingOsc = false;
                        _oscBuffer.Clear();
                    }
                }
            }
            else if (c == '\x1b' && i + 1 < str.Length && str[i + 1] == ']')
            {
                // Start of OSC sequence
                _parsingOsc = true;
                _oscBuffer.Clear();
                i++; // Skip the ]
            }
        }
    }

    /// <summary>
    /// Processes a completed OSC sequence and extracts the title if applicable.
    /// </summary>
    private void ProcessOscSequence()
    {
        _parsingOsc = false;
        var content = _oscBuffer.ToString();
        _oscBuffer.Clear();

        // Format: "Ps;Pt" where Ps is the parameter and Pt is the text
        var semicolonIndex = content.IndexOf(';');
        if (semicolonIndex > 0)
        {
            var paramStr = content[..semicolonIndex];
            if (int.TryParse(paramStr, out int param))
            {
                // Ps = 0: Set icon name and window title
                // Ps = 2: Set window title
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

    /// <summary>
    /// Called periodically to check if activity state has changed (active -> idle).
    /// </summary>
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
            // Try to kill the process through the IProcess interface
            if (State == SessionState.Running)
            {
                var termPty = _easyTerminalControl?.ConPTYTerm;
                var process = termPty?.Process;
                if (process != null && !process.HasExited)
                {
                    process.Kill();
                }
            }
        }
        catch
        {
            // Ignore errors during termination
        }
        finally
        {
            State = SessionState.Exited;
        }
    }

    public bool IsProcessRunning()
    {
        if (State == SessionState.Exited)
            return false;

        try
        {
            var termPty = _easyTerminalControl?.ConPTYTerm;
            return termPty?.Process != null && !termPty.Process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Sends text to the terminal as if typed by the user.
    /// </summary>
    /// <param name="text">The text to send.</param>
    /// <param name="appendNewline">If true, appends a newline to execute the command.</param>
    /// <param name="newlineChar">The newline character to use (default: \r for shells).</param>
    /// <param name="useUserInput">If true, uses internal UserInput method (for apps like Claude Code).</param>
    public void SendText(string text, bool appendNewline = true, string newlineChar = "\r", bool useUserInput = false)
    {
        try
        {
            var termPty = _easyTerminalControl?.ConPTYTerm;
            if (termPty == null || !IsProcessRunning())
                return;

            if (useUserInput)
            {
                // Focus first
                _easyTerminalControl?.Focus();

                // Send text + newline via the internal UserInput method
                // This properly triggers key handling for apps like Claude Code
                // Use BeginInvoke to allow focus to settle before sending input
                var textToSend = appendNewline ? text + "\r" : text;
                _easyTerminalControl?.Dispatcher.BeginInvoke(() =>
                {
                    SendViaUserInput(termPty, textToSend);
                }, System.Windows.Threading.DispatcherPriority.Input);
            }
            else
            {
                // Use WriteToTerm for standard shell commands
                var textToSend = appendNewline ? text + newlineChar : text;
                termPty.WriteToTerm(textToSend.AsSpan());
            }
        }
        catch
        {
            // Ignore errors when sending text
        }
    }

    /// <summary>
    /// Sends text to the terminal (without executing - user presses Enter manually).
    /// </summary>
    private static void SendViaUserInput(TermPTY conPtyTerm, string input)
    {
        try
        {
            // Strip any newline characters - user will press Enter manually
            var textOnly = input.TrimEnd('\r', '\n');
            if (!string.IsNullOrEmpty(textOnly))
            {
                conPtyTerm.WriteToTerm(textOnly.AsSpan());
            }
        }
        catch
        {
            // Silently ignore errors
        }
    }

    /// <summary>
    /// Focuses the terminal control.
    /// </summary>
    public void Focus()
    {
        try
        {
            _easyTerminalControl?.Focus();
        }
        catch
        {
            // Ignore focus errors
        }
    }

    /// <summary>
    /// Gets the currently selected text in the terminal.
    /// Note: This clears the selection after retrieving the text.
    /// </summary>
    /// <returns>The selected text, or empty string if no selection.</returns>
    public string GetSelectedText()
    {
        try
        {
            // Access the underlying Terminal control which has GetSelectedText()
            return _easyTerminalControl?.Terminal?.GetSelectedText() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Copies the currently selected text to the clipboard.
    /// </summary>
    /// <returns>True if text was copied, false if no selection.</returns>
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

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    /// <summary>
    /// Checks if this terminal has Win32 keyboard focus.
    /// Walks up from the focused HWND to find if it's within this control's visual tree.
    /// </summary>
    public bool HasWin32Focus()
    {
        try
        {
            if (_easyTerminalControl == null) return false;

            var focusedHwnd = GetFocus();
            if (focusedHwnd == IntPtr.Zero) return false;

            // Get the HWND that hosts this control
            var source = PresentationSource.FromVisual(_easyTerminalControl) as HwndSource;
            if (source == null) return false;

            // Get the bounds of our control in screen coordinates
            var controlBounds = GetScreenBounds(_easyTerminalControl);
            if (controlBounds == null) return false;

            // Get the position of the focused window
            // If it's within our bounds, we have focus
            if (GetCursorPos(out POINT cursorPos))
            {
                var hwndUnderCursor = WindowFromPoint(cursorPos);
                if (hwndUnderCursor != IntPtr.Zero)
                {
                    // Check if the cursor is within our control's bounds
                    var bounds = controlBounds.Value;
                    if (cursorPos.X >= bounds.Left && cursorPos.X <= bounds.Right &&
                        cursorPos.Y >= bounds.Top && cursorPos.Y <= bounds.Bottom)
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            // Ignore errors
        }
        return false;
    }

    private static System.Windows.Rect? GetScreenBounds(System.Windows.Media.Visual visual)
    {
        try
        {
            var source = PresentationSource.FromVisual(visual);
            if (source?.CompositionTarget == null) return null;

            // Get the transform from the visual to the screen
            var transform = visual.TransformToAncestor(source.RootVisual);
            var topLeft = transform.Transform(new System.Windows.Point(0, 0));

            // Convert to screen coordinates
            var screenTopLeft = source.CompositionTarget.TransformToDevice.Transform(topLeft);

            // Get the window position
            if (source is HwndSource hwndSource)
            {
                var windowRect = new RECT();
                GetWindowRect(hwndSource.Handle, out windowRect);

                // Get the size of the visual
                if (visual is System.Windows.FrameworkElement fe)
                {
                    var size = source.CompositionTarget.TransformToDevice.Transform(
                        new System.Windows.Point(fe.ActualWidth, fe.ActualHeight));

                    return new System.Windows.Rect(
                        windowRect.Left + screenTopLeft.X,
                        windowRect.Top + screenTopLeft.Y,
                        size.X,
                        size.Y);
                }
            }
        }
        catch { }
        return null;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    public void Dispose()
    {
        Terminate();
    }
}
