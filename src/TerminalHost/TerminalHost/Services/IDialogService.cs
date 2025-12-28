using TerminalHost.Domain;

namespace TerminalHost.Services;

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
    /// Shows the Create Worktree dialog.
    /// </summary>
    /// <param name="repositoryPath">Path to the repository.</param>
    /// <param name="branches">Available branches.</param>
    /// <param name="suggestedBasePath">Suggested base path for the worktree.</param>
    /// <returns>The dialog result, or null if cancelled.</returns>
    Task<CreateWorktreeDialogResult?> ShowCreateWorktreeDialogAsync(
        string repositoryPath,
        IEnumerable<GitBranch> branches,
        string suggestedBasePath);
}
