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
