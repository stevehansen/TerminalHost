using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

/// <summary>
/// Service for detecting when a terminal is waiting for user input.
/// </summary>
public interface IInputPromptDetectionService
{
    /// <summary>
    /// Checks if the given terminal output indicates the terminal is waiting for user input.
    /// </summary>
    /// <param name="recentOutput">Recent terminal output to analyze.</param>
    /// <param name="lookbackLines">Number of lines from the end to check (default from settings).</param>
    /// <returns>True if the terminal appears to be waiting for input.</returns>
    bool IsWaitingForInput(string recentOutput, int? lookbackLines = null);

    /// <summary>
    /// Gets the pattern that matched, if any.
    /// </summary>
    /// <param name="recentOutput">Recent terminal output to analyze.</param>
    /// <param name="lookbackLines">Number of lines from the end to check.</param>
    /// <returns>The matching pattern, or null if no match.</returns>
    InputPromptPattern? GetMatchingPattern(string recentOutput, int? lookbackLines = null);

    /// <summary>
    /// Reloads patterns from settings.
    /// </summary>
    void ReloadPatterns();

    /// <summary>
    /// Gets the current indicator style setting.
    /// </summary>
    WaitingIndicatorStyle IndicatorStyle { get; }

    /// <summary>
    /// Gets the current indicator color setting.
    /// </summary>
    string IndicatorColor { get; }

    /// <summary>
    /// Gets whether input prompt detection is enabled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Gets the minimum idle time before checking for input prompts.
    /// </summary>
    int MinIdleTimeMs { get; }
}
