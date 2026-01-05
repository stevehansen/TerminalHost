using System;
using System.Threading.Tasks;
using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Abstraction for terminal control implementations.
/// Allows different terminal backends (VtNetCore for macOS, EasyTerminalControl for Windows, etc.)
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

    /// <summary>
    /// Fired when the terminal is resized.
    /// </summary>
    event EventHandler? Resized;
}
