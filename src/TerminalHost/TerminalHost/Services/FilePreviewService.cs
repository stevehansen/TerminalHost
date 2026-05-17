using System.IO;
using System.Text;
using System.Windows.Documents;
using TerminalHost.Core.Services;
using TerminalHost.Services.SyntaxHighlighting;

namespace TerminalHost.Services;

internal sealed class FilePreviewService : IFilePreviewService
{
    private const int MaxFileSize = 1024 * 1024; // 1MB max
    private const int MaxLines = 1000;

    private readonly Dictionary<string, ISyntaxHighlighter> _highlighters;
    private readonly PlainTextHighlighter _fallbackHighlighter;
    private readonly IFileSystem _fileSystem;

    public FilePreviewService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
        _fallbackHighlighter = new PlainTextHighlighter();

        var highlighterList = new ISyntaxHighlighter[]
        {
            new JsonHighlighter(),
            new CSharpHighlighter(),
            new XmlHighlighter(),
            new MarkdownHighlighter(),
            new PythonHighlighter(),
            new JavaScriptHighlighter(),
            new CsvHighlighter(),
            new TsvHighlighter(),
            new DiffHighlighter(),
            _fallbackHighlighter
        };

        _highlighters = new Dictionary<string, ISyntaxHighlighter>(StringComparer.OrdinalIgnoreCase);

        foreach (var highlighter in highlighterList)
        {
            foreach (var ext in highlighter.SupportedExtensions)
            {
                _highlighters[ext] = highlighter;
            }
        }
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

            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            var highlighter = GetHighlighter(extension);
            var document = highlighter.CreateHighlightedDocument(content, highlightLine);

            return new FilePreviewResult
            {
                FilePath = filePath,
                FileName = fileName,
                Document = document,
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

    private ISyntaxHighlighter GetHighlighter(string extension)
    {
        return _highlighters.TryGetValue(extension, out var highlighter)
            ? highlighter
            : _fallbackHighlighter;
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
    public FlowDocument? Document { get; init; }
    public string? Content { get; init; }
    public string? Error { get; init; }
    public int LineCount { get; init; }
    public long FileSize { get; init; }
    public int? HighlightLine { get; init; }

    public bool IsSuccess => Document != null && Error == null;
}