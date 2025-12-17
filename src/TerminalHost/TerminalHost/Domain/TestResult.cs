namespace TerminalHost.Domain;

/// <summary>
/// Status of a test execution.
/// </summary>
public enum TestStatus
{
    /// <summary>
    /// Test passed successfully.
    /// </summary>
    Passed,

    /// <summary>
    /// Test failed.
    /// </summary>
    Failed,

    /// <summary>
    /// Test was skipped.
    /// </summary>
    Skipped,

    /// <summary>
    /// Test is currently running.
    /// </summary>
    Running,

    /// <summary>
    /// Test status unknown.
    /// </summary>
    Unknown
}

/// <summary>
/// Represents a test result from a test run.
/// </summary>
public class TestResult
{
    /// <summary>
    /// The test name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The fully qualified test name (namespace.class.method).
    /// </summary>
    public string FullName { get; set; } = "";

    /// <summary>
    /// The source file path containing the test.
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// The line number in the source file.
    /// </summary>
    public int? Line { get; set; }

    /// <summary>
    /// The test execution status.
    /// </summary>
    public TestStatus Status { get; set; } = TestStatus.Unknown;

    /// <summary>
    /// Error message if the test failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Stack trace if the test failed.
    /// </summary>
    public string? StackTrace { get; set; }

    /// <summary>
    /// Test execution duration.
    /// </summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>
    /// Child test results (for test suites/classes).
    /// </summary>
    public List<TestResult> Children { get; set; } = [];

    /// <summary>
    /// Gets a display name (just the test method name if available).
    /// </summary>
    public string DisplayName => Name.Contains('.') ? Name.Split('.').Last() : Name;

    /// <summary>
    /// Gets a formatted duration string.
    /// </summary>
    public string DurationDisplay
    {
        get
        {
            if (Duration == null) return "";
            if (Duration.Value.TotalSeconds < 1)
                return $"{Duration.Value.TotalMilliseconds:F0}ms";
            return $"{Duration.Value.TotalSeconds:F1}s";
        }
    }

    /// <summary>
    /// Gets an icon for the test status.
    /// </summary>
    public string StatusIcon => Status switch
    {
        TestStatus.Passed => "P",
        TestStatus.Failed => "X",
        TestStatus.Skipped => "O",
        TestStatus.Running => ">",
        _ => "?"
    };

    /// <summary>
    /// Gets whether this is a container (suite/class) with children.
    /// </summary>
    public bool IsContainer => Children.Count > 0;
}

/// <summary>
/// Summary of a test run.
/// </summary>
public class TestRunSummary
{
    /// <summary>
    /// Total number of tests.
    /// </summary>
    public int Total { get; set; }

    /// <summary>
    /// Number of passed tests.
    /// </summary>
    public int Passed { get; set; }

    /// <summary>
    /// Number of failed tests.
    /// </summary>
    public int Failed { get; set; }

    /// <summary>
    /// Number of skipped tests.
    /// </summary>
    public int Skipped { get; set; }

    /// <summary>
    /// Total duration of the test run.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Whether all tests passed.
    /// </summary>
    public bool AllPassed => Failed == 0 && Total > 0;

    /// <summary>
    /// Gets a summary string.
    /// </summary>
    public string SummaryText => $"{Passed} passed, {Failed} failed, {Skipped} skipped";
}
