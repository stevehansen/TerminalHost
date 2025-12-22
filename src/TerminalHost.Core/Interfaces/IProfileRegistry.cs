using TerminalHost.Core.Domain;

namespace TerminalHost.Core.Interfaces;

public interface IProfileRegistry
{
    IReadOnlyList<Profile> Profiles { get; }
    IReadOnlyList<LinkPattern> LinkPatterns { get; }
    IReadOnlyList<ProjectType> ProjectTypes { get; }
    AppSettings Settings { get; }

    event EventHandler? ProfilesChanged;

    Profile? GetProfile(string id);
    IEnumerable<Profile> GetAutoStartProfiles();
    void AddProfile(Profile profile);
    void UpdateProfile(Profile profile);
    void RemoveProfile(string id);
    void Reload();
    IReadOnlyList<ProjectType> GetProjectTypes();
    DirectorySettings GetDirectorySettings(string workingDirectory);
    void SaveConfiguration();
}
