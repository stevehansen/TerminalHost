using System.Text;
using System.Text.RegularExpressions;
using ColorCode;
using Markdig;
using Markdig.Extensions.Yaml;
using Markdig.Renderers.Html;
using Markdig.SyntaxHighlighting;
using Markdig.Syntax;
using TerminalHost.Core.Interfaces;
using YamlDotNet.Serialization;
using CCColor = ColorCode.Styling.Color;

namespace TerminalHost.Core.Services;

/// <summary>
/// Service for converting Markdown to HTML using Markdig.
/// </summary>
public class MarkdownService : IMarkdownService
{
    private readonly MarkdownPipeline _pipeline;
    private readonly IFileSystem _fileSystem;

    private static readonly IStyleSheet DarkStyleSheet = new DarkStyleSheet();

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
        /* Frontmatter table styling */
        .frontmatter {
            background-color: #252526;
            border: 1px solid #3c3c3c;
            border-radius: 6px;
            padding: 12px 16px;
            margin-bottom: 24px;
            font-size: 0.9em;
        }
        .frontmatter table {
            margin: 0;
            width: auto;
        }
        .frontmatter th {
            background: none;
            color: #9cdcfe;
            font-weight: normal;
            text-align: right;
            padding-right: 16px;
            border: none;
            white-space: nowrap;
        }
        .frontmatter td {
            border: none;
            color: #ce9178;
        }
        .frontmatter td a {
            color: #569cd6;
        }
        /* Mermaid diagram styling */
        .mermaid {
            background-color: #2d2d2d;
            border-radius: 6px;
            padding: 16px;
            margin: 16px 0;
            text-align: center;
        }
        .mermaid svg {
            max-width: 100%;
            height: auto;
        }
    ";

    public MarkdownService(IFileSystem fileSystem)
    {
        _fileSystem = fileSystem;
        // Configure Markdig pipeline with common extensions
        _pipeline = new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseAlertBlocks()
            .UseYamlFrontMatter()
            .UseSyntaxHighlighting(customCss: DarkStyleSheet)
            .Build();
    }

    public string ConvertToHtml(string markdown, string? basePath = null)
    {
        if (string.IsNullOrEmpty(markdown))
            return WrapHtml("", basePath);

        // Parse the document to extract frontmatter
        var document = Markdown.Parse(markdown, _pipeline);

        // Extract and render frontmatter as table
        var frontmatterHtml = ExtractAndRenderFrontmatter(document, markdown);

        // Convert markdown to HTML
        var html = Markdown.ToHtml(document, _pipeline);

        // Convert mermaid code blocks to mermaid divs
        html = ConvertMermaidBlocks(html);

        // Combine frontmatter and content
        var fullHtml = frontmatterHtml + html;

        return WrapHtml(fullHtml, basePath);
    }

    private const long MaxFileSize = 5 * 1024 * 1024; // 5MB

    public async Task<string> ConvertFileToHtmlAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !_fileSystem.FileExists(filePath))
            return WrapHtml("<p style='color: #f44747;'>File not found</p>", null);

        try
        {
            var fileSize = _fileSystem.GetFileSize(filePath);
            if (fileSize > MaxFileSize)
            {
                var sizeMb = fileSize / (1024.0 * 1024.0);
                return WrapHtml($"<p style='color: #f4b842;'>File too large to render ({sizeMb:F1} MB). Maximum supported size is {MaxFileSize / (1024 * 1024)} MB.</p>", null);
            }

            var markdown = await _fileSystem.ReadAllTextAsync(filePath);
            var basePath = Path.GetDirectoryName(filePath);
            return ConvertToHtml(markdown, basePath);
        }
        catch (Exception ex)
        {
            return WrapHtml($"<p style='color: #f44747;'>Error reading file: {ex.Message}</p>", null);
        }
    }

    private string ExtractAndRenderFrontmatter(MarkdownDocument document, string markdown)
    {
        var yamlBlock = document.Descendants<YamlFrontMatterBlock>().FirstOrDefault();
        if (yamlBlock == null)
            return "";

        try
        {
            // Extract the YAML content from the original markdown
            var yamlContent = markdown.Substring(yamlBlock.Span.Start, yamlBlock.Span.Length);

            // Remove the --- delimiters
            yamlContent = yamlContent.Trim();
            if (yamlContent.StartsWith("---"))
                yamlContent = yamlContent.Substring(3);
            if (yamlContent.EndsWith("---"))
                yamlContent = yamlContent.Substring(0, yamlContent.Length - 3);
            yamlContent = yamlContent.Trim();

            if (string.IsNullOrWhiteSpace(yamlContent))
                return "";

            // Parse YAML to dictionary
            var deserializer = new DeserializerBuilder().Build();
            var yamlObject = deserializer.Deserialize<Dictionary<string, object>>(yamlContent);

            if (yamlObject == null || yamlObject.Count == 0)
                return "";

            // Render as HTML table
            var sb = new StringBuilder();
            sb.AppendLine("<div class='frontmatter'>");
            sb.AppendLine("<table>");

            foreach (var kvp in yamlObject)
            {
                var key = FormatFrontmatterKey(kvp.Key);
                var value = FormatFrontmatterValue(kvp.Value);
                sb.AppendLine($"<tr><th>{HtmlEncode(key)}</th><td>{value}</td></tr>");
            }

            sb.AppendLine("</table>");
            sb.AppendLine("</div>");

            return sb.ToString();
        }
        catch
        {
            // If YAML parsing fails, skip frontmatter rendering
            return "";
        }
    }

    private static string FormatFrontmatterKey(string key)
    {
        // Convert snake_case to Title Case
        return string.Join(" ", key.Split('_')
            .Select(word => char.ToUpper(word[0]) + word.Substring(1).ToLower()));
    }

    private static string FormatFrontmatterValue(object? value)
    {
        if (value == null)
            return "<em>N/A</em>";

        var strValue = value.ToString() ?? "";

        // Handle arrays/lists
        if (value is IList<object> list)
            strValue = string.Join(", ", list);

        // Auto-link URLs
        if (strValue.StartsWith("http://") || strValue.StartsWith("https://"))
            return $"<a href='{HtmlEncode(strValue)}' target='_blank'>{HtmlEncode(strValue)}</a>";

        // Handle N/A values
        if (strValue.Equals("N/A", StringComparison.OrdinalIgnoreCase))
            return "<em>N/A</em>";

        return HtmlEncode(strValue);
    }

    private static string HtmlEncode(string text)
    {
        return System.Net.WebUtility.HtmlEncode(text);
    }

    private static string ConvertMermaidBlocks(string html)
    {
        // Markdig.SyntaxHighlighting renders fenced code blocks as: <div class="lang-xxx editor-colors">content</div>
        var pattern = @"<div class=""lang-mermaid[^""]*"">([\s\S]*?)</div>";

        return Regex.Replace(html, pattern, match =>
        {
            var content = match.Groups[1].Value;
            // Decode HTML entities that Markdig may have encoded
            content = System.Net.WebUtility.HtmlDecode(content);
            return $"<pre class=\"mermaid\">{content}</pre>";
        }, RegexOptions.IgnoreCase);
    }

    private static string ConvertRelativeLinks(string html, string basePath)
    {
        if (string.IsNullOrEmpty(basePath))
            return html;

        // Convert relative href links to absolute file:// URLs
        // Match href="..." where the value doesn't start with http://, https://, mailto:, #, or file://
        var hrefPattern = @"href=""(?!https?://|mailto:|#|file://|javascript:)([^""]+)""";

        html = Regex.Replace(html, hrefPattern, match =>
        {
            var relativePath = match.Groups[1].Value;
            try
            {
                // Decode any HTML entities in the path
                relativePath = System.Net.WebUtility.HtmlDecode(relativePath);

                // Combine with base path and normalize
                var absolutePath = Path.GetFullPath(Path.Combine(basePath, relativePath));
                var fileUrl = new Uri(absolutePath).AbsoluteUri;
                return $"href=\"{fileUrl}\"";
            }
            catch
            {
                // If path processing fails, leave the link as-is
                return match.Value;
            }
        }, RegexOptions.IgnoreCase);

        // Also convert relative src for images
        var srcPattern = @"src=""(?!https?://|data:|file://)([^""]+)""";

        html = Regex.Replace(html, srcPattern, match =>
        {
            var relativePath = match.Groups[1].Value;
            try
            {
                relativePath = System.Net.WebUtility.HtmlDecode(relativePath);
                var absolutePath = Path.GetFullPath(Path.Combine(basePath, relativePath));
                var fileUrl = new Uri(absolutePath).AbsoluteUri;
                return $"src=\"{fileUrl}\"";
            }
            catch
            {
                return match.Value;
            }
        }, RegexOptions.IgnoreCase);

        return html;
    }

    private static string WrapHtml(string content, string? basePath)
    {
        // Convert relative links to absolute file:// URLs (more reliable than base tag)
        if (!string.IsNullOrEmpty(basePath))
        {
            content = ConvertRelativeLinks(content, basePath);
        }

        return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <style>{DarkThemeCss}</style>
</head>
<body>
{content}
<script>
    // Intercept all link clicks and send to C# via WebView2 bridge
    document.addEventListener('click', function(e) {{
        var target = e.target;
        while (target && target.tagName !== 'A') {{
            target = target.parentElement;
        }}
        if (target && target.href) {{
            e.preventDefault();
            e.stopPropagation();
            if (window.chrome && window.chrome.webview) {{
                window.chrome.webview.postMessage(target.href);
            }}
        }}
    }}, true);

    // Load mermaid dynamically and initialize when loaded
    var script = document.createElement('script');
    script.src = 'https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.min.js';
    script.onload = function() {{
        mermaid.initialize({{
            startOnLoad: false,
            theme: 'dark',
            themeVariables: {{
                primaryColor: '#569cd6',
                primaryTextColor: '#d4d4d4',
                primaryBorderColor: '#3c3c3c',
                lineColor: '#808080',
                secondaryColor: '#2d2d2d',
                tertiaryColor: '#252526',
                background: '#1e1e1e',
                mainBkg: '#2d2d2d',
                secondBkg: '#252526',
                border1: '#3c3c3c',
                border2: '#3c3c3c',
                arrowheadColor: '#808080',
                fontFamily: '-apple-system, BlinkMacSystemFont, Segoe UI, Roboto, Arial, sans-serif',
                fontSize: '14px',
                textColor: '#d4d4d4',
                nodeTextColor: '#d4d4d4'
            }},
            flowchart: {{
                useMaxWidth: true,
                htmlLabels: true,
                curve: 'basis'
            }},
            sequence: {{
                useMaxWidth: true,
                mirrorActors: false
            }},
            c4: {{
                useMaxWidth: true
            }}
        }});
        mermaid.run();
    }};
    document.head.appendChild(script);
</script>
</body>
</html>";
    }
}

file sealed class DarkStyleSheet : IStyleSheet
{
    // VS Code Dark+ theme colors
    private static readonly CCColor PlainText = new(212, 212, 212);      // #D4D4D4
    private static readonly CCColor Comment = new(106, 153, 85);         // #6A9955
    private static readonly CCColor String = new(206, 145, 120);         // #CE9178
    private static readonly CCColor Keyword = new(86, 156, 214);         // #569CD6
    private static readonly CCColor ControlKeyword = new(197, 134, 192); // #C586C0
    private static readonly CCColor TypeColor = new(78, 201, 176);       // #4EC9B0
    private static readonly CCColor Number = new(181, 206, 168);         // #B5CEA8
    private static readonly CCColor XmlTag = new(128, 128, 128);         // #808080
    private static readonly CCColor XmlName = new(86, 156, 214);         // #569CD6
    private static readonly CCColor Attribute = new(156, 220, 254);      // #9CDCFE
    private static readonly CCColor AttributeValue = new(206, 145, 120); // #CE9178
    private static readonly CCColor Punctuation = new(212, 212, 212);    // #D4D4D4
    private static readonly CCColor Variable = new(156, 220, 254);       // #9CDCFE
    private static readonly CCColor Function = new(220, 220, 170);       // #DCDCAA
    private static readonly CCColor Namespace = new(78, 201, 176);       // #4EC9B0
    private static readonly CCColor CssProperty = new(156, 220, 254);    // #9CDCFE
    private static readonly CCColor CssValue = new(206, 145, 120);       // #CE9178
    private static readonly CCColor CssSelector = new(215, 186, 125);    // #D7BA7D

    private static readonly StyleDictionary styles;

    public StyleDictionary Styles => styles;

    static DarkStyleSheet()
    {
        styles = new StyleDictionary
        {
            new Style("Plain Text")
            {
                Foreground = PlainText,
                CssClassName = "plainText"
            },
            new Style("HTML Server-Side Script")
            {
                Foreground = Keyword,
                CssClassName = "htmlServerSideScript"
            },
            new Style("HTML Comment")
            {
                Foreground = Comment,
                CssClassName = "htmlComment"
            },
            new Style("Html Tag Delimiter")
            {
                Foreground = XmlTag,
                CssClassName = "htmlTagDelimiter"
            },
            new Style("HTML Element ScopeName")
            {
                Foreground = XmlName,
                CssClassName = "htmlElementName"
            },
            new Style("HTML Attribute ScopeName")
            {
                Foreground = Attribute,
                CssClassName = "htmlAttributeName"
            },
            new Style("HTML Attribute Value")
            {
                Foreground = AttributeValue,
                CssClassName = "htmlAttributeValue"
            },
            new Style("HTML Operator")
            {
                Foreground = Punctuation,
                CssClassName = "htmlOperator"
            },
            new Style("Comment")
            {
                Foreground = Comment,
                CssClassName = "comment"
            },
            new Style("XML Doc Tag")
            {
                Foreground = Comment,
                CssClassName = "xmlDocTag"
            },
            new Style("XML Doc Comment")
            {
                Foreground = Comment,
                CssClassName = "xmlDocComment"
            },
            new Style("String")
            {
                Foreground = String,
                CssClassName = "string"
            },
            new Style("String (C# @ Verbatim)")
            {
                Foreground = String,
                CssClassName = "stringCSharpVerbatim"
            },
            new Style("Keyword")
            {
                Foreground = Keyword,
                CssClassName = "keyword"
            },
            new Style("Preprocessor Keyword")
            {
                Foreground = ControlKeyword,
                CssClassName = "preprocessorKeyword"
            },
            new Style("HTML Entity")
            {
                Foreground = String,
                CssClassName = "htmlEntity"
            },
            new Style("XML Attribute")
            {
                Foreground = Attribute,
                CssClassName = "xmlAttribute"
            },
            new Style("XML Attribute Quotes")
            {
                Foreground = Punctuation,
                CssClassName = "xmlAttributeQuotes"
            },
            new Style("XML Attribute Value")
            {
                Foreground = AttributeValue,
                CssClassName = "xmlAttributeValue"
            },
            new Style("XML CData Section")
            {
                Foreground = String,
                CssClassName = "xmlCDataSection"
            },
            new Style("XML Comment")
            {
                Foreground = Comment,
                CssClassName = "xmlComment"
            },
            new Style("XML Delimiter")
            {
                Foreground = XmlTag,
                CssClassName = "xmlDelimiter"
            },
            new Style("XML Name")
            {
                Foreground = XmlName,
                CssClassName = "xmlName"
            },
            new Style("Class Name")
            {
                Foreground = TypeColor,
                CssClassName = "className"
            },
            new Style("CSS Selector")
            {
                Foreground = CssSelector,
                CssClassName = "cssSelector"
            },
            new Style("CSS Property Name")
            {
                Foreground = CssProperty,
                CssClassName = "cssPropertyName"
            },
            new Style("CSS Property Value")
            {
                Foreground = CssValue,
                CssClassName = "cssPropertyValue"
            },
            new Style("SQL System Function")
            {
                Foreground = Function,
                CssClassName = "sqlSystemFunction"
            },
            new Style("PowerShell PowerShellAttribute")
            {
                Foreground = TypeColor,
                CssClassName = "powershellAttribute"
            },
            new Style("PowerShell Operator")
            {
                Foreground = Punctuation,
                CssClassName = "powershellOperator"
            },
            new Style("PowerShell Type")
            {
                Foreground = TypeColor,
                CssClassName = "powershellType"
            },
            new Style("PowerShell Variable")
            {
                Foreground = Variable,
                CssClassName = "powershellVariable"
            },
            new Style("Type")
            {
                Foreground = TypeColor,
                CssClassName = "type"
            },
            new Style("Type Variable")
            {
                Foreground = TypeColor,
                Italic = true,
                CssClassName = "typeVariable"
            },
            new Style("Name Space")
            {
                Foreground = Namespace,
                CssClassName = "namespace"
            },
            new Style("Constructor")
            {
                Foreground = Function,
                CssClassName = "constructor"
            },
            new Style("Predefined")
            {
                Foreground = TypeColor,
                CssClassName = "predefined"
            },
            new Style("Pseudo Keyword")
            {
                Foreground = Keyword,
                CssClassName = "pseudoKeyword"
            },
            new Style("String Escape")
            {
                Foreground = new CCColor(215, 186, 125), // #D7BA7D - escape sequences
                CssClassName = "stringEscape"
            },
            new Style("Control Keyword")
            {
                Foreground = ControlKeyword,
                CssClassName = "controlKeyword"
            },
            new Style("Number")
            {
                Foreground = Number,
                CssClassName = "number"
            },
            new Style("Operator")
            {
                Foreground = Punctuation,
                CssClassName = "operator"
            },
            new Style("Delimiter")
            {
                Foreground = Punctuation,
                CssClassName = "delimiter"
            },
            new Style("Markdown Header")
            {
                Foreground = Keyword,
                Bold = true,
                CssClassName = "markdownHeader"
            },
            new Style("Markdown Code")
            {
                Foreground = String,
                CssClassName = "markdownCode"
            },
            new Style("Markdown List Item")
            {
                Foreground = Keyword,
                Bold = true,
                CssClassName = "markdownListItem"
            },
            new Style("Markdown Emphasized")
            {
                Foreground = PlainText,
                Italic = true,
                CssClassName = "italic"
            },
            new Style("Markdown Bold")
            {
                Foreground = PlainText,
                Bold = true,
                CssClassName = "bold"
            }
        };
    }
}
