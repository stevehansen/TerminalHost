using System.IO;
using TerminalHost.Core.Interfaces;
using TerminalHost.Domain;

namespace TerminalHost.Services;

public class FileExplorerService : IFileExplorerService
{
    private readonly IFileSystem _fileSystem;

    public FileExplorerService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public Task<List<FileSystemNode>> GetChildrenAsync(string directoryPath, bool showHidden = false)
    {
        return Task.Run(() =>
        {
            var nodes = new List<FileSystemNode>();

            if (!_fileSystem.DirectoryExists(directoryPath))
                return nodes;

            try
            {
                // Get directories first
                var directories = _fileSystem.GetDirectories(directoryPath);
                foreach (var dir in directories.OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
                {
                    var dirName = Path.GetFileName(dir);

                    // Skip hidden directories unless requested
                    if (!showHidden && (dirName.StartsWith('.') || IsHiddenSafe(dir)))
                        continue;

                    var node = new FileSystemNode
                    {
                        Name = dirName,
                        FullPath = dir,
                        IsDirectory = true,
                        ChildrenLoaded = false
                    };

                    // Add a dummy child if the directory has children (for lazy loading)
                    if (HasChildren(dir, showHidden))
                    {
                        node.Children.Add(FileSystemNode.CreateDummy());
                    }

                    nodes.Add(node);
                }

                // Get files
                var files = _fileSystem.GetFiles(directoryPath);
                foreach (var file in files.OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                {
                    var fileName = Path.GetFileName(file);

                    // Skip hidden files unless requested
                    if (!showHidden && (fileName.StartsWith('.') || IsHiddenSafe(file)))
                        continue;

                    var node = new FileSystemNode
                    {
                        Name = fileName,
                        FullPath = file,
                        IsDirectory = false,
                        ChildrenLoaded = true // Files don't have children
                    };

                    nodes.Add(node);
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Access denied - return empty list
            }
            catch (IOException)
            {
                // IO error - return empty list
            }

            return nodes;
        });
    }

    private bool HasChildren(string directoryPath, bool showHidden)
    {
        try
        {
            var dirs = _fileSystem.GetDirectories(directoryPath);
            if (dirs.Any(d => showHidden || (!Path.GetFileName(d).StartsWith('.') && !IsHiddenSafe(d))))
                return true;

            var files = _fileSystem.GetFiles(directoryPath);
            return files.Any(f => showHidden || (!Path.GetFileName(f).StartsWith('.') && !IsHiddenSafe(f)));
        }
        catch
        {
            return false;
        }
    }

    private bool IsHiddenSafe(string path)
    {
        try
        {
            return _fileSystem.IsHidden(path);
        }
        catch
        {
            return false;
        }
    }

    public Task<bool> CreateFileAsync(string path)
    {
        return Task.Run(() =>
        {
            try
            {
                if (_fileSystem.FileExists(path))
                    return false;

                _fileSystem.WriteAllText(path, "");
                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    public Task<bool> CreateDirectoryAsync(string path)
    {
        return Task.Run(() =>
        {
            try
            {
                if (_fileSystem.DirectoryExists(path))
                    return false;

                _fileSystem.CreateDirectory(path);
                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    public Task<bool> RenameAsync(string oldPath, string newPath)
    {
        return Task.Run(() =>
        {
            try
            {
                if (oldPath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
                    return true;

                // Check if destination exists
                if (_fileSystem.FileExists(newPath) || _fileSystem.DirectoryExists(newPath))
                    return false;

                _fileSystem.Move(oldPath, newPath);
                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    public Task<bool> DeleteAsync(string path)
    {
        return Task.Run(() =>
        {
            try
            {
                if (_fileSystem.FileExists(path))
                {
                    _fileSystem.DeleteFile(path);
                    return true;
                }

                if (_fileSystem.DirectoryExists(path))
                {
                    _fileSystem.DeleteDirectory(path, recursive: true);
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        });
    }

    public void ApplyGitStatus(IEnumerable<FileSystemNode> nodes, IEnumerable<GitFileStatus> gitFiles, string workingDirectory)
    {
        var gitFileDict = gitFiles.ToDictionary(
            gf => Path.GetFullPath(Path.Combine(workingDirectory, gf.FilePath)).ToLowerInvariant(),
            gf => gf.Status);

        ApplyGitStatusRecursive(nodes, gitFileDict, workingDirectory);
    }

    private void ApplyGitStatusRecursive(IEnumerable<FileSystemNode> nodes, Dictionary<string, GitFileStatusType> gitFileDict, string workingDirectory)
    {
        foreach (var node in nodes)
        {
            var normalizedPath = node.FullPath.ToLowerInvariant();

            if (gitFileDict.TryGetValue(normalizedPath, out var status))
            {
                node.GitStatus = status;
            }
            else
            {
                node.GitStatus = null;
            }

            // For directories, check if any children have git status
            if (node.IsDirectory && node.Children.Any(c => !c.IsLoading))
            {
                ApplyGitStatusRecursive(node.Children.Where(c => !c.IsLoading), gitFileDict, workingDirectory);

                // If any child has status, mark parent directory too (propagate up)
                var hasModifiedChildren = node.Children.Any(c => c.GitStatus != null ||
                    (c.IsDirectory && HasModifiedDescendants(c)));
                if (hasModifiedChildren && node.GitStatus == null)
                {
                    // Don't set a specific status on parent, but this could be used for visual indicator
                }
            }
        }
    }

    private static bool HasModifiedDescendants(FileSystemNode node)
    {
        if (node.GitStatus != null)
            return true;

        return node.Children.Any(c => !c.IsLoading && (c.GitStatus != null || HasModifiedDescendants(c)));
    }

    public IDisposable WatchDirectory(string path, Action<FileSystemWatcherEventArgs> onChange)
    {
        var watcher = new FileSystemWatcher(path)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
            IncludeSubdirectories = true,
            EnableRaisingEvents = true
        };

        void OnChange(object sender, FileSystemEventArgs e)
        {
            onChange(new FileSystemWatcherEventArgs
            {
                Path = e.FullPath,
                ChangeType = e.ChangeType
            });
        }

        void OnRename(object sender, RenamedEventArgs e)
        {
            onChange(new FileSystemWatcherEventArgs
            {
                Path = e.FullPath,
                ChangeType = e.ChangeType,
                OldPath = e.OldFullPath
            });
        }

        watcher.Created += OnChange;
        watcher.Deleted += OnChange;
        watcher.Changed += OnChange;
        watcher.Renamed += OnRename;

        return new FileWatcherDisposable(watcher);
    }

    private class FileWatcherDisposable : IDisposable
    {
        private readonly FileSystemWatcher _watcher;
        private bool _disposed;

        public FileWatcherDisposable(FileSystemWatcher watcher)
        {
            _watcher = watcher;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _disposed = true;
            }
        }
    }
}
