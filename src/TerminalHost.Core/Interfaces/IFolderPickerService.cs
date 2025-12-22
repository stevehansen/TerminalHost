namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Platform-agnostic service for showing folder picker dialogs.
/// </summary>
public interface IFolderPickerService
{
    /// <summary>
    /// Shows a folder picker dialog and returns the selected path.
    /// </summary>
    /// <param name="title">The dialog title/description.</param>
    /// <param name="initialPath">Optional initial directory to display.</param>
    /// <returns>The selected folder path, or null if cancelled.</returns>
    string? PickFolder(string? title = null, string? initialPath = null);
}
