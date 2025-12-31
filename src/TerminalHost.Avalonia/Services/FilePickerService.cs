using TerminalHost.Core.Interfaces;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace TerminalHost.Services;

internal sealed class FilePickerService : IFilePickerService
{
    public async Task<IReadOnlyList<string>> PickFilesAsync(
        string? title = null,
        IReadOnlyList<FilePickerFilter>? filters = null,
        string? initialDirectory = null,
        bool allowMultiple = false)
    {
        var topLevel = GetTopLevel();
        if (topLevel == null)
            return Array.Empty<string>();

        var options = new FilePickerOpenOptions
        {
            Title = title ?? "Select File",
            AllowMultiple = allowMultiple,
            FileTypeFilter = ConvertFilters(filters)
        };

        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
        {
            options.SuggestedStartLocation = await topLevel.StorageProvider
                .TryGetFolderFromPathAsync(initialDirectory);
        }

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(options);

        return result.Select(f => f.Path.LocalPath).ToList();
    }

    public async Task<string?> PickFileAsync(
        string? title = null,
        IReadOnlyList<FilePickerFilter>? filters = null,
        string? initialDirectory = null)
    {
        var files = await PickFilesAsync(title, filters, initialDirectory, false);
        return files.Count > 0 ? files[0] : null;
    }

    public async Task<string?> PickSaveFileAsync(
        string? title = null,
        string? defaultFileName = null,
        IReadOnlyList<FilePickerFilter>? filters = null,
        string? initialDirectory = null)
    {
        var topLevel = GetTopLevel();
        if (topLevel == null)
            return null;

        var options = new FilePickerSaveOptions
        {
            Title = title ?? "Save File",
            SuggestedFileName = defaultFileName,
            FileTypeChoices = ConvertFilters(filters)
        };

        if (!string.IsNullOrEmpty(initialDirectory) && Directory.Exists(initialDirectory))
        {
            options.SuggestedStartLocation = await topLevel.StorageProvider
                .TryGetFolderFromPathAsync(initialDirectory);
        }

        var result = await topLevel.StorageProvider.SaveFilePickerAsync(options);

        return result?.Path.LocalPath;
    }

    private static IReadOnlyList<Avalonia.Platform.Storage.FilePickerFileType>? ConvertFilters(
        IReadOnlyList<FilePickerFilter>? filters)
    {
        if (filters == null || filters.Count == 0)
            return null;

        return filters.Select(f => new Avalonia.Platform.Storage.FilePickerFileType(f.Name)
        {
            Patterns = f.Extensions.Select(e => e.StartsWith("*.") ? e : $"*.{e}").ToList()
        }).ToList();
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
