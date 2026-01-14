using System.Text;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
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
    private DateTime? _activitySuppressedUntil;

    // Terminal title tracking (parsed from OSC escape sequences)
    private string _terminalTitle = string.Empty;
    private readonly StringBuilder _oscBuffer = new();
    private bool _parsingOsc;

    // Output buffer for link detection (circular buffer of recent lines)
    private readonly StringBuilder _outputBuffer = new();
    private const int MaxOutputBufferSize = 50000; // ~50KB of recent output

    /// <summary>
    /// The last time output was received from the terminal.
    /// </summary>
    public DateTime? LastOutputTime => _lastOutputTime;

    /// <summary>
    /// Returns true if the terminal has produced substantial output within the last 3 seconds.
    /// </summary>
    public bool IsActive => _lastOutputTime.HasValue &&
        (DateTime.Now - _lastOutputTime.Value).TotalSeconds < 3;

    /// <summary>
    /// Temporarily suppresses activity detection for a period of time.
    /// Use this when resize/tab switch causes terminal redraw that shouldn't count as activity.
    /// </summary>
    /// <param name="seconds">How long to suppress activity detection.</param>
    public void SuppressActivityBriefly(double seconds = 2.0)
    {
        _activitySuppressedUntil = DateTime.Now.AddSeconds(seconds);
        // Also clear current activity state to stop any current spinner
        _lastOutputTime = null;
        if (_wasActive)
        {
            _wasActive = false;
            ActivityChanged?.Invoke(this, EventArgs.Empty);
        }
    }

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
        // Unsubscribe from old control to prevent memory leaks
        UnsubscribeFromTerminalControl();

        _terminalControl = control;
        TerminalControl = control.NativeControl;

        control.Loaded += OnTerminalLoaded;
        control.OutputReceived += OnTerminalOutput;
        control.MouseClicked += OnTerminalMouseClicked;
        control.ProcessExited += OnProcessExited;
        control.Resized += OnTerminalResized;
    }

    /// <summary>
    /// Unsubscribes from the current terminal control's events.
    /// </summary>
    private void UnsubscribeFromTerminalControl()
    {
        if (_terminalControl != null)
        {
            _terminalControl.Loaded -= OnTerminalLoaded;
            _terminalControl.OutputReceived -= OnTerminalOutput;
            _terminalControl.MouseClicked -= OnTerminalMouseClicked;
            _terminalControl.ProcessExited -= OnProcessExited;
            _terminalControl.Resized -= OnTerminalResized;
        }
    }

    private void OnTerminalResized(object? sender, EventArgs e)
    {
        // Suppress activity briefly after resize to avoid false positives
        // from terminal redraw output (shell prompt, etc.)
        SuppressActivityBriefly(1.5);
    }

    private void OnTerminalLoaded(object? sender, EventArgs e)
    {
        // Terminal is ready for use
    }

    /// <summary>
    /// Called when terminal output is received. Updates activity tracking and output buffer.
    /// </summary>
    private void OnTerminalOutput(string output)
    {
        // Increment character count for statistics
        _statisticsService.IncrementCharCount(_workingDirectory, _terminalType, output.Length);

        // Only trigger activity for output that contains visible characters
        // This prevents invisible escape sequences (cursor updates, title changes, etc.)
        // from keeping the activity indicator spinning indefinitely
        // Also skip if activity is temporarily suppressed (e.g., after resize/tab switch)
        var isSuppressed = _activitySuppressedUntil.HasValue && DateTime.Now < _activitySuppressedUntil.Value;
        if (!isSuppressed && ContainsVisibleOutput(output))
        {
            _lastOutputTime = DateTime.Now;

            // Fire activity changed if we transitioned from idle to active
            if (!_wasActive)
            {
                _wasActive = true;
                ActivityChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        // Parse OSC escape sequences for title changes
        ParseOscSequences(output.AsSpan());

        // Append to output buffer for link detection
        AppendToOutputBuffer(output);
    }

    /// <summary>
    /// Checks if output contains substantial visible (printable) characters.
    /// Returns false if output consists only of control characters, escape sequences,
    /// or very short output (like single-character status updates).
    /// </summary>
    private static bool ContainsVisibleOutput(string output)
    {
        // Require at least this many visible characters to trigger activity
        // This filters out prompt redraws, single-char spinners, etc.
        const int MinVisibleChars = 5;

        bool inEscapeSequence = false;
        int visibleCount = 0;

        for (int i = 0; i < output.Length; i++)
        {
            char c = output[i];

            // Start of escape sequence
            if (c == '\x1b')
            {
                inEscapeSequence = true;
                continue;
            }

            if (inEscapeSequence)
            {
                // CSI sequence: ESC [ ... letter
                // OSC sequence: ESC ] ... BEL or ST
                // Other: ESC letter
                if (c == '[')
                {
                    // CSI - scan until letter
                    while (++i < output.Length)
                    {
                        char sc = output[i];
                        if (sc >= 0x40 && sc <= 0x7E) // @ to ~
                            break;
                    }
                }
                else if (c == ']')
                {
                    // OSC - scan until BEL or ST
                    while (++i < output.Length)
                    {
                        if (output[i] == '\x07') // BEL
                            break;
                        if (output[i] == '\x1b' && i + 1 < output.Length && output[i + 1] == '\\')
                        {
                            i++; // Skip ST
                            break;
                        }
                    }
                }
                // Single-character escape sequence (ESC letter)
                inEscapeSequence = false;
                continue;
            }

            // Skip control characters
            if (c < 0x20)
                continue;

            // Count visible characters
            if (c >= 0x20)
            {
                visibleCount++;
                if (visibleCount >= MinVisibleChars)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Handles mouse click events from the terminal control.
    /// </summary>
    private void OnTerminalMouseClicked(object? sender, TerminalMouseEventArgs e)
    {
        // Only handle Ctrl+Click for link detection
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

    /// <summary>
    /// Gets text for link detection, trying selection first, then recent output.
    /// </summary>
    public string? GetTextForLinkDetection()
    {
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

            // Trim if too large (keep last half) - use Remove to avoid string allocation
            if (_outputBuffer.Length > MaxOutputBufferSize)
            {
                var removeCount = _outputBuffer.Length - (MaxOutputBufferSize / 2);
                _outputBuffer.Remove(0, removeCount);
            }
        }
    }

    /// <summary>
    /// Parses OSC escape sequences for terminal title changes.
    /// OSC format: ESC ] Ps ; Pt BEL  or  ESC ] Ps ; Pt ST
    /// Where Ps = 0 (set icon name and window title), 2 (set window title)
    /// </summary>
    private void ParseOscSequences(ReadOnlySpan<char> str)
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
            if (State == SessionState.Running)
            {
                _terminalControl?.Kill();
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

        return _terminalControl?.IsProcessRunning ?? false;
    }

    /// <summary>
    /// Sends text to the terminal as if typed by the user.
    /// </summary>
    /// <param name="text">The text to send.</param>
    /// <param name="appendNewline">If true, appends a newline to execute the command.</param>
    /// <param name="newlineChar">The newline character to use (default: \r for shells).</param>
    /// <param name="useUserInput">If true, focuses the terminal first (for apps like Claude Code).</param>
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
        catch
        {
            // Ignore errors when sending text
        }
    }

    /// <summary>
    /// Focuses the terminal control.
    /// </summary>
    public void Focus()
    {
        try
        {
            _terminalControl?.Focus();
        }
        catch
        {
            // Ignore focus errors
        }
    }

    /// <summary>
    /// Gets the currently selected text in the terminal.
    /// </summary>
    /// <returns>The selected text, or empty string if no selection.</returns>
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

    /// <summary>
    /// Copies the currently selected text to the clipboard.
    /// </summary>
    /// <returns>True if text was copied, false if no selection.</returns>
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
        UnsubscribeFromTerminalControl();
        Terminate();

        // Dispose the terminal control if it implements IDisposable
        // This is critical - MacTerminalControl holds PTY processes, timers, and buffers
        if (_terminalControl is IDisposable disposableControl)
        {
            disposableControl.Dispose();
        }
        _terminalControl = null;

        // Clear all event subscribers to prevent memory leaks
        // (MainViewModel subscribes to LinkClicked, etc.)
        ActivityChanged = null;
        TitleChanged = null;
        LinkClicked = null;
        ProcessExited = null;
    }
}
