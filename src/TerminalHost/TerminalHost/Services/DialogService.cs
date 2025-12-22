using System.Diagnostics;

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
/// Provides dialog methods for Avalonia/macOS.
/// Stage 4: Simplified stub implementation - full dialogs will be added in Stage 5.
/// </summary>
public class DialogService : IDialogService
{
    /// <summary>
    /// Shows an error dialog with OK button.
    /// </summary>
    public void ShowError(string message, string title = "Error")
    {
        // Stage 4: Log to debug output, full dialog in Stage 5
        Debug.WriteLine($"[ERROR] {title}: {message}");
    }

    /// <summary>
    /// Shows a warning dialog with OK button.
    /// </summary>
    public void ShowWarning(string message, string title = "Warning")
    {
        // Stage 4: Log to debug output, full dialog in Stage 5
        Debug.WriteLine($"[WARNING] {title}: {message}");
    }

    /// <summary>
    /// Shows an information dialog with OK button.
    /// </summary>
    public void ShowInfo(string message, string title = "Information")
    {
        // Stage 4: Log to debug output, full dialog in Stage 5
        Debug.WriteLine($"[INFO] {title}: {message}");
    }

    /// <summary>
    /// Shows a confirmation dialog with Yes/No buttons.
    /// Returns true if Yes was clicked, false otherwise.
    /// </summary>
    public bool ShowConfirmation(string message, string title = "Confirm")
    {
        // Stage 4: Default to true, full dialog in Stage 5
        Debug.WriteLine($"[CONFIRM] {title}: {message} -> defaulting to Yes");
        return true;
    }

    /// <summary>
    /// Shows an input dialog to get text from the user.
    /// Returns the input text, or null if cancelled.
    /// </summary>
    public string? ShowInput(string prompt, string title = "Input", string defaultValue = "")
    {
        // Stage 4: Return default value, full dialog in Stage 5
        Debug.WriteLine($"[INPUT] {title}: {prompt} -> returning default: {defaultValue}");
        return defaultValue;
    }
}
