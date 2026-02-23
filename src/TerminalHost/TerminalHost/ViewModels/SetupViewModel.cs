using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using TerminalHost.Core.Domain;

namespace TerminalHost.ViewModels;

public partial class SetupViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<Dependency> _dependencies = [];

    public SetupViewModel()
    {
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
            Name = "Nerd Font",
            Description = "A font with developer-focused glyphs (e.g., Cascadia Code NF).",
            DetectionCommand = "", // Special case, requires different detection logic
            HomepageUrl = "https://github.com/microsoft/cascadia-code",
            InstallUrl = "https://github.com/microsoft/cascadia-code/releases"
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
            Name = "MCP Collab Server",
            Description = "Registers TerminalHost collaboration tools in Claude Code (messaging, file claims, shared memory).",
            DetectionCommand = "claude mcp list",
            DetectionOutputContains = "terminalhost-collab",
            InstallCommand = "claude mcp add --transport http terminalhost-collab http://localhost:19280/api/mcp -s user",
            HomepageUrl = "https://github.com/anthropics/claude-code"
        });

        Dependencies.Add(new Dependency
        {
            Name = "GitHub CLI",
            Description = "CLI for GitHub. Used for PR/issue integration in Tasks.",
            DetectionCommand = "gh --version",
            InstallCommand = "winget install --id GitHub.cli",
            HomepageUrl = "https://cli.github.com/",
            InstallUrl = "https://cli.github.com/"
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
    private static void OpenInstallLink(Dependency? dependency)
    {
        if (dependency != null && !string.IsNullOrEmpty(dependency.InstallUrl))
        {
            Process.Start(new ProcessStartInfo(dependency.InstallUrl) { UseShellExecute = true });
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
        
        if (dependency.Name == "Nerd Font")
        {
            var nerdFontNames = AppConstants.NerdFontNames;
            string? foundFontName = null;

            dependency.IsInstalled = System.Windows.Media.Fonts.SystemFontFamilies.Any(ff =>
            {
                // Check against the known list of full font names
                if (nerdFontNames.Any(name => ff.Source.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    foundFontName = ff.Source;
                    return true;
                }

                // Also check the language-specific family names
                foreach (var name in ff.FamilyNames.Values)
                {
                    if (nerdFontNames.Any(nfName => nfName.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        foundFontName = name;
                        return true;
                    }
                }

                return false;
            });

            dependency.DetectedVersion = dependency.IsInstalled ? $"Installed ({foundFontName})" : "Not Found";
            dependency.FullOutput = dependency.IsInstalled 
                ? $"An installed Nerd Font was found: {foundFontName}" 
                : $"None of the recommended Nerd Fonts were found: {string.Join(", ", nerdFontNames)}";
            dependency.ExitCode = dependency.IsInstalled ? 0 : 1;
        }
        else
        {
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
        }

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
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"{command}\"",
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

            if (output.Contains("is not recognized as the name of a cmdlet", StringComparison.OrdinalIgnoreCase))
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
