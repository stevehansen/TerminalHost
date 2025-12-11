using System.IO;
using System.Text;

namespace TerminalHost.Services;

public class FileEditService
{
    private const int MaxFileSize = 1024 * 1024; // 1MB max for editing

    public FileEditResult LoadFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new FileEditResult
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                Error = "File not found"
            };
        }

        var fileInfo = new FileInfo(filePath);

        if (fileInfo.Length > MaxFileSize)
        {
            return new FileEditResult
            {
                FilePath = filePath,
                FileName = fileInfo.Name,
                FileSize = fileInfo.Length,
                Error = $"File too large ({fileInfo.Length / 1024:N0} KB). Maximum supported size is {MaxFileSize / 1024:N0} KB."
            };
        }

        try
        {
            var (content, encoding) = ReadFileWithEncoding(filePath);
            var lines = content.Split('\n');

            return new FileEditResult
            {
                FilePath = filePath,
                FileName = fileInfo.Name,
                Content = content,
                Encoding = encoding,
                LineCount = lines.Length,
                FileSize = fileInfo.Length,
                IsReadOnly = fileInfo.IsReadOnly
            };
        }
        catch (Exception ex)
        {
            return new FileEditResult
            {
                FilePath = filePath,
                FileName = fileInfo.Name,
                FileSize = fileInfo.Length,
                Error = $"Error reading file: {ex.Message}"
            };
        }
    }

    public FileSaveResult SaveFile(string filePath, string content, Encoding? encoding = null)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Exists && fileInfo.IsReadOnly)
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
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, content, encoding);

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

    private static (string content, Encoding encoding) ReadFileWithEncoding(string filePath)
    {
        // Read file with encoding detection
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = reader.ReadToEnd();
        return (content, reader.CurrentEncoding);
    }
}

public class FileEditResult
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public string? Content { get; init; }
    public string? Error { get; init; }
    public Encoding? Encoding { get; init; }
    public int LineCount { get; init; }
    public long FileSize { get; init; }
    public bool IsReadOnly { get; init; }

    public bool IsSuccess => Content != null && Error == null;
}

public class FileSaveResult
{
    public bool Success { get; init; }
    public string? FilePath { get; init; }
    public string? Error { get; init; }
}
