using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Service for running tests and parsing results.
/// </summary>
public interface ITestRunnerService
{
    /// <summary>
    /// Event raised when test output is received.
    /// </summary>
    event EventHandler<string>? OutputReceived;

    /// <summary>
    /// Event raised when test run completes.
    /// </summary>
    event EventHandler<TestRunSummary>? TestRunCompleted;

    /// <summary>
    /// Runs all tests in the specified directory.
    /// </summary>
    Task<List<TestResult>> RunAllTestsAsync(string workingDirectory, ProjectType? projectType = null);

    /// <summary>
    /// Runs tests for a specific file.
    /// </summary>
    Task<List<TestResult>> RunTestsForFileAsync(string workingDirectory, string filePath, ProjectType? projectType = null);

    /// <summary>
    /// Re-runs the last test run.
    /// </summary>
    Task<List<TestResult>> RerunLastTestsAsync(string workingDirectory);

    /// <summary>
    /// Runs only the failed tests from the last run.
    /// </summary>
    Task<List<TestResult>> RunFailedTestsAsync(string workingDirectory, List<TestResult> failedTests, ProjectType? projectType = null);

    /// <summary>
    /// Gets the test command for a project type.
    /// </summary>
    string? GetTestCommand(ProjectType? projectType);

    /// <summary>
    /// Cancels the currently running test.
    /// </summary>
    void CancelCurrentRun();

    /// <summary>
    /// Whether a test run is currently in progress.
    /// </summary>
    bool IsRunning { get; }
}
