namespace TerminalHost.Core.Interfaces;

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
}
