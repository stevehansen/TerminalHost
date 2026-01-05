using TerminalHost.Core.Interfaces;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace TerminalHost.Services;

internal sealed class FolderPickerService : IFolderPickerService
{
    /// <summary>
    /// Synchronous wrapper for picking a single folder.
    /// </summary>
    public string? PickFolder(string? title = null, string? initialPath = null)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return PickFolderOnUIThread(title, initialPath);
        }

        string? result = null;
        var resetEvent = new ManualResetEventSlim(false);

        Dispatcher.UIThread.Post(async () =>
        {
            result = await PickFolderAsync(title, initialPath);
            resetEvent.Set();
        });

        resetEvent.Wait();
        return result;
    }

    private string? PickFolderOnUIThread(string? title, string? initialPath)
    {
        string? result = null;
        var frame = new DispatcherFrame();

        // Must use InvokeAsync to stay on UI thread - folder picker requires main thread on macOS
        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            result = await PickFolderAsync(title, initialPath);
            frame.Continue = false;
        });

        Dispatcher.UIThread.PushFrame(frame);
        return result;
    }

    /// <summary>
    /// Synchronous wrapper for picking multiple folders.
    /// </summary>
    public string[]? PickFolders(string? title = null, string? initialPath = null)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            return PickFoldersOnUIThread(title, initialPath);
        }

        string[]? result = null;
        var resetEvent = new ManualResetEventSlim(false);

        Dispatcher.UIThread.Post(async () =>
        {
            result = await PickFoldersAsync(title, initialPath);
            resetEvent.Set();
        });

        resetEvent.Wait();
        return result;
    }

    private string[]? PickFoldersOnUIThread(string? title, string? initialPath)
    {
        string[]? result = null;
        var frame = new DispatcherFrame();

        // Must use InvokeAsync to stay on UI thread - folder picker requires main thread on macOS
        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            result = await PickFoldersAsync(title, initialPath);
            frame.Continue = false;
        });

        Dispatcher.UIThread.PushFrame(frame);
        return result;
    }

    public async Task<string?> PickFolderAsync(string? title = null, string? initialDirectory = null)
    {
        var topLevel = GetTopLevel();
        if (topLevel == null)
            return null;

        var options = new FolderPickerOpenOptions
        {
            Title = title ?? "Select Folder",
            AllowMultiple = false
        };

        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
        {
            options.SuggestedStartLocation = await topLevel.StorageProvider
                .TryGetFolderFromPathAsync(initialDirectory);
        }

        var result = await topLevel.StorageProvider.OpenFolderPickerAsync(options);

        return result.Count > 0 ? result[0].Path.LocalPath : null;
    }

    public async Task<string[]?> PickFoldersAsync(string? title = null, string? initialDirectory = null)
    {
        var topLevel = GetTopLevel();
        if (topLevel == null)
            return null;

        var options = new FolderPickerOpenOptions
        {
            Title = title ?? "Select Folders",
            AllowMultiple = true
        };

        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
        {
            options.SuggestedStartLocation = await topLevel.StorageProvider
                .TryGetFolderFromPathAsync(initialDirectory);
        }

        var result = await topLevel.StorageProvider.OpenFolderPickerAsync(options);

        return result.Count > 0 ? result.Select(f => f.Path.LocalPath).ToArray() : null;
    }

    private static TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }
}
