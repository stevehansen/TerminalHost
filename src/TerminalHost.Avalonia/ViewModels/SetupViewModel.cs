using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.ViewModels;

public partial class SetupViewModel : ObservableObject
{
    private readonly IProcessService? _processService;

    [ObservableProperty]
    private ObservableCollection<Dependency> _dependencies = [];

    public SetupViewModel(IProcessService? processService = null)
    {
        _processService = processService;
        LoadDependencies();
    }

    private void LoadDependencies()
    {
        Dependencies.Add(new Dependency
        {
            Name = "Git",
            Description = "The version control system.",
            DetectionCommand = "git --version",
            HomepageUrl = "https://git-scm.com/",
            InstallUrl = "https://git-scm.com/downloads"
        });

        Dependencies.Add(new Dependency
        {
            Name = "Claude Code",
            Description = "The AI code assistant CLI.",
            DetectionCommand = "claude --version",
            InstallCommand = "npm install -g @anthropic-ai/claude-code",
            HomepageUrl = "https://claude.ai/",
            InstallUrl = "https://docs.anthropic.com/en/docs/claude-code",
            IsAiAssistant = true,
            AiAssistantId = "claude"
        });

        Dependencies.Add(new Dependency
        {
            Name = "Gemini CLI",
            Description = "Google's Gemini AI assistant CLI.",
            DetectionCommand = "gemini --version",
            InstallCommand = "npm install -g @google/gemini-cli",
            HomepageUrl = "https://github.com/anthropics/anthropic-quickstarts/tree/main/gemini-cli",
            InstallUrl = "https://github.com/anthropics/anthropic-quickstarts/tree/main/gemini-cli",
            IsAiAssistant = true,
            AiAssistantId = "gemini"
        });

        Dependencies.Add(new Dependency
        {
            Name = "OpenAI Codex CLI",
            Description = "OpenAI's Codex CLI assistant.",
            DetectionCommand = "codex --version",
            InstallCommand = "npm install -g @openai/codex",
            HomepageUrl = "https://github.com/openai/codex-cli",
            InstallUrl = "https://github.com/openai/codex-cli",
            IsAiAssistant = true,
            AiAssistantId = "codex"
        });

        Dependencies.Add(new Dependency
        {
            Name = "GitHub Copilot CLI",
            Description = "GitHub Copilot in the command line (requires gh CLI).",
            DetectionCommand = "gh extension list",
            DetectionOutputContains = "copilot",
            InstallCommand = "gh extension install github/gh-copilot",
            HomepageUrl = "https://github.com/features/copilot",
            InstallUrl = "https://docs.github.com/en/copilot/using-github-copilot/using-github-copilot-in-the-command-line",
            IsAiAssistant = true,
            AiAssistantId = "copilot"
        });

        Dependencies.Add(new Dependency
        {
            Name = "GitHub CLI",
            Description = "CLI for GitHub. Used for PR/issue integration in Tasks.",
            DetectionCommand = "gh --version",
            HomepageUrl = "https://cli.github.com/",
            InstallUrl = "https://cli.github.com/"
        });

        Dependencies.Add(new Dependency
        {
            Name = "MCP Collab Server",
            Description = "Registers TerminalHost collaboration tools in Claude Code.",
            DetectionCommand = "claude mcp list",
            DetectionOutputContains = "terminalhost-collab",
            InstallCommand = "claude mcp add --transport http terminalhost-collab http://localhost:19280/api/mcp -s user",
            HomepageUrl = "https://docs.anthropic.com/en/docs/claude-code",
            InstallUrl = "https://docs.anthropic.com/en/docs/claude-code"
        });

        Dependencies.Add(new Dependency
        {
            Name = "HC.Dev Tool",
            Description = "The HC.Dev tool for .NET.",
            DetectionCommand = "dev h",
            InstallCommand = "dotnet tool install --global HC.Dev",
            HomepageUrl = "https://www.nuget.org/packages/HC.Dev/",
            InstallUrl = "https://www.nuget.org/packages/HC.Dev/"
        });
    }

    [RelayCommand]
    private void OpenInstallLink(Dependency? dependency)
    {
        if (dependency != null && !string.IsNullOrEmpty(dependency.InstallUrl))
        {
            if (_processService != null)
            {
                _processService.Start(dependency.InstallUrl);
            }
            else
            {
                Process.Start(new ProcessStartInfo(dependency.InstallUrl) { UseShellExecute = true });
            }
        }
    }

    [RelayCommand]
    private async Task CheckAllDependenciesAsync()
    {
        foreach (var dep in Dependencies)
        {
            await CheckDependencyAsync(dep);
        }
    }

    [RelayCommand]
    private async Task CheckDependencyAsync(Dependency? dependency)
    {
        if (dependency == null) return;

        dependency.IsDetecting = true;

        var (success, output, exitCode) = await RunCommandAsync(dependency.DetectionCommand);

        // For commands that need to check output content (e.g., gh extension list)
        if (!string.IsNullOrEmpty(dependency.DetectionOutputContains))
        {
            dependency.IsInstalled = success && output.Contains(dependency.DetectionOutputContains, StringComparison.OrdinalIgnoreCase);
            dependency.DetectedVersion = dependency.IsInstalled ? "Installed" : "Not found";
        }
        else
        {
            dependency.IsInstalled = success;
            dependency.DetectedVersion = success ? output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? output : "Not found";
        }

        dependency.FullOutput = output;
        dependency.ExitCode = exitCode;

        dependency.IsDetecting = false;
    }

    [RelayCommand]
    private async Task InstallDependencyAsync(Dependency? dependency)
    {
        if (dependency == null) return;
        
        if (string.IsNullOrEmpty(dependency.InstallCommand))
        {
            if (!string.IsNullOrEmpty(dependency.HomepageUrl))
            {
                Process.Start(new ProcessStartInfo(dependency.HomepageUrl) { UseShellExecute = true });
            }
            return;
        }

        dependency.IsInstalling = true;
        var (_, output, exitCode) = await RunCommandAsync(dependency.InstallCommand);
        dependency.FullOutput = output;
        dependency.ExitCode = exitCode;
        dependency.IsInstalling = false;
        
        await CheckDependencyAsync(dependency);
    }

    [RelayCommand]
    private static void ToggleDetails(Dependency? dependency)
    {
        if (dependency != null)
        {
            dependency.ShowDetails = !dependency.ShowDetails;
        }
    }
    
    private static async Task<(bool success, string output, int exitCode)> RunCommandAsync(string command)
    {
        if (string.IsNullOrEmpty(command))
        {
            return (false, "No command specified.", -1);
        }

        try
        {
            // Use login shell (-l) to pick up user's PATH (e.g. ~/.local/bin for claude)
            // GUI apps on macOS have a minimal PATH that excludes user additions
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/zsh",
                    Arguments = $"-l -c \"{command.Replace("\"", "\\\"")}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };

            var outputBuilder = new StringBuilder();
            process.OutputDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) outputBuilder.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            
            await process.WaitForExitAsync();

            var output = outputBuilder.ToString().Trim();
            
            if (process.ExitCode == 0)
            {
                return (true, output, process.ExitCode);
            }

            // Check for common "command not found" patterns
            if (output.Contains("command not found", StringComparison.OrdinalIgnoreCase) ||
                output.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                return (false, output, process.ExitCode);
            }

            return (true, output, process.ExitCode);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, -1);
        }
    }
}
