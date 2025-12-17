using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using TerminalHost.Domain;

namespace TerminalHost.Services;

/// <summary>
/// Implementation of ITestRunnerService.
/// </summary>
internal sealed partial class TestRunnerService : ITestRunnerService
{
    private Process? _currentProcess;
    private readonly object _lock = new();
    private string? _lastCommand;
    private string? _lastWorkingDirectory;

    public event EventHandler<string>? OutputReceived;
    public event EventHandler<TestRunSummary>? TestRunCompleted;

    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _currentProcess != null && !_currentProcess.HasExited;
            }
        }
    }

    public async Task<List<TestResult>> RunAllTestsAsync(string workingDirectory, ProjectType? projectType = null)
    {
        var command = GetTestCommand(projectType) ?? "dotnet test";
        _lastCommand = command;
        _lastWorkingDirectory = workingDirectory;

        return await RunTestsInternalAsync(workingDirectory, command, projectType);
    }

    public async Task<List<TestResult>> RunTestsForFileAsync(string workingDirectory, string filePath, ProjectType? projectType = null)
    {
        var baseCommand = GetTestCommand(projectType) ?? "dotnet test";
        var command = GetSingleFileCommand(baseCommand, filePath, projectType);
        _lastCommand = command;
        _lastWorkingDirectory = workingDirectory;

        return await RunTestsInternalAsync(workingDirectory, command, projectType);
    }

    public async Task<List<TestResult>> RerunLastTestsAsync(string workingDirectory)
    {
        if (string.IsNullOrEmpty(_lastCommand) || string.IsNullOrEmpty(_lastWorkingDirectory))
        {
            // No previous run, run all tests
            return await RunAllTestsAsync(workingDirectory);
        }

        return await RunTestsInternalAsync(_lastWorkingDirectory, _lastCommand, null);
    }

    public async Task<List<TestResult>> RunFailedTestsAsync(string workingDirectory, List<TestResult> failedTests, ProjectType? projectType = null)
    {
        if (failedTests.Count == 0)
        {
            return [];
        }

        var baseCommand = GetTestCommand(projectType) ?? "dotnet test";

        // For .NET, use --filter to run specific tests
        if (baseCommand.Contains("dotnet test"))
        {
            var filter = string.Join("|", failedTests.Select(t => t.FullName));
            var command = $"{baseCommand} --filter \"FullyQualifiedName~{filter}\"";
            return await RunTestsInternalAsync(workingDirectory, command, projectType);
        }

        // For npm, just rerun all tests (npm doesn't have easy single test filtering)
        return await RunAllTestsAsync(workingDirectory, projectType);
    }

    public string? GetTestCommand(ProjectType? projectType)
    {
        if (projectType == null)
            return "dotnet test";

        // Check if the project type has a custom test command
        return projectType.TestCommand ?? projectType.Id switch
        {
            "dotnet" => "dotnet test",
            "nodejs-npm" => "npm test",
            "nodejs-yarn" => "yarn test",
            "python" => "pytest",
            "rust" => "cargo test",
            "go" => "go test ./...",
            _ => null
        };
    }

    public void CancelCurrentRun()
    {
        lock (_lock)
        {
            if (_currentProcess != null && !_currentProcess.HasExited)
            {
                try
                {
                    _currentProcess.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore kill errors
                }
            }
        }
    }

    private async Task<List<TestResult>> RunTestsInternalAsync(string workingDirectory, string command, ProjectType? projectType)
    {
        var results = new List<TestResult>();
        var output = new StringBuilder();
        var trxPath = Path.Combine(Path.GetTempPath(), $"test-results-{Guid.NewGuid()}.trx");

        // For .NET tests, add TRX logger for structured output
        var isDotNet = command.Contains("dotnet test");
        var actualCommand = isDotNet
            ? $"{command} --logger \"trx;LogFileName={trxPath}\" --no-build"
            : command;

        try
        {
            var (exitCode, outputText) = await RunCommandAsync(workingDirectory, actualCommand);
            output.Append(outputText);

            // Parse results
            if (isDotNet && File.Exists(trxPath))
            {
                results = ParseTrxResults(trxPath);
                File.Delete(trxPath);
            }
            else
            {
                results = ParseConsoleOutput(outputText, projectType);
            }

            // Calculate summary
            var summary = new TestRunSummary
            {
                Total = results.Sum(r => CountTests(r)),
                Passed = results.Sum(r => CountTests(r, TestStatus.Passed)),
                Failed = results.Sum(r => CountTests(r, TestStatus.Failed)),
                Skipped = results.Sum(r => CountTests(r, TestStatus.Skipped)),
                Duration = results.Where(r => r.Duration.HasValue).Aggregate(TimeSpan.Zero, (acc, r) => acc + r.Duration!.Value)
            };

            TestRunCompleted?.Invoke(this, summary);
        }
        catch (Exception ex)
        {
            OutputReceived?.Invoke(this, $"Error running tests: {ex.Message}");
        }

        return results;
    }

    private async Task<(int exitCode, string output)> RunCommandAsync(string workingDirectory, string command)
    {
        var output = new StringBuilder();

        try
        {
            // Determine if this is a shell command or a direct executable
            var psi = new ProcessStartInfo
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Parse command into executable and arguments
            if (command.StartsWith("dotnet "))
            {
                psi.FileName = "dotnet";
                psi.Arguments = command.Substring(7);
            }
            else if (command.StartsWith("npm "))
            {
                psi.FileName = "npm";
                psi.Arguments = command.Substring(4);
            }
            else if (command.StartsWith("cargo "))
            {
                psi.FileName = "cargo";
                psi.Arguments = command.Substring(6);
            }
            else if (command.StartsWith("go "))
            {
                psi.FileName = "go";
                psi.Arguments = command.Substring(3);
            }
            else if (command.StartsWith("pytest"))
            {
                psi.FileName = "pytest";
                psi.Arguments = command.Length > 6 ? command.Substring(7) : "";
            }
            else
            {
                // Default: use cmd to run the command
                psi.FileName = "cmd.exe";
                psi.Arguments = $"/c {command}";
            }

            using var process = new Process { StartInfo = psi };

            lock (_lock)
            {
                _currentProcess = process;
            }

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    output.AppendLine(e.Data);
                    OutputReceived?.Invoke(this, e.Data);
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    output.AppendLine(e.Data);
                    OutputReceived?.Invoke(this, e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            lock (_lock)
            {
                _currentProcess = null;
            }

            return (process.ExitCode, output.ToString());
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                _currentProcess = null;
            }

            return (-1, $"Error: {ex.Message}");
        }
    }

    private static string GetSingleFileCommand(string baseCommand, string filePath, ProjectType? projectType)
    {
        var testClass = Path.GetFileNameWithoutExtension(filePath);

        if (baseCommand.Contains("dotnet test"))
        {
            // For .NET, filter by class name
            return $"{baseCommand} --filter \"FullyQualifiedName~{testClass}\"";
        }

        if (baseCommand.Contains("npm test") || baseCommand.Contains("yarn test"))
        {
            // For npm/yarn, pass the file path
            return $"{baseCommand} -- {filePath}";
        }

        if (baseCommand.Contains("pytest"))
        {
            // For pytest, pass the file path
            return $"{baseCommand} {filePath}";
        }

        return baseCommand;
    }

    private static List<TestResult> ParseTrxResults(string trxPath)
    {
        var results = new List<TestResult>();

        try
        {
            var doc = XDocument.Load(trxPath);
            var ns = doc.Root?.GetDefaultNamespace();

            if (ns == null) return results;

            var testResults = doc.Descendants(ns + "UnitTestResult");

            foreach (var tr in testResults)
            {
                var outcome = tr.Attribute("outcome")?.Value ?? "Unknown";
                var duration = tr.Attribute("duration")?.Value;

                var result = new TestResult
                {
                    Name = tr.Attribute("testName")?.Value ?? "Unknown",
                    FullName = tr.Attribute("testName")?.Value ?? "Unknown",
                    Status = outcome switch
                    {
                        "Passed" => TestStatus.Passed,
                        "Failed" => TestStatus.Failed,
                        "NotExecuted" => TestStatus.Skipped,
                        _ => TestStatus.Unknown
                    }
                };

                if (TimeSpan.TryParse(duration, out var d))
                {
                    result.Duration = d;
                }

                // Get error info if failed
                var errorInfo = tr.Element(ns + "Output")?.Element(ns + "ErrorInfo");
                if (errorInfo != null)
                {
                    result.ErrorMessage = errorInfo.Element(ns + "Message")?.Value;
                    result.StackTrace = errorInfo.Element(ns + "StackTrace")?.Value;
                }

                results.Add(result);
            }
        }
        catch
        {
            // Return empty results on parse error
        }

        return results;
    }

    private static List<TestResult> ParseConsoleOutput(string output, ProjectType? projectType)
    {
        var results = new List<TestResult>();
        var lines = output.Split('\n');

        // .NET test output patterns
        var dotnetPassed = DotNetPassedRegex();
        var dotnetFailed = DotNetFailedRegex();
        var dotnetSkipped = DotNetSkippedRegex();

        // npm/jest patterns
        var jestPassed = JestPassedRegex();
        var jestFailed = JestFailedRegex();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // .NET patterns
            var passedMatch = dotnetPassed.Match(trimmed);
            if (passedMatch.Success)
            {
                results.Add(new TestResult
                {
                    Name = passedMatch.Groups[1].Value,
                    FullName = passedMatch.Groups[1].Value,
                    Status = TestStatus.Passed,
                    Duration = TimeSpan.FromMilliseconds(double.TryParse(passedMatch.Groups[2].Value, out var ms) ? ms : 0)
                });
                continue;
            }

            var failedMatch = dotnetFailed.Match(trimmed);
            if (failedMatch.Success)
            {
                results.Add(new TestResult
                {
                    Name = failedMatch.Groups[1].Value,
                    FullName = failedMatch.Groups[1].Value,
                    Status = TestStatus.Failed
                });
                continue;
            }

            var skippedMatch = dotnetSkipped.Match(trimmed);
            if (skippedMatch.Success)
            {
                results.Add(new TestResult
                {
                    Name = skippedMatch.Groups[1].Value,
                    FullName = skippedMatch.Groups[1].Value,
                    Status = TestStatus.Skipped
                });
                continue;
            }

            // Jest patterns
            var jestPassedMatch = jestPassed.Match(trimmed);
            if (jestPassedMatch.Success)
            {
                results.Add(new TestResult
                {
                    Name = jestPassedMatch.Groups[1].Value,
                    FullName = jestPassedMatch.Groups[1].Value,
                    Status = TestStatus.Passed
                });
                continue;
            }

            var jestFailedMatch = jestFailed.Match(trimmed);
            if (jestFailedMatch.Success)
            {
                results.Add(new TestResult
                {
                    Name = jestFailedMatch.Groups[1].Value,
                    FullName = jestFailedMatch.Groups[1].Value,
                    Status = TestStatus.Failed
                });
            }
        }

        return results;
    }

    private static int CountTests(TestResult result, TestStatus? status = null)
    {
        if (result.Children.Count == 0)
        {
            return status == null || result.Status == status ? 1 : 0;
        }

        return result.Children.Sum(c => CountTests(c, status));
    }

    [GeneratedRegex(@"Passed\s+(.+?)\s+\[(\d+)\s*ms\]")]
    private static partial Regex DotNetPassedRegex();

    [GeneratedRegex(@"Failed\s+(.+)")]
    private static partial Regex DotNetFailedRegex();

    [GeneratedRegex(@"Skipped\s+(.+)")]
    private static partial Regex DotNetSkippedRegex();

    [GeneratedRegex(@"✓\s+(.+)")]
    private static partial Regex JestPassedRegex();

    [GeneratedRegex(@"✕\s+(.+)")]
    private static partial Regex JestFailedRegex();
}
