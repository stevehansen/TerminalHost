using System.Collections.ObjectModel;
using TerminalHost.ViewModels;

namespace TerminalHost.Services;

/// <summary>
/// Toast notification type.
/// </summary>
public enum ToastType
{
    Info,
    Success,
    Warning,
    Error,
    Progress
}

/// <summary>
/// Service for displaying toast notifications.
/// </summary>
public interface IToastService
{
    /// <summary>
    /// Observable collection of active toasts for UI binding.
    /// </summary>
    ObservableCollection<ToastViewModel> Toasts { get; }

    /// <summary>
    /// Shows a simple toast notification.
    /// </summary>
    /// <param name="message">The message to display</param>
    /// <param name="type">Toast type (Info, Success, Warning, Error)</param>
    /// <param name="autoClose">Whether to auto-close after delay</param>
    /// <returns>Toast ID for reference</returns>
    string Show(string message, ToastType type = ToastType.Info, bool autoClose = true);

    /// <summary>
    /// Shows a toast with custom icon.
    /// </summary>
    /// <param name="message">The message to display</param>
    /// <param name="icon">Custom icon to display</param>
    /// <param name="autoClose">Whether to auto-close after delay</param>
    /// <returns>Toast ID for reference</returns>
    string Show(string message, string icon, bool autoClose = true);

    /// <summary>
    /// Creates a progress toast that can be updated.
    /// </summary>
    /// <param name="message">Initial message</param>
    /// <returns>Progress toast handle for updates</returns>
    IProgressToast ShowProgress(string message);

    /// <summary>
    /// Updates an existing toast by ID.
    /// </summary>
    /// <param name="id">Toast ID</param>
    /// <param name="message">New message</param>
    /// <param name="icon">New icon (optional)</param>
    void Update(string id, string message, string? icon = null);

    /// <summary>
    /// Closes a specific toast by ID.
    /// </summary>
    /// <param name="id">Toast ID to close</param>
    void Close(string id);

    /// <summary>
    /// Closes all toasts.
    /// </summary>
    void CloseAll();
}

/// <summary>
/// Handle for a progress toast that can be updated.
/// </summary>
public interface IProgressToast : IDisposable
{
    /// <summary>
    /// Unique identifier for this toast.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Updates the progress toast message and optionally the icon.
    /// </summary>
    /// <param name="message">New message</param>
    /// <param name="icon">New icon (optional)</param>
    void Update(string message, string? icon = null);

    /// <summary>
    /// Marks the operation as complete with a success message.
    /// </summary>
    /// <param name="message">Completion message</param>
    /// <param name="type">Toast type for completion (default: Success)</param>
    void Complete(string message, ToastType type = ToastType.Success);

    /// <summary>
    /// Marks the operation as failed with an error message.
    /// </summary>
    /// <param name="message">Error message</param>
    void Fail(string message);

    /// <summary>
    /// Closes the progress toast immediately.
    /// </summary>
    void Close();
}
