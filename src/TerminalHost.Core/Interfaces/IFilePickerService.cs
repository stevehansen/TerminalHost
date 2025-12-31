using System.Collections.Generic;
using System.Threading.Tasks;
using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Abstraction for file selection dialogs.
/// Replaces platform-specific file picker APIs.
/// </summary>
public interface IFilePickerService
{
    /// <summary>
    /// Opens a file picker dialog.
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="filters">File type filters</param>
    /// <param name="initialDirectory">Starting directory (optional)</param>
    /// <param name="allowMultiple">Allow multiple file selection</param>
    /// <returns>Selected file path(s), or empty if cancelled</returns>
    Task<IReadOnlyList<string>> PickFilesAsync(
        string? title = null,
        IReadOnlyList<FilePickerFilter>? filters = null,
        string? initialDirectory = null,
        bool allowMultiple = false);

    /// <summary>
    /// Opens a single file picker dialog.
    /// </summary>
    Task<string?> PickFileAsync(
        string? title = null,
        IReadOnlyList<FilePickerFilter>? filters = null,
        string? initialDirectory = null);

    /// <summary>
    /// Opens a save file dialog.
    /// </summary>
    /// <param name="title">Dialog title</param>
    /// <param name="defaultFileName">Default file name</param>
    /// <param name="filters">File type filters</param>
    /// <param name="initialDirectory">Starting directory (optional)</param>
    /// <returns>Selected save path, or null if cancelled</returns>
    Task<string?> PickSaveFileAsync(
        string? title = null,
        string? defaultFileName = null,
        IReadOnlyList<FilePickerFilter>? filters = null,
        string? initialDirectory = null);
}
