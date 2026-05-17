using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

public sealed class TerminalProfilesBuilder : ITerminalProfilesBuilder
{
    private readonly IContainerService? _containerService;

    public TerminalProfilesBuilder(IContainerService? containerService = null)
    {
        _containerService = containerService;
    }

    public TerminalProfilesResult Build(
        string workingDirectory,
        AiAssistant aiAssistant,
        AppSettings settings,
        bool wrapCustomInShell)
    {
        var customProfile = new Profile
        {
            Id = "custom",
            Name = aiAssistant.Name,
            Command = wrapCustomInShell ? settings.ShellCommand : aiAssistant.Command,
            StartupCommand = wrapCustomInShell ? aiAssistant.Command : null,
            WorkingDir = workingDirectory,
            Icon = aiAssistant.Icon
        };

        var shellProfile = new Profile
        {
            Id = "shell",
            Name = settings.ShellCommandName,
            Command = settings.ShellCommand,
            WorkingDir = workingDirectory,
            Icon = settings.ShellCommandIcon
        };

        string? containerName = null;
        if (_containerService != null && _containerService.IsEnabledForDirectory(workingDirectory))
        {
            containerName = _containerService.GetContainerName(workingDirectory);
            customProfile.ContainerName = containerName;
            shellProfile.ContainerName = containerName;
        }

        return new TerminalProfilesResult(customProfile, shellProfile, containerName);
    }
}
