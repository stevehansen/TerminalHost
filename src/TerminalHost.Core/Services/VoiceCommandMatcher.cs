using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Services;

/// <summary>
/// Matches speech transcripts to registered voice commands using normalization,
/// alias lookup, and fuzzy matching. Platform-agnostic — used by all voice service implementations.
/// Also detects meta-intents: send-to-AI prefixes/suffixes, confirm/cancel keywords.
/// </summary>
public class VoiceCommandMatcher
{
    private IReadOnlyList<VoiceCommandEntry> _commands = [];

    /// <summary>
    /// Filler words to strip from transcripts before matching.
    /// </summary>
    private static readonly HashSet<string> FillerWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "um", "uh", "please", "can", "you", "could", "would",
        "the", "a", "an", "just", "like", "okay", "ok", "so"
    };

    /// <summary>
    /// Prefixes that signal "send the rest to the AI terminal".
    /// </summary>
    private static readonly string[] SendToAiPrefixes =
    [
        "send to claude",
        "send to ai",
        "tell ai",
        "tell claude",
        "instruct ai",
        "instruct claude",
        "ask claude",
        "ask ai",
        "type"
    ];

    /// <summary>
    /// Suffixes that signal "send everything before this to the AI terminal".
    /// </summary>
    private static readonly string[] SendToAiSuffixes =
    [
        "send to ai",
        "send to claude",
        "send it"
    ];

    /// <summary>
    /// Words that confirm the current pending action.
    /// </summary>
    private static readonly HashSet<string> ConfirmWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "yes", "yep", "yeah", "confirm", "go", "do it", "execute", "run it", "proceed"
    };

    /// <summary>
    /// Words that cancel the current pending action.
    /// </summary>
    private static readonly HashSet<string> CancelWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "no", "nope", "cancel", "stop", "nevermind", "never mind", "abort", "dismiss", "close"
    };

    /// <summary>
    /// Update the command vocabulary for matching.
    /// </summary>
    public void SetCommands(IReadOnlyList<VoiceCommandEntry> commands)
    {
        _commands = commands;
    }

    /// <summary>
    /// Match a speech transcript to the best command, or detect a meta-intent.
    /// </summary>
    /// <param name="transcript">Raw speech-to-text output.</param>
    /// <param name="confidenceThreshold">Minimum confidence to consider a match.</param>
    /// <returns>Match result with intent, best match, and alternatives.</returns>
    public VoiceCommandResult Match(string transcript, float confidenceThreshold = 0.8f)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return new VoiceCommandResult
            {
                Transcript = transcript ?? "",
                ConfidenceThreshold = confidenceThreshold
            };
        }

        var lower = transcript.Trim().ToLowerInvariant();

        // Check for confirm/cancel (short utterances during Preview state)
        if (ConfirmWords.Contains(lower))
        {
            return new VoiceCommandResult
            {
                Transcript = transcript,
                Intent = VoiceIntent.Confirm,
                ConfidenceThreshold = confidenceThreshold
            };
        }

        if (CancelWords.Contains(lower))
        {
            return new VoiceCommandResult
            {
                Transcript = transcript,
                Intent = VoiceIntent.Cancel,
                ConfidenceThreshold = confidenceThreshold
            };
        }

        // Check for send-to-AI prefixes: "send to claude: fix the login bug"
        foreach (var prefix in SendToAiPrefixes)
        {
            if (lower.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var message = transcript[prefix.Length..].TrimStart(':', ' ', ',');
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return new VoiceCommandResult
                    {
                        Transcript = transcript,
                        Intent = VoiceIntent.SendToAi,
                        AiMessage = message,
                        ConfidenceThreshold = confidenceThreshold
                    };
                }
            }
        }

        // Check for send-to-AI suffixes: "fix the login bug, send to AI"
        foreach (var suffix in SendToAiSuffixes)
        {
            if (lower.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                var message = transcript[..^suffix.Length].TrimEnd(',', ' ', '.', '-');
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return new VoiceCommandResult
                    {
                        Transcript = transcript,
                        Intent = VoiceIntent.SendToAi,
                        AiMessage = message,
                        ConfidenceThreshold = confidenceThreshold
                    };
                }
            }
        }

        // Normal command matching
        if (_commands.Count == 0)
        {
            return new VoiceCommandResult
            {
                Transcript = transcript,
                ConfidenceThreshold = confidenceThreshold
            };
        }

        var normalized = Normalize(transcript);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new VoiceCommandResult
            {
                Transcript = transcript,
                ConfidenceThreshold = confidenceThreshold
            };
        }

        var matches = new List<VoiceCommandMatch>();

        foreach (var command in _commands)
        {
            var bestConfidence = 0f;

            foreach (var phrase in command.AllPhrases)
            {
                var phraseNormalized = Normalize(phrase);
                var confidence = CalculateConfidence(normalized, phraseNormalized);
                if (confidence > bestConfidence)
                    bestConfidence = confidence;
            }

            if (bestConfidence > 0.3f) // Only include plausible matches
            {
                matches.Add(new VoiceCommandMatch(command, bestConfidence));
            }
        }

        matches.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));

        var bestMatch = matches.Count > 0 ? matches[0] : null;
        var alternatives = matches.Skip(1).Take(3).ToList();

        return new VoiceCommandResult
        {
            Transcript = transcript,
            BestMatch = bestMatch,
            Alternatives = alternatives,
            ConfidenceThreshold = confidenceThreshold
        };
    }

    /// <summary>
    /// Normalize text for matching: lowercase, strip filler words, trim whitespace.
    /// </summary>
    internal static string Normalize(string text)
    {
        var lower = text.ToLowerInvariant().Trim();
        var words = lower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var filtered = words.Where(w => !FillerWords.Contains(w)).ToArray();

        // If all words were filler, use original words
        return filtered.Length > 0
            ? string.Join(" ", filtered)
            : string.Join(" ", words);
    }

    /// <summary>
    /// Calculate match confidence between a normalized transcript and a command phrase.
    /// Returns 0.0 to 1.0.
    /// </summary>
    internal static float CalculateConfidence(string transcript, string phrase)
    {
        // Exact match
        if (transcript == phrase)
            return 1.0f;

        // Contains check (transcript contains the full phrase)
        if (transcript.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            return 0.95f;

        // Phrase contains transcript (user said a subset)
        if (phrase.Contains(transcript, StringComparison.OrdinalIgnoreCase))
        {
            // Score based on how much of the phrase was said
            var ratio = (float)transcript.Length / phrase.Length;
            return 0.5f + (ratio * 0.4f); // 0.5 to 0.9 depending on coverage
        }

        // Fuzzy match using Levenshtein distance
        var distance = LevenshteinDistance(transcript, phrase);
        var maxLen = Math.Max(transcript.Length, phrase.Length);
        if (maxLen == 0) return 0f;

        var similarity = 1.0f - ((float)distance / maxLen);
        return similarity;
    }

    /// <summary>
    /// Compute Levenshtein edit distance between two strings.
    /// </summary>
    internal static int LevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source)) return target?.Length ?? 0;
        if (string.IsNullOrEmpty(target)) return source.Length;

        var sourceLen = source.Length;
        var targetLen = target.Length;

        // Use single-row optimization
        var previousRow = new int[targetLen + 1];
        var currentRow = new int[targetLen + 1];

        for (var j = 0; j <= targetLen; j++)
            previousRow[j] = j;

        for (var i = 1; i <= sourceLen; i++)
        {
            currentRow[0] = i;

            for (var j = 1; j <= targetLen; j++)
            {
                var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                currentRow[j] = Math.Min(
                    Math.Min(currentRow[j - 1] + 1, previousRow[j] + 1),
                    previousRow[j - 1] + cost);
            }

            (previousRow, currentRow) = (currentRow, previousRow);
        }

        return previousRow[targetLen];
    }
}
