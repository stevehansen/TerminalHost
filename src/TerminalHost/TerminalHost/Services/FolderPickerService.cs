using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace TerminalHost.Services;

internal sealed class FolderPickerService : IFolderPickerService
{
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

    private static TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }
}
