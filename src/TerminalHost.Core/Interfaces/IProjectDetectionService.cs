using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

public interface IProjectDetectionService
{
    ProjectType? DetectProjectType(string workingDirectory);
    string? FindDotNetProject(string workingDirectory);
    RunConfiguration CreateDefaultConfiguration(ProjectType projectType, bool useWatchCommand = true);
    List<RunConfiguration> SuggestConfigurations(string workingDirectory, ProjectType projectType);
    List<RunConfiguration> GetOrCreateConfigurations(string workingDirectory, DirectorySettings directorySettings);
}
