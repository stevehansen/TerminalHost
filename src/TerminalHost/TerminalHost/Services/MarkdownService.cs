using System.IO;
using Markdig;

namespace TerminalHost.Services;

/// <summary>
/// Service for converting Markdown to HTML using Markdig.
/// </summary>
public class MarkdownService : IMarkdownService
{
    private readonly MarkdownPipeline _pipeline;

    private const string DarkThemeCss = @"
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif;
            background-color: #1e1e1e;
            color: #d4d4d4;
            line-height: 1.6;
            padding: 20px;
            max-width: 900px;
            margin: 0 auto;
        }
        h1, h2, h3, h4, h5, h6 {
            color: #e0e0e0;
            border-bottom: 1px solid #3c3c3c;
            padding-bottom: 0.3em;
            margin-top: 1.5em;
        }
        h1 { font-size: 2em; }
        h2 { font-size: 1.5em; }
        h3 { font-size: 1.25em; }
        code {
            background-color: #2d2d2d;
            padding: 0.2em 0.4em;
            border-radius: 3px;
            font-family: Consolas, 'Courier New', monospace;
            font-size: 0.9em;
            color: #ce9178;
        }
        pre {
            background-color: #2d2d2d;
            padding: 16px;
            overflow-x: auto;
            border-radius: 6px;
            border: 1px solid #3c3c3c;
        }
        pre code {
            background: none;
            padding: 0;
            color: #d4d4d4;
        }
        blockquote {
            border-left: 4px solid #569cd6;
            margin: 0;
            padding: 0 16px;
            color: #9cdcfe;
        }
        a {
            color: #569cd6;
            text-decoration: none;
        }
        a:hover {
            text-decoration: underline;
        }
        table {
            border-collapse: collapse;
            width: 100%;
            margin: 16px 0;
        }
        th, td {
            border: 1px solid #3c3c3c;
            padding: 8px 12px;
            text-align: left;
        }
        th {
            background-color: #2d2d2d;
            color: #e0e0e0;
        }
        tr:nth-child(even) {
            background-color: #252526;
        }
        ul, ol {
            padding-left: 2em;
        }
        li {
            margin: 0.5em 0;
        }
        hr {
            border: none;
            border-top: 1px solid #3c3c3c;
            margin: 24px 0;
        }
        img {
            max-width: 100%;
            height: auto;
        }
        .task-list-item {
            list-style: none;
            margin-left: -1.5em;
        }
        .task-list-item input {
            margin-right: 0.5em;
        }
    ";

    public MarkdownService()
    {
        // Configure Markdig pipeline with common extensions
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseTaskLists()
            .UseAutoLinks()
            .UseYamlFrontMatter()
            .Build();
    }

    public string ConvertToHtml(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return WrapHtml("");

        var html = Markdown.ToHtml(markdown, _pipeline);
        return WrapHtml(html);
    }

    public async Task<string> ConvertFileToHtmlAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return WrapHtml("<p style='color: #f44747;'>File not found</p>");

        try
        {
            var markdown = await File.ReadAllTextAsync(filePath);
            return ConvertToHtml(markdown);
        }
        catch (Exception ex)
        {
            return WrapHtml($"<p style='color: #f44747;'>Error reading file: {ex.Message}</p>");
        }
    }

    private static string WrapHtml(string content)
    {
        return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>{DarkThemeCss}</style>
</head>
<body>
{content}
</body>
</html>";
    }
}
