using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using TerminalHost.Core.Interfaces;
using TerminalHost.Domain;
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
/// Provides dialog methods for Avalonia/macOS.
/// </summary>
public class DialogService : IDialogService
{
    private Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }

    private DialogResult ShowDialog(string message, string title, DialogType type, DialogButtons buttons)
    {
        // If we're on the UI thread, we can't block - use a frame-based approach
        if (Dispatcher.UIThread.CheckAccess())
        {
            return ShowDialogOnUIThread(message, title, type, buttons);
        }

        // From background thread, we can safely block
        DialogResult result = DialogResult.None;
        var resetEvent = new ManualResetEventSlim(false);

        Dispatcher.UIThread.Post(() =>
        {
            var dialog = new NotificationDialog(message, title, type, buttons);
            var owner = GetMainWindow();

            dialog.Closed += (s, e) =>
            {
                result = dialog.Result;
                resetEvent.Set();
            };

            if (owner != null)
            {
                dialog.ShowDialog(owner);
            }
            else
            {
                dialog.Show();
            }
        });

        resetEvent.Wait();
        return result;
    }

    private DialogResult ShowDialogOnUIThread(string message, string title, DialogType type, DialogButtons buttons)
    {
        DialogResult result = DialogResult.None;
        var frame = new DispatcherFrame();

        var dialog = new NotificationDialog(message, title, type, buttons);
        var owner = GetMainWindow();

        dialog.Closed += (s, e) =>
        {
            result = dialog.Result;
            frame.Continue = false;
        };

        if (owner != null)
        {
            dialog.ShowDialog(owner);
        }
        else
        {
            dialog.Show();
        }

        // Push a frame to process messages until dialog closes
        Dispatcher.UIThread.PushFrame(frame);

        return result;
    }

    /// <summary>
    /// Shows an error dialog with OK button.
    /// </summary>
    public void ShowError(string message, string title = "Error")
    {
        ShowDialog(message, title, DialogType.Error, DialogButtons.OK);
    }

    /// <summary>
    /// Shows a warning dialog with OK button.
    /// </summary>
    public void ShowWarning(string message, string title = "Warning")
    {
        ShowDialog(message, title, DialogType.Warning, DialogButtons.OK);
    }

    /// <summary>
    /// Shows an information dialog with OK button.
    /// </summary>
    public void ShowInfo(string message, string title = "Information")
    {
        ShowDialog(message, title, DialogType.Information, DialogButtons.OK);
    }

    /// <summary>
    /// Shows a confirmation dialog with Yes/No buttons.
    /// Returns true if Yes was clicked, false otherwise.
    /// </summary>
    public bool ShowConfirmation(string message, string title = "Confirm")
    {
        var result = ShowDialog(message, title, DialogType.Question, DialogButtons.YesNo);
        return result == DialogResult.Yes;
    }

    /// <summary>
    /// Shows an input dialog to get text from the user.
    /// Returns the input text, or null if cancelled.
    /// </summary>
    public string? ShowInput(string prompt, string title = "Input", string defaultValue = "")
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return ShowInputOnUIThread(prompt, title, defaultValue);
        }

        string? result = null;
        var resetEvent = new ManualResetEventSlim(false);

        Dispatcher.UIThread.Post(() =>
        {
            var dialog = new InputDialog(prompt, title, defaultValue);
            var owner = GetMainWindow();

            dialog.Closed += (s, e) =>
            {
                result = dialog.Confirmed ? dialog.InputText : null;
                resetEvent.Set();
            };

            if (owner != null)
            {
                dialog.ShowDialog(owner);
            }
            else
            {
                dialog.Show();
            }
        });

        resetEvent.Wait();
        return result;
    }

    private string? ShowInputOnUIThread(string prompt, string title, string defaultValue)
    {
        string? result = null;
        var frame = new DispatcherFrame();

        var dialog = new InputDialog(prompt, title, defaultValue);
        var owner = GetMainWindow();

        dialog.Closed += (s, e) =>
        {
            result = dialog.Confirmed ? dialog.InputText : null;
            frame.Continue = false;
        };

        if (owner != null)
        {
            dialog.ShowDialog(owner);
        }
        else
        {
            dialog.Show();
        }

        Dispatcher.UIThread.PushFrame(frame);
        return result;
    }

    /// <summary>
    /// Shows a dialog with custom button labels.
    /// </summary>
    public int ShowCustomButtons(string message, string title, params string[] buttons)
    {
        // For now, use a simple confirmation which returns 0 for first button (Yes) or -1 for close
        // A proper implementation would create a custom dialog with the provided buttons
        var result = ShowDialog(message, title, DialogType.Question, DialogButtons.YesNo);
        return result switch
        {
            DialogResult.Yes => 0,
            DialogResult.No => 1,
            _ => -1
        };
    }

    /// <summary>
    /// Shows the Create Worktree dialog (synchronous wrapper).
    /// </summary>
    public CreateWorktreeDialogResult? ShowCreateWorktreeDialog(
        string repoPath,
        IEnumerable<GitBranch> branches,
        string suggestedBasePath)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return ShowCreateWorktreeDialogOnUIThread(repoPath, branches, suggestedBasePath);
        }

        CreateWorktreeDialogResult? result = null;
        var resetEvent = new ManualResetEventSlim(false);

        Dispatcher.UIThread.Post(() =>
        {
            result = ShowCreateWorktreeDialogOnUIThread(repoPath, branches, suggestedBasePath);
            resetEvent.Set();
        });

        resetEvent.Wait();
        return result;
    }

    private CreateWorktreeDialogResult? ShowCreateWorktreeDialogOnUIThread(
        string repoPath,
        IEnumerable<GitBranch> branches,
        string suggestedBasePath)
    {
        CreateWorktreeDialogResult? result = null;
        var frame = new DispatcherFrame();

        var dialog = new CreateWorktreeDialog(repoPath, branches, suggestedBasePath);
        var owner = GetMainWindow();

        dialog.Closed += (s, e) =>
        {
            result = dialog.GetResult();
            frame.Continue = false;
        };

        if (owner != null)
        {
            dialog.ShowDialog(owner);
        }
        else
        {
            dialog.Show();
        }

        Dispatcher.UIThread.PushFrame(frame);
        return result;
    }

    /// <summary>
    /// Shows the Create Intent dialog (for Timeline mode).
    /// </summary>
    public CreateIntentDialogResult? ShowCreateIntentDialog(
        string repoPath,
        IEnumerable<GitBranch> branches,
        string suggestedBasePath,
        IEnumerable<string> openFolders)
    {
        // Stub implementation - return null (cancelled)
        // Full implementation would show CreateIntentDialog
        return null;
    }

    /// <summary>
    /// Shows the Create Worktree dialog (async version).
    /// </summary>
    public async Task<CreateWorktreeDialogResult?> ShowCreateWorktreeDialogAsync(
        string repositoryPath,
        IEnumerable<GitBranch> branches,
        string suggestedBasePath)
    {
        // Ensure we're on the UI thread
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(
                () => ShowCreateWorktreeDialogAsync(repositoryPath, branches, suggestedBasePath));
        }

        var dialog = new CreateWorktreeDialog(repositoryPath, branches, suggestedBasePath);
        var owner = GetMainWindow();

        if (owner != null)
        {
            await dialog.ShowDialog(owner);
        }
        else
        {
            dialog.Show();
            // Wait for dialog to close
            var tcs = new TaskCompletionSource<bool>();
            dialog.Closed += (s, e) => tcs.SetResult(true);
            await tcs.Task;
        }

        return dialog.GetResult();
    }
}
