using System.IO;
using Microsoft.Win32;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Windows.Services;

/// <summary>
/// Windows implementation of folder picker using the modern OpenFolderDialog.
/// </summary>
public sealed class FolderPickerService : IFolderPickerService
{
    /// <inheritdoc />
    public string? PickFolder(string? title = null, string? initialPath = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title ?? "Select Folder",
            Multiselect = false
        };

        // Set initial directory if provided and exists
        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
        {
            dialog.InitialDirectory = initialPath;
        }

        if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.FolderName))
        {
            return dialog.FolderName;
        }

        return null;
    }
}
