namespace TerminalHost.Core.Services;

/// <summary>
/// Parses a file path that may carry a trailing line/column suffix
/// (e.g., "file.cs:42:15", "C:\path\file.cs:42"). Pure string manipulation — no IO.
/// </summary>
public static class FilePathPositionParser
{
    public static (string path, int? line, int? column) Parse(string input)
    {
        var parts = input.Split(':');

        string path;
        int startIndex;

        if (parts.Length >= 2 && parts[0].Length == 1 && char.IsLetter(parts[0][0]))
        {
            path = parts[0] + ":" + parts[1];
            startIndex = 2;
        }
        else
        {
            path = parts[0];
            startIndex = 1;
        }

        int? line = null;
        int? column = null;

        if (parts.Length > startIndex && int.TryParse(parts[startIndex], out var parsedLine))
        {
            line = parsedLine;

            if (parts.Length > startIndex + 1 && int.TryParse(parts[startIndex + 1], out var parsedColumn))
            {
                column = parsedColumn;
            }
        }

        return (path, line, column);
    }
}
