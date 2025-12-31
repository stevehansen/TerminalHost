using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using TerminalHost.Core.Interfaces;
using TerminalHost.Domain;
using TerminalHost.Core.Interfaces;
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

    private string _currentWorkingDirectory = string.Empty;
    private ProjectType? _currentProjectType;

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private ObservableCollection<TestResult> _results = [];

    [ObservableProperty]
    private TestResult? _selectedResult;

    [ObservableProperty]
    private int _passedCount;

    [ObservableProperty]
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
        IDispatcherService dispatcherService)
    {
        _testRunnerService = testRunnerService;
        _projectDetectionService = projectDetectionService;
        _mainViewModel = mainViewModel;
        _dialogService = dialogService;
        _dispatcherService = dispatcherService;

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
        // Could show more details about selected test
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
