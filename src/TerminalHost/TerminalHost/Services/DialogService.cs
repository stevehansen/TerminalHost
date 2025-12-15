using System.Windows;
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
public class DialogService : IDialogService // Make it non-static and implement interface
{
    /// <summary>
    /// Shows an error dialog with OK button.
    /// </summary>
    public void ShowError(string message, string title = "Error") // Make it non-static
    {
        Show(message, title, DialogType.Error, DialogButtons.OK);
    }

    /// <summary>
    /// Shows a warning dialog with OK button.
    /// </summary>
    public void ShowWarning(string message, string title = "Warning") // Make it non-static
    {
        Show(message, title, DialogType.Warning, DialogButtons.OK);
    }

    /// <summary>
    /// Shows an information dialog with OK button.
    /// </summary>
    public void ShowInfo(string message, string title = "Information") // Make it non-static
    {
        Show(message, title, DialogType.Information, DialogButtons.OK);
    }

    /// <summary>
    /// Shows a confirmation dialog with Yes/No buttons.
    /// Returns true if Yes was clicked, false otherwise.
    /// </summary>
    public bool ShowConfirmation(string message, string title = "Confirm") // Make it non-static
    {
        var result = Show(message, title, DialogType.Question, DialogButtons.YesNo);
        return result == DialogResult.Yes;
    }

    /// <summary>
    /// Shows a dialog with full control over type and buttons.
    /// </summary>
    public DialogResult Show(string message, string title, DialogType type, DialogButtons buttons) // Make it non-static
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
    /// Gets the currently active window to use as dialog owner.
    /// </summary>
    private Window? GetActiveWindow() // Make it non-static
    {
        // In a real WPF app, Application.Current is usually available.
        // For testing, this might be null, so handle gracefully.
        return Application.Current?.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsActive)
            ?? Application.Current?.MainWindow;
    }
}