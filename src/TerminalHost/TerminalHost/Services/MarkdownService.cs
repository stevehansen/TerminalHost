using ColorCode;
using Markdig;
using Markdig.SyntaxHighlighting;
using CCColor = ColorCode.Styling.Color;

namespace TerminalHost.Services;

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

    public string ConvertToHtml(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return WrapHtml("");

        var html = Markdig.Markdown.ToHtml(markdown, _pipeline);
        return WrapHtml(html);
    }

    public async Task<string> ConvertFileToHtmlAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !_fileSystem.FileExists(filePath))
            return WrapHtml("<p style='color: #f44747;'>File not found</p>");

        try
        {
            var markdown = await _fileSystem.ReadAllTextAsync(filePath);
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