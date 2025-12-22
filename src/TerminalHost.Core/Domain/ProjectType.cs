using System.Text.Json.Serialization;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Represents a project type that can be auto-detected and has default run configurations.
/// </summary>
public class ProjectType
{
    /// <summary>
    /// Unique identifier for this project type (e.g., "dotnet", "nodejs").
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>
    /// Display name (e.g., ".NET", "Node.js").
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// File patterns to detect this project type (e.g., ["*.csproj", "*.sln"]).
    /// </summary>
    [JsonPropertyName("detectFiles")]
    public string[] DetectFiles { get; set; } = [];

    /// <summary>
    /// Default command to run the project (e.g., "dotnet run").
    /// </summary>
    [JsonPropertyName("defaultCommand")]
    public string DefaultCommand { get; set; } = "";

    /// <summary>
    /// Optional watch/dev command (e.g., "dotnet watch run").
    /// </summary>
    [JsonPropertyName("watchCommand")]
    public string? WatchCommand { get; set; }

    /// <summary>
    /// Optional regex pattern to detect localhost URLs from output.
    /// </summary>
    [JsonPropertyName("urlPattern")]
    public string? UrlPattern { get; set; }

    /// <summary>
    /// Detection priority. Higher values are checked first.
    /// </summary>
    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 0;

    /// <summary>
    /// Command to run tests (e.g., "dotnet test", "npm test").
    /// </summary>
    [JsonPropertyName("testCommand")]
    public string? TestCommand { get; set; }

    /// <summary>
    /// Glob pattern for test files (e.g., "**/*Tests.cs", "**/*.test.ts").
    /// </summary>
    [JsonPropertyName("testFilePattern")]
    public string? TestFilePattern { get; set; }

    /// <summary>
    /// Command to run tests for a single file. Use {file} as placeholder.
    /// </summary>
    [JsonPropertyName("testSingleFileCommand")]
    public string? TestSingleFileCommand { get; set; }

    /// <summary>
    /// Gets the default project types for common frameworks.
    /// </summary>
    public static List<ProjectType> GetDefaults() =>
    [
        new ProjectType
        {
            Id = "dotnet",
            Name = ".NET",
            DetectFiles = ["*.csproj", "*.sln"],
            DefaultCommand = "dotnet run",
            WatchCommand = "dotnet watch run",
            UrlPattern = @"Now listening on: (https?://[^\s]+)",
            Priority = 10,
            TestCommand = "dotnet test",
            TestFilePattern = "**/*Tests.cs",
            TestSingleFileCommand = "dotnet test --filter \"FullyQualifiedName~{testClass}\""
        },
        new ProjectType
        {
            Id = "nodejs-npm",
            Name = "Node.js (npm)",
            DetectFiles = ["package.json"],
            DefaultCommand = "npm start",
            WatchCommand = "npm run dev",
            UrlPattern = @"(?:Local|http):\s*(https?://(?:localhost|127\.0\.0\.1)[:\d]*)",
            Priority = 5,
            TestCommand = "npm test",
            TestFilePattern = "**/*.test.{js,ts,jsx,tsx}",
            TestSingleFileCommand = "npm test -- {file}"
        },
        new ProjectType
        {
            Id = "python",
            Name = "Python",
            DetectFiles = ["requirements.txt", "setup.py", "pyproject.toml"],
            DefaultCommand = "python main.py",
            WatchCommand = "flask run",
            UrlPattern = @"Running on (https?://[^\s]+)",
            Priority = 3,
            TestCommand = "pytest",
            TestFilePattern = "**/test_*.py",
            TestSingleFileCommand = "pytest {file}"
        },
        new ProjectType
        {
            Id = "rust",
            Name = "Rust",
            DetectFiles = ["Cargo.toml"],
            DefaultCommand = "cargo run",
            WatchCommand = "cargo watch -x run",
            UrlPattern = @"(https?://(?:localhost|127\.0\.0\.1):\d+)",
            Priority = 4,
            TestCommand = "cargo test",
            TestFilePattern = "**/tests/**/*.rs"
        },
        new ProjectType
        {
            Id = "go",
            Name = "Go",
            DetectFiles = ["go.mod"],
            DefaultCommand = "go run .",
            WatchCommand = null,
            UrlPattern = @"(https?://(?:localhost|127\.0\.0\.1):\d+)",
            Priority = 4,
            TestCommand = "go test ./...",
            TestFilePattern = "**/*_test.go"
        }
    ];
}
