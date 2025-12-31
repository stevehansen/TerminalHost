using System.Threading.Tasks;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Abstraction for clipboard operations.
/// Replaces platform-specific clipboard APIs.
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
