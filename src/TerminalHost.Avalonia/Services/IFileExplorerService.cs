using System.IO;
using TerminalHost.Domain;

namespace TerminalHost.Services;

public interface IFileExplorerService
{
    /// <summary>
    /// Gets the children (files and directories) of a directory.
    /// Folders are returned first, then files, both sorted alphabetically.
    /// </summary>
    Task<List<FileSystemNode>> GetChildrenAsync(string directoryPath, bool showHidden = false);

    /// <summary>
    /// Creates a new empty file at the specified path.
    /// </summary>
    Task<bool> CreateFileAsync(string path);

    /// <summary>
    /// Creates a new directory at the specified path.
    /// </summary>
    Task<bool> CreateDirectoryAsync(string path);

    /// <summary>
    /// Renames or moves a file/directory.
    /// </summary>
    Task<bool> RenameAsync(string oldPath, string newPath);

    /// <summary>
    /// Deletes a file or directory (with confirmation handled by caller).
    /// </summary>
    Task<bool> DeleteAsync(string path);

    /// <summary>
    /// Applies git status to a collection of nodes by matching file paths.
    /// </summary>
    void ApplyGitStatus(IEnumerable<FileSystemNode> nodes, IEnumerable<GitFileStatus> gitFiles, string workingDirectory);

    /// <summary>
    /// Starts watching a directory for changes. Returns a disposable to stop watching.
    /// </summary>
    IDisposable WatchDirectory(string path, Action<FileSystemWatcherEventArgs> onChange);
}

public class FileSystemWatcherEventArgs
{
    public required string Path { get; init; }
    public required WatcherChangeTypes ChangeType { get; init; }
    public string? OldPath { get; init; }
}
