using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Domain;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

/// <summary>
/// ViewModel for the Test Results popup (F6).
/// </summary>
public partial class TestResultsViewModel : ObservableObject
{
    private readonly ITestRunnerService _testRunnerService;
    private readonly IProjectDetectionService _projectDetectionService;
    private readonly MainViewModel _mainViewModel;
    private readonly IDialogService _dialogService;
    private readonly IDispatcherService _dispatcherService;
    private readonly IConfigurationService _configurationService;
    private readonly IProcessService _processService;
    private readonly IFileSystem _fileSystem;
    private readonly IToastService _toastService;
    private readonly IAiExecutionService _aiExecutionService;
    private readonly IClipboardService _clipboardService;

    private string _currentWorkingDirectory = string.Empty;
    private ProjectType? _currentProjectType;
    private readonly Dictionary<string, string> _aiDiagnoses = new();

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeFailuresCommand))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeFailuresCommand))]
    private bool _isAnalyzingFailures;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedDiagnosis))]
    private string _selectedAiDiagnosis = "";

    public bool HasSelectedDiagnosis => !string.IsNullOrEmpty(SelectedAiDiagnosis);
    public bool CanAnalyzeFailures => FailedCount > 0 && !IsRunning && !IsAnalyzingFailures;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private ObservableCollection<TestResult> _results = [];

    [ObservableProperty]
    private TestResult? _selectedResult;

    [ObservableProperty]
    private int _passedCount;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalyzeFailuresCommand))]
    private int _failedCount;

    [ObservableProperty]
    private int _skippedCount;

    [ObservableProperty]
    private string _totalDuration = "";

    [ObservableProperty]
    private string _title = "Test Results";

    [ObservableProperty]
    private string _output = "";

    // View properties for positioning/sizing the popup
    [ObservableProperty]
    private double _width = 700;

    [ObservableProperty]
    private double _height = 500;

    [ObservableProperty]
    private double _horizontalOffset;

    [ObservableProperty]
    private double _verticalOffset;

    public TestResultsViewModel(
        ITestRunnerService testRunnerService,
        IProjectDetectionService projectDetectionService,
        MainViewModel mainViewModel,
        IDialogService dialogService,
        IDispatcherService dispatcherService,
        IConfigurationService configurationService,
        IProcessService processService,
        IFileSystem fileSystem,
        IToastService toastService,
        IAiExecutionService aiExecutionService,
        IClipboardService clipboardService)
    {
        _testRunnerService = testRunnerService;
        _projectDetectionService = projectDetectionService;
        _mainViewModel = mainViewModel;
        _dialogService = dialogService;
        _dispatcherService = dispatcherService;
        _configurationService = configurationService;
        _processService = processService;
        _fileSystem = fileSystem;
        _toastService = toastService;
        _aiExecutionService = aiExecutionService;
        _clipboardService = clipboardService;

        _testRunnerService.OutputReceived += OnOutputReceived;
        _testRunnerService.TestRunCompleted += OnTestRunCompleted;
    }

    [RelayCommand]
    public async Task RunAllTestsAsync()
    {
        if (_mainViewModel.SelectedTab is not TerminalPairTabViewModel terminalTab)
        {
            StatusMessage = "Please select a project tab first.";
            IsOpen = true;
            return;
        }

        _currentWorkingDirectory = terminalTab.Pair.WorkingDirectory;
        _currentProjectType = _projectDetectionService.DetectProjectType(_currentWorkingDirectory);

        Title = $"Test Results - {terminalTab.Title}";
        StatusMessage = "Running tests...";
        Output = "";
        Results.Clear();
        ResetCounts();
        _aiDiagnoses.Clear();
        SelectedAiDiagnosis = "";

        IsRunning = true;
        IsOpen = true;

        try
        {
            var results = await _testRunnerService.RunAllTestsAsync(_currentWorkingDirectory, _currentProjectType);
            UpdateResults(results);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    public async Task RerunTestsAsync()
    {
        if (string.IsNullOrEmpty(_currentWorkingDirectory))
        {
            await RunAllTestsAsync();
            return;
        }

        StatusMessage = "Re-running tests...";
        Output = "";
        Results.Clear();
        ResetCounts();
        _aiDiagnoses.Clear();
        SelectedAiDiagnosis = "";

        IsRunning = true;

        try
        {
            var results = await _testRunnerService.RerunLastTestsAsync(_currentWorkingDirectory);
            UpdateResults(results);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    public async Task RunFailedTestsAsync()
    {
        if (string.IsNullOrEmpty(_currentWorkingDirectory))
        {
            StatusMessage = "No previous test run.";
            return;
        }

        var failedTests = Results.Where(r => r.Status == TestStatus.Failed).ToList();
        if (failedTests.Count == 0)
        {
            StatusMessage = "No failed tests to re-run.";
            return;
        }

        StatusMessage = $"Re-running {failedTests.Count} failed tests...";
        Output = "";
        Results.Clear();
        ResetCounts();
        _aiDiagnoses.Clear();
        SelectedAiDiagnosis = "";

        IsRunning = true;

        try
        {
            var results = await _testRunnerService.RunFailedTestsAsync(_currentWorkingDirectory, failedTests, _currentProjectType);
            UpdateResults(results);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private void CancelTests()
    {
        if (IsRunning)
        {
            _testRunnerService.CancelCurrentRun();
            StatusMessage = "Test run cancelled.";
            IsRunning = false;
        }
    }

    [RelayCommand]
    private async Task CopySelectedAiDiagnosisAsync()
    {
        if (!string.IsNullOrEmpty(SelectedAiDiagnosis))
        {
            await _clipboardService.SetTextAsync(SelectedAiDiagnosis);
            _toastService.Show("Copied to clipboard", ToastType.Success);
        }
    }

    [RelayCommand]
    private void Close()
    {
        if (IsRunning)
        {
            _testRunnerService.CancelCurrentRun();
        }
        IsOpen = false;
    }

    partial void OnSelectedResultChanged(TestResult? value)
    {
        if (value != null && _aiDiagnoses.TryGetValue(value.FullName, out var diag))
            SelectedAiDiagnosis = diag;
        else
            SelectedAiDiagnosis = "";
    }

    private void OnOutputReceived(object? sender, string output)
    {
        _dispatcherService.BeginInvoke(() =>
        {
            Output += output + Environment.NewLine;
        });
    }

    private void OnTestRunCompleted(object? sender, TestRunSummary summary)
    {
        _dispatcherService.BeginInvoke(() =>
        {
            PassedCount = summary.Passed;
            FailedCount = summary.Failed;
            SkippedCount = summary.Skipped;
            TotalDuration = summary.Duration.TotalSeconds < 1
                ? $"{summary.Duration.TotalMilliseconds:F0}ms"
                : $"{summary.Duration.TotalSeconds:F1}s";

            StatusMessage = summary.AllPassed
                ? $"All {summary.Total} tests passed"
                : $"{summary.Failed} of {summary.Total} tests failed";
        });
    }

    private void UpdateResults(List<TestResult> results)
    {
        Results = new ObservableCollection<TestResult>(results);

        // Update counts
        PassedCount = CountByStatus(results, TestStatus.Passed);
        FailedCount = CountByStatus(results, TestStatus.Failed);
        SkippedCount = CountByStatus(results, TestStatus.Skipped);

        if (Results.Count == 0)
        {
            StatusMessage = "No tests found.";
        }
        else if (FailedCount == 0)
        {
            StatusMessage = $"All {PassedCount} tests passed";
        }
        else
        {
            StatusMessage = $"{FailedCount} of {PassedCount + FailedCount} tests failed";
        }

        // Select first failed test, or first test
        SelectedResult = Results.FirstOrDefault(r => r.Status == TestStatus.Failed) ?? Results.FirstOrDefault();

        RunFailedTestsCommand.NotifyCanExecuteChanged();
        AnalyzeFailuresCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanAnalyzeFailures))]
    private async Task AnalyzeFailuresAsync()
    {
        var config = _configurationService.Load();
        var claudePath = Environment.ExpandEnvironmentVariables(config.Settings.CustomCommand);
        var claudeFileName = Path.GetFileNameWithoutExtension(claudePath).ToLowerInvariant();
        if (!claudeFileName.Contains("claude") && !claudeFileName.Contains("gemini"))
        {
            _toastService.Show("AI assistant not configured \u2014 check Settings \u2192 General", ToastType.Warning);
            return;
        }

        var failedTests = FlattenLeafTests(Results)
            .Where(r => r.Status == TestStatus.Failed)
            .Take(10)
            .ToList();
        if (failedTests.Count == 0)
        {
            _toastService.Show("No failures to analyze", ToastType.Info);
            return;
        }

        IsAnalyzingFailures = true;
        _aiDiagnoses.Clear();
        StatusMessage = $"Analyzing {failedTests.Count} failure{(failedTests.Count == 1 ? "" : "s")}...";
        try
        {
            // Build per-test failure descriptions
            var failureBlocks = new List<string>();
            for (int i = 0; i < failedTests.Count; i++)
            {
                var test = failedTests[i];
                var block = new List<string> { $"Test {i + 1}: {test.FullName}" };
                if (!string.IsNullOrWhiteSpace(test.ErrorMessage))
                    block.Add($"Error: {test.ErrorMessage}");
                if (!string.IsNullOrWhiteSpace(test.StackTrace))
                {
                    var stackLines = test.StackTrace.Split('\n').Take(20);
                    block.Add("Stack:\n" + string.Join('\n', stackLines));
                }
                failureBlocks.Add(string.Join('\n', block));
            }

            // Collect source files (total 300 lines)
            var sourceSection = new List<string>();
            int remainingSourceLines = 300;
            foreach (var test in failedTests)
            {
                if (string.IsNullOrEmpty(test.FilePath) || remainingSourceLines <= 0) break;
                var fullPath = Path.IsPathRooted(test.FilePath)
                    ? test.FilePath
                    : Path.Combine(_currentWorkingDirectory, test.FilePath);
                if (!_fileSystem.FileExists(fullPath)) continue;
                try
                {
                    var content = _fileSystem.ReadAllText(fullPath);
                    var lines = content.Split('\n').Take(remainingSourceLines).ToArray();
                    sourceSection.Add($"// {test.FilePath}");
                    sourceSection.AddRange(lines);
                    remainingSourceLines -= lines.Length;
                }
                catch { /* ignore unreadable files */ }
            }

            var projectName = Title.Replace("Test Results - ", "");
            var separatorInstruction = failedTests.Count > 1
                ? "\nSeparate each diagnosis with a line containing only \"---\". Maintain the same order as the tests listed."
                : "";

            var prompt = $"""
                You are diagnosing test failures. Be concise \u2014 2-4 sentences max per failure.
                Format: plain text, no markdown, no code fences.{separatorInstruction}

                Project: {projectName}

                FAILING TESTS:
                {string.Join("\n\n", failureBlocks)}
                """;

            if (sourceSection.Count > 0)
                prompt += $"\n\nTEST SOURCE:\n{string.Join('\n', sourceSection)}";

            var (exitCode, output, error) = await _processService.RunAsync(
                claudePath, "-p --no-session-persistence", _currentWorkingDirectory,
                stdin: prompt, timeout: TimeSpan.FromSeconds(60));

            if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var diagnoses = SplitDiagnoses(output.Trim(), failedTests.Count);
                for (int i = 0; i < failedTests.Count && i < diagnoses.Length; i++)
                    _aiDiagnoses[failedTests[i].FullName] = diagnoses[i];

                // Refresh display for currently selected test
                if (SelectedResult != null && _aiDiagnoses.TryGetValue(SelectedResult.FullName, out var diag))
                    SelectedAiDiagnosis = diag;

                var diagnosed = Math.Min(failedTests.Count, diagnoses.Length);
                StatusMessage = $"AI diagnosis complete \u2014 {diagnosed} of {failedTests.Count} failures analysed";
                _toastService.Show($"Analyzed {diagnosed} failures", ToastType.Success);
            }
            else if (exitCode == -1)
            {
                StatusMessage = "AI analysis timed out \u2014 check AI assistant or try again";
                _toastService.Show("AI timed out \u2014 try again", ToastType.Warning);
            }
            else
            {
                var detail = !string.IsNullOrWhiteSpace(error)
                    ? error.Split('\n')[0].Trim()
                    : $"exit {exitCode}";
                StatusMessage = $"AI analysis failed: {detail}";
                _toastService.Show($"AI failed: {detail}", ToastType.Error);
            }
        }
        finally
        {
            IsAnalyzingFailures = false;
        }
    }

    private static string[] SplitDiagnoses(string output, int expectedCount)
    {
        if (expectedCount == 1)
            return [output];

        // Split on lines that are exactly "---" (AI separator)
        var lines = output.Split('\n');
        var parts = new List<List<string>>();
        var current = new List<string>();
        foreach (var line in lines)
        {
            if (line.Trim() == "---")
            {
                if (current.Count > 0) { parts.Add(current); current = []; }
            }
            else
            {
                current.Add(line);
            }
        }
        if (current.Count > 0) parts.Add(current);
        return parts.Select(p => string.Join('\n', p).Trim()).Where(s => s.Length > 0).ToArray();
    }

    private static List<TestResult> FlattenLeafTests(IEnumerable<TestResult> results)
    {
        var leaves = new List<TestResult>();
        foreach (var r in results)
        {
            if (r.Children.Count > 0)
                leaves.AddRange(FlattenLeafTests(r.Children));
            else
                leaves.Add(r);
        }
        return leaves;
    }

    private void ResetCounts()
    {
        PassedCount = 0;
        FailedCount = 0;
        SkippedCount = 0;
        TotalDuration = "";
    }

    private static int CountByStatus(List<TestResult> results, TestStatus status)
    {
        return results.Sum(r => CountByStatusRecursive(r, status));
    }

    private static int CountByStatusRecursive(TestResult result, TestStatus status)
    {
        if (result.Children.Count == 0)
        {
            return result.Status == status ? 1 : 0;
        }
        return result.Children.Sum(c => CountByStatusRecursive(c, status));
    }
}
