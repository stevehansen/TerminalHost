using TerminalHost.Core.Interfaces;
using System.IO;
using TerminalHost.Core.Interfaces;
using System.Text;
using TerminalHost.Core.Services;

namespace TerminalHost.Services;

internal sealed class FilePreviewService : IFilePreviewService
{
    private const int MaxFileSize = 1024 * 1024; // 1MB max
    private const int MaxLines = 1000;

    private readonly IFileSystem _fileSystem;

    public FilePreviewService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
    }

    public FilePreviewResult? LoadFilePreview(string filePath, int? highlightLine = null)
    {
        if (!_fileSystem.FileExists(filePath))
        {
            return null;
        }

        var fileSize = _fileSystem.GetFileSize(filePath);
        var fileName = Path.GetFileName(filePath);

        if (fileSize > MaxFileSize)
        {
            return new FilePreviewResult
            {
                FilePath = filePath,
                FileName = fileName,
                FileSize = fileSize,
                Error = $"File too large ({fileSize / 1024:N0} KB). Maximum supported size is {MaxFileSize / 1024:N0} KB."
            };
        }

        try
        {
            var content = ReadFileContent(filePath);
            var lines = content.Split('\n');

            if (lines.Length > MaxLines)
            {
                content = string.Join('\n', lines.Take(MaxLines));
                content += $"\n\n... (truncated, showing first {MaxLines} of {lines.Length} lines)";
            }

            return new FilePreviewResult
            {
                FilePath = filePath,
                FileName = fileName,
                Content = content,
                LineCount = lines.Length,
                FileSize = fileSize,
                HighlightLine = highlightLine
            };
        }
        catch (Exception ex)
        {
            return new FilePreviewResult
            {
                FilePath = filePath,
                FileName = fileName,
                FileSize = fileSize,
                Error = $"Error reading file: {ex.Message}"
            };
        }
    }

    private string ReadFileContent(string filePath)
    {
        // Try to detect encoding
        using var stream = _fileSystem.OpenRead(filePath);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    public static (string path, int? line, int? column) ParseFilePathWithPosition(string input)
        => FilePathPositionParser.Parse(input);
}

public class FilePreviewResult
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public string? Content { get; init; }
    public string? Error { get; init; }
    public int LineCount { get; init; }
    public long FileSize { get; init; }
    public int? HighlightLine { get; init; }

    public bool IsSuccess => Content != null && Error == null;
}