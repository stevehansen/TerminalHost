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
