using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Result from the CreateWorktreeDialog.
/// </summary>
public record CreateWorktreeDialogResult(
    string BranchName,
    string WorktreePath,
    bool CreateNewBranch,
    bool OpenAfterCreation);

/// <summary>
/// Result from the CreateIntentDialog.
/// </summary>
public record CreateIntentDialogResult(
    string Name,
    string? Context,
    bool UseExistingFolder,
    // For new worktree mode:
    string? BranchName,
    string? WorktreePath,
    bool CreateNewBranch,
    // For existing folder mode:
    string? ExistingFolderPath);

public interface IDialogService
{
    void ShowError(string message, string title = "Error");
    void ShowWarning(string message, string title = "Warning");
    void ShowInfo(string message, string title = "Information");
    bool ShowConfirmation(string message, string title = "Confirm");

    /// <summary>
    /// Shows an input dialog to get text from the user.
    /// Returns the input text, or null if cancelled.
    /// </summary>
    string? ShowInput(string prompt, string title = "Input", string defaultValue = "");

    /// <summary>
    /// Shows a dialog with custom button labels.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <param name="title">The dialog title.</param>
    /// <param name="buttons">Button labels (first is primary/accent color).</param>
    /// <returns>Index of clicked button (0-based), or -1 if dialog was closed without selection.</returns>
    int ShowCustomButtons(string message, string title, params string[] buttons);

    /// <summary>
    /// Shows a dialog for creating a new git worktree.
    /// </summary>
    /// <param name="repoPath">Path to the repository.</param>
    /// <param name="branches">Available branches for selection.</param>
    /// <param name="suggestedBasePath">Base path for auto-generated worktree location.</param>
    /// <returns>The dialog result, or null if cancelled.</returns>
    CreateWorktreeDialogResult? ShowCreateWorktreeDialog(string repoPath, IEnumerable<GitBranch> branches, string suggestedBasePath);

    /// <summary>
    /// Shows a dialog for creating a new Timeline intent.
    /// </summary>
    /// <param name="repoPath">Path to the repository (for context display and worktree creation).</param>
    /// <param name="branches">Available branches for worktree creation.</param>
    /// <param name="suggestedBasePath">Base path for auto-generated worktree location.</param>
    /// <param name="openFolders">Currently open folders (for existing folder selection).</param>
    /// <returns>The dialog result, or null if cancelled.</returns>
    CreateIntentDialogResult? ShowCreateIntentDialog(
        string repoPath,
        IEnumerable<GitBranch> branches,
        string suggestedBasePath,
        IEnumerable<string> openFolders);

}
