namespace TerminalHost.Core.Domain;

/// <summary>
/// Result of loading a file preview.
/// </summary>
public class FilePreviewResult
{
    /// <summary>Full path to the file.</summary>
    public required string FilePath { get; init; }

    /// <summary>File name without path.</summary>
    public required string FileName { get; init; }

    /// <summary>Raw text content of the file.</summary>
    public string? Content { get; init; }

    /// <summary>Number of lines in the file.</summary>
    public int LineCount { get; init; }

    /// <summary>File size in bytes.</summary>
    public long FileSize { get; init; }

    /// <summary>Line number to highlight (1-based).</summary>
    public int? HighlightLine { get; init; }

    /// <summary>Error message if loading failed.</summary>
    public string? Error { get; init; }

    /// <summary>Whether the preview loaded successfully.</summary>
    public bool IsSuccess => Error == null && Content != null;

    /// <summary>
    /// Platform-specific document object for syntax highlighting.
    /// On WPF this would be FlowDocument, on Avalonia it would be
    /// a different type. Can be null for cross-platform usage.
    /// </summary>
    public object? Document { get; set; }
}
