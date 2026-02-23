namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Result from an AI execution operation.
/// </summary>
public record AiExecutionResult(
    bool Success,
    string? Output,
    string? Error,
    string? FullStdout,
    string? FullStderr,
    int ExitCode,
    bool TimedOut);

/// <summary>
/// Centralized service for executing AI assistant commands with progress feedback and error handling.
/// </summary>
public interface IAiExecutionService
{
    /// <summary>
    /// Executes an AI prompt with streaming progress toast and error dialog on failure.
    /// </summary>
    /// <param name="prompt">The prompt to send via stdin.</param>
    /// <param name="workingDirectory">Working directory for the AI process.</param>
    /// <param name="operationLabel">Human-readable label for progress (e.g., "Explaining commit").</param>
    /// <param name="timeout">Timeout (defaults to 30 seconds).</param>
    /// <param name="showErrorOnFailure">Whether to auto-show error dialog on failure (default: true).</param>
    /// <returns>Execution result with output and error details.</returns>
    Task<AiExecutionResult> ExecuteAsync(
        string prompt,
        string workingDirectory,
        string operationLabel,
        TimeSpan? timeout = null,
        bool showErrorOnFailure = true);

    /// <summary>
    /// Checks if an AI assistant is configured and available.
    /// Shows a warning toast if not configured.
    /// </summary>
    /// <returns>True if AI is available, false otherwise.</returns>
    bool IsAiAvailable();
}
