namespace TerminalHost.Core.Domain;

public static class FileIconMapper
{
    public static string GetIcon(string path, bool isDirectory, bool isExpanded = false)
    {
        if (isDirectory)
        {
            return isExpanded ? "📂" : "📁";
        }

        var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        var fileName = System.IO.Path.GetFileName(path).ToLowerInvariant();

        // Special file names
        return fileName switch
        {
            "readme.md" or "readme.txt" or "readme" => "📖",
            "license" or "license.md" or "license.txt" => "📜",
            ".gitignore" or ".gitattributes" => "🔧",
            ".editorconfig" => "⚙️",
            "dockerfile" or "docker-compose.yml" or "docker-compose.yaml" => "🐳",
            "package.json" or "package-lock.json" => "📦",
            "tsconfig.json" or "jsconfig.json" => "⚙️",
            ".env" or ".env.local" or ".env.development" or ".env.production" => "🔐",
            _ => GetIconByExtension(extension)
        };
    }

    private static string GetIconByExtension(string extension)
    {
        return extension switch
        {
            // Code files
            ".cs" => "🔷",      // C#
            ".vb" => "🔷",      // VB.NET
            ".fs" => "🔷",      // F#
            ".js" => "🟨",      // JavaScript
            ".jsx" => "🟨",
            ".ts" => "🟦",      // TypeScript
            ".tsx" => "🟦",
            ".py" => "🐍",      // Python
            ".rb" => "💎",      // Ruby
            ".go" => "🔵",      // Go
            ".rs" => "🦀",      // Rust
            ".java" => "☕",    // Java
            ".kt" => "🟣",      // Kotlin
            ".swift" => "🍊",   // Swift
            ".php" => "🐘",     // PHP
            ".c" or ".h" => "🔶",
            ".cpp" or ".hpp" or ".cc" => "🔶",

            // Web files
            ".html" or ".htm" => "🌐",
            ".css" or ".scss" or ".sass" or ".less" => "🎨",
            ".vue" => "💚",
            ".svelte" => "🔥",

            // Data/config files
            ".json" => "📋",
            ".xml" => "📋",
            ".yaml" or ".yml" => "📋",
            ".toml" => "📋",
            ".ini" or ".cfg" or ".conf" => "⚙️",
            ".env" => "🔐",

            // Document files
            ".md" or ".markdown" => "📝",
            ".txt" => "📄",
            ".rtf" => "📄",
            ".pdf" => "📕",
            ".doc" or ".docx" => "📘",
            ".xls" or ".xlsx" => "📗",
            ".ppt" or ".pptx" => "📙",
            ".csv" => "📊",

            // Image files
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico" or ".webp" => "🖼️",
            ".svg" => "🎨",
            ".psd" => "🎨",
            ".ai" => "🎨",

            // Audio/Video files
            ".mp3" or ".wav" or ".ogg" or ".flac" or ".aac" => "🎵",
            ".mp4" or ".avi" or ".mkv" or ".mov" or ".webm" => "🎬",

            // Archive files
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".bz2" => "📦",

            // Binary/executable files
            ".exe" or ".msi" => "⚙️",
            ".dll" => "📚",
            ".so" or ".dylib" => "📚",

            // Database files
            ".db" or ".sqlite" or ".sqlite3" => "🗃️",
            ".sql" => "🗄️",

            // Shell/script files
            ".sh" or ".bash" => "🖥️",
            ".ps1" or ".psm1" or ".psd1" => "🖥️",
            ".bat" or ".cmd" => "🖥️",

            // Project files
            ".sln" => "🏗️",
            ".csproj" or ".fsproj" or ".vbproj" => "📐",

            // Certificate/key files
            ".pem" or ".crt" or ".cer" or ".key" => "🔑",

            // Font files
            ".ttf" or ".otf" or ".woff" or ".woff2" => "🔤",

            // Log files
            ".log" => "📜",

            // Diff/patch files
            ".diff" or ".patch" => "📑",

            // Default
            _ => "📄"
        };
    }
}
