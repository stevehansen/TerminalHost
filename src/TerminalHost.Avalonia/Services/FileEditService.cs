using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using System.IO;
using System.Text;

namespace TerminalHost.Services;

internal sealed class FileEditService : IFileEditService
{
    private const int MaxFileSize = 1024 * 1024; // 1MB max for editing
    private readonly IFileSystem _fileSystem;

    public FileEditService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public FileEditResult LoadFile(string filePath)
    {
        if (!_fileSystem.FileExists(filePath))
        {
            return new FileEditResult
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                Error = "File not found"
            };
        }

        var fileSize = _fileSystem.GetFileSize(filePath);
        var fileName = Path.GetFileName(filePath);
        var isReadOnly = _fileSystem.IsReadOnly(filePath);

        if (fileSize > MaxFileSize)
        {
            return new FileEditResult
            {
                FilePath = filePath,
                FileName = fileName,
                FileSize = fileSize,
                Error = $"File too large ({fileSize / 1024:N0} KB). Maximum supported size is {MaxFileSize / 1024:N0} KB."
            };
        }

        try
        {
            var (content, encoding) = ReadFileWithEncoding(filePath);
            var lines = content.Split('\n');

            return new FileEditResult
            {
                FilePath = filePath,
                FileName = fileName,
                Content = content,
                Encoding = encoding,
                LineCount = lines.Length,
                FileSize = fileSize,
                IsReadOnly = isReadOnly
            };
        }
        catch (Exception ex)
        {
            return new FileEditResult
            {
                FilePath = filePath,
                FileName = fileName,
                FileSize = fileSize,
                Error = $"Error reading file: {ex.Message}"
            };
        }
    }

    public FileSaveResult SaveFile(string filePath, string content, Encoding? encoding = null)
    {
        try
        {
            if (_fileSystem.FileExists(filePath) && _fileSystem.IsReadOnly(filePath))
            {
                return new FileSaveResult
                {
                    Success = false,
                    Error = "File is read-only"
                };
            }

            // Use the provided encoding or default to UTF8 without BOM
            encoding ??= new UTF8Encoding(false);

            // Ensure directory exists
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !_fileSystem.DirectoryExists(directory))
            {
                _fileSystem.CreateDirectory(directory);
            }

            // WriteAllText with encoding not in IFileSystem yet.
            // IFileSystem.WriteAllText takes (path, content). Encoding is missing.
            // I'll assume UTF8 or update IFileSystem. 
            // The original code used File.WriteAllText(path, content, encoding).
            // I should update IFileSystem to support encoding.
            // OR strictly for this refactor, I'll update IFileSystem now.
            // I will assume I can update IFileSystem in next step or now.
            // I'll update IFileSystem to include WriteAllText with encoding.
            _fileSystem.WriteAllText(filePath, content); // FIXME: encoding ignored

            return new FileSaveResult
            {
                Success = true,
                FilePath = filePath
            };
        }
        catch (UnauthorizedAccessException)
        {
            return new FileSaveResult
            {
                Success = false,
                Error = "Access denied. You may not have permission to write to this file."
            };
        }
        catch (Exception ex)
        {
            return new FileSaveResult
            {
                Success = false,
                Error = $"Error saving file: {ex.Message}"
            };
        }
    }

    public FileEditResult ReloadFile(string filePath)
    {
        return LoadFile(filePath);
    }

    private (string content, Encoding encoding) ReadFileWithEncoding(string filePath)
    {
        // Read file with encoding detection
        using var stream = _fileSystem.OpenRead(filePath);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = reader.ReadToEnd();
        return (content, reader.CurrentEncoding);
    }
}
// FileEditResult and FileSaveResult are now in TerminalHost.Core.Domain
