using System.Windows;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Views.Dialogs;

namespace TerminalHost.Services;

/// <summary>
/// Type of dialog message to display.
/// </summary>
public enum DialogType
{
    Error,
    Warning,
    Information,
    Question
}

/// <summary>
/// Button configurations for dialogs.
/// </summary>
public enum DialogButtons
{
    OK,
    OKCancel,
    YesNo
}

/// <summary>
/// Result returned from dialog interactions.
/// </summary>
public enum DialogResult
{
    None,
    OK,
    Cancel,
    Yes,
    No
}

/// <summary>
/// Provides themed dialog methods to replace MessageBox throughout the application.
/// </summary>
public class DialogService : IDialogService
{
    /// <summary>
    /// Shows an error dialog with OK button.
    /// </summary>
    public void ShowError(string message, string title = "Error")
    {
        Show(message, title, DialogType.Error, DialogButtons.OK);
    }

    /// <summary>
    /// Shows a warning dialog with OK button.
    /// </summary>
    public void ShowWarning(string message, string title = "Warning")
    {
        Show(message, title, DialogType.Warning, DialogButtons.OK);
    }

    /// <summary>
    /// Shows an information dialog with OK button.
    /// </summary>
    public void ShowInfo(string message, string title = "Information")
    {
        Show(message, title, DialogType.Information, DialogButtons.OK);
    }

    /// <summary>
    /// Shows a confirmation dialog with Yes/No buttons.
    /// Returns true if Yes was clicked, false otherwise.
    /// </summary>
    public bool ShowConfirmation(string message, string title = "Confirm")
    {
        var result = Show(message, title, DialogType.Question, DialogButtons.YesNo);
        return result == DialogResult.Yes;
    }

    /// <summary>
    /// Shows a dialog with full control over type and buttons.
    /// </summary>
    private static DialogResult Show(string message, string title, DialogType type, DialogButtons buttons)
    {
        var owner = GetActiveWindow();
        var dialog = new NotificationDialog(message, title, type, buttons)
        {
            Owner = owner,
            WindowStartupLocation = owner != null
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen
        };

        dialog.ShowDialog();
        return dialog.Result;
    }

    /// <summary>
    /// Shows an input dialog to get text from the user.
    /// Returns the input text, or null if cancelled.
    /// </summary>
    public string? ShowInput(string prompt, string title = "Input", string defaultValue = "")
    {
        var owner = GetActiveWindow();
        var dialog = new InputDialog(prompt, title, defaultValue)
        {
            Owner = owner,
            WindowStartupLocation = owner != null
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen
        };

        var result = dialog.ShowDialog();
        return result == true ? dialog.InputText : null;
    }

    /// <summary>
    /// Shows a dialog with custom button labels.
    /// Returns the index of the clicked button (0-based), or -1 if dialog was closed without selection.
    /// </summary>
    public int ShowCustomButtons(string message, string title, params string[] buttons)
    {
        if (buttons == null || buttons.Length == 0)
        {
            buttons = ["OK"];
        }

        var owner = GetActiveWindow();
        var dialog = new CustomButtonDialog(message, title, buttons)
        {
            Owner = owner,
            WindowStartupLocation = owner != null
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen
        };

        dialog.ShowDialog();
        return dialog.SelectedButtonIndex;
    }

    /// <summary>
    /// Shows a dialog for creating a new git worktree.
    /// </summary>
    public CreateWorktreeDialogResult? ShowCreateWorktreeDialog(string repoPath, IEnumerable<GitBranch> branches, string suggestedBasePath)
    {
        var owner = GetActiveWindow();
        var dialog = new CreateWorktreeDialog(repoPath, branches, suggestedBasePath)
        {
            Owner = owner,
            WindowStartupLocation = owner != null
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen
        };

        var result = dialog.ShowDialog();
        if (result == true && dialog.Confirmed)
        {
            return new CreateWorktreeDialogResult(
                dialog.BranchName,
                dialog.WorktreePath,
                dialog.CreateNewBranch,
                dialog.OpenAfterCreation);
        }

        return null;
    }

    /// <summary>
    /// Gets the currently active window to use as dialog owner.
    /// </summary>
    private static Window? GetActiveWindow()
    {
        // In a real WPF app, Application.Current is usually available.
        // For testing, this might be null, so handle gracefully.
        return Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsActive)
            ?? Application.Current?.MainWindow;
    }
}
