using System.IO;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Centralized AI execution with streaming progress toast and error dialog on failure.
/// </summary>
public class AiExecutionService : IAiExecutionService
{
    private readonly IProcessService _processService;
    private readonly IConfigurationService _configurationService;
    private readonly IToastService _toastService;
    private readonly IDialogService _dialogService;

    public AiExecutionService(
        IProcessService processService,
        IConfigurationService configurationService,
        IToastService toastService,
        IDialogService dialogService)
    {
        _processService = processService;
        _configurationService = configurationService;
        _toastService = toastService;
        _dialogService = dialogService;
    }

    public bool IsAiAvailable()
    {
        var config = _configurationService.Load();
        var claudePath = Environment.ExpandEnvironmentVariables(config.Settings.CustomCommand);
        var fileName = Path.GetFileNameWithoutExtension(claudePath).ToLowerInvariant();

        if (!fileName.Contains("claude") && !fileName.Contains("gemini"))
        {
            _toastService.Show("AI assistant not configured \u2014 check Settings \u2192 General", ToastType.Warning);
            return false;
        }

        return true;
    }

    public async Task<AiExecutionResult> ExecuteAsync(
        string prompt,
        string workingDirectory,
        string operationLabel,
        TimeSpan? timeout = null,
        bool showErrorOnFailure = true)
    {
        var config = _configurationService.Load();
        var claudePath = Environment.ExpandEnvironmentVariables(config.Settings.CustomCommand);
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);

        var charCount = 0;
        using var toast = _toastService.ShowProgress($"{operationLabel}...");

        try
        {
            var (exitCode, output, error) = await _processService.RunStreamingAsync(
                claudePath,
                "-p --no-session-persistence",
                workingDirectory,
                stdin: prompt,
                timeout: effectiveTimeout,
                onOutput: line =>
                {
                    charCount += line.Length + 1; // +1 for newline
                    toast.Update($"{operationLabel}... ({charCount:N0} chars)");
                });

            var timedOut = exitCode == -1 && error == "Process timed out";
            var success = exitCode == 0 && !string.IsNullOrWhiteSpace(output);

            if (success)
            {
                toast.Complete($"{operationLabel} complete");
                return new AiExecutionResult(true, output.Trim(), null, output, error, exitCode, false);
            }

            // Failure path
            if (timedOut)
            {
                toast.Fail($"{operationLabel} timed out");
                if (showErrorOnFailure)
                    ShowErrorDialog(operationLabel, output, error, timedOut: true);
            }
            else
            {
                var firstError = error.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? "Unknown error";
                toast.Fail($"{operationLabel} failed: {firstError}");
                if (showErrorOnFailure)
                    ShowErrorDialog(operationLabel, output, error, timedOut: false);
            }

            return new AiExecutionResult(false, null, error, output, error, exitCode, timedOut);
        }
        catch (Exception ex)
        {
            toast.Fail($"{operationLabel} failed");
            if (showErrorOnFailure)
                ShowErrorDialog(operationLabel, "", ex.Message, timedOut: false);
            return new AiExecutionResult(false, null, ex.Message, "", ex.Message, -1, false);
        }
    }

    private void ShowErrorDialog(string operationLabel, string stdout, string stderr, bool timedOut)
    {
        var parts = new List<string>();

        if (timedOut)
            parts.Add("The operation timed out.");

        if (!string.IsNullOrWhiteSpace(stdout))
            parts.Add($"--- STDOUT ---\n{stdout.Trim()}");

        if (!string.IsNullOrWhiteSpace(stderr))
            parts.Add($"--- STDERR ---\n{stderr.Trim()}");

        if (parts.Count == 0)
            parts.Add("No output was captured.");

        var message = string.Join("\n\n", parts);
        _dialogService.ShowError(message, $"{operationLabel} Failed");
    }
}
