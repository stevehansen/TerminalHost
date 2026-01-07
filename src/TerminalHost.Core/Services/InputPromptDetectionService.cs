using System.Text.RegularExpressions;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Service for detecting when a terminal is waiting for user input based on output patterns.
/// </summary>
public class InputPromptDetectionService : IInputPromptDetectionService
{
    private readonly IConfigurationService _configurationService;
    private List<(InputPromptPattern Pattern, Regex Regex)> _compiledPatterns = [];
    private InputPromptSettings _settings = new();

    public InputPromptDetectionService(IConfigurationService configurationService)
    {
        _configurationService = configurationService;
        ReloadPatterns();
    }

    public WaitingIndicatorStyle IndicatorStyle => _settings.IndicatorStyle;
    public string IndicatorColor => _settings.IndicatorColor;
    public bool IsEnabled => _settings.Enabled;
    public int MinIdleTimeMs => _settings.MinIdleTimeMs;

    public void ReloadPatterns()
    {
        var config = _configurationService.Load();
        _settings = config.Settings.InputPrompt;

        // Combine default patterns with custom patterns
        var allPatterns = new List<InputPromptPattern>();

        // Add defaults (unless disabled)
        foreach (var defaultPattern in InputPromptPattern.GetDefaults())
        {
            if (!_settings.DisabledPatternIds.Contains(defaultPattern.Id))
            {
                allPatterns.Add(defaultPattern);
            }
        }

        // Add custom patterns
        allPatterns.AddRange(_settings.CustomPatterns);

        // Filter enabled and sort by priority (descending)
        var enabledPatterns = allPatterns
            .Where(p => p.Enabled)
            .OrderByDescending(p => p.Priority)
            .ToList();

        // Compile regex patterns
        _compiledPatterns = [];
        foreach (var pattern in enabledPatterns)
        {
            try
            {
                var regex = new Regex(
                    pattern.Pattern,
                    RegexOptions.Compiled | RegexOptions.Multiline,
                    TimeSpan.FromMilliseconds(100) // Timeout to prevent ReDoS
                );
                _compiledPatterns.Add((pattern, regex));
            }
            catch (ArgumentException)
            {
                // Invalid regex pattern - skip it
            }
        }
    }

    public bool IsWaitingForInput(string recentOutput, int? lookbackLines = null)
    {
        if (!_settings.Enabled || string.IsNullOrEmpty(recentOutput))
            return false;

        return GetMatchingPattern(recentOutput, lookbackLines) != null;
    }

    public InputPromptPattern? GetMatchingPattern(string recentOutput, int? lookbackLines = null)
    {
        if (!_settings.Enabled || string.IsNullOrEmpty(recentOutput))
            return null;

        var linesToCheck = lookbackLines ?? _settings.LookbackLines;
        var lastLines = GetLastLines(recentOutput, linesToCheck);

        if (string.IsNullOrWhiteSpace(lastLines))
            return null;

        foreach (var (pattern, regex) in _compiledPatterns)
        {
            try
            {
                if (regex.IsMatch(lastLines))
                {
                    return pattern;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                // Pattern took too long - skip it
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the last N lines from the output string.
    /// </summary>
    private static string GetLastLines(string text, int lineCount)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // Split into lines (handle both \n and \r\n)
        var lines = text.Split(["\r\n", "\n"], StringSplitOptions.None);

        // Remove trailing empty lines (common after output)
        var nonEmptyEndIndex = lines.Length - 1;
        while (nonEmptyEndIndex >= 0 && string.IsNullOrWhiteSpace(lines[nonEmptyEndIndex]))
        {
            nonEmptyEndIndex--;
        }

        if (nonEmptyEndIndex < 0)
            return string.Empty;

        // Get the last N non-trailing-empty lines
        var startIndex = Math.Max(0, nonEmptyEndIndex - lineCount + 1);
        var selectedLines = lines.Skip(startIndex).Take(nonEmptyEndIndex - startIndex + 1);

        return string.Join("\n", selectedLines);
    }
}
