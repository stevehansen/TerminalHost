using System.IO;

namespace TerminalHost.Domain;

/// <summary>
/// Represents a paired set of terminals for a single working directory.
/// Contains both a custom command terminal (e.g., Claude Code) and a shell terminal.
/// </summary>
public class TerminalPair : IDisposable
{
    public Guid Id { get; }
    public string WorkingDirectory { get; }
    public string DirectoryName => Path.GetFileName(WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                                   ?? WorkingDirectory;

    public TerminalSession CustomTerminal { get; }
    public TerminalSession ShellTerminal { get; }

    public ActiveTerminal ActiveTerminal { get; set; } = ActiveTerminal.Custom;

    public TerminalSession CurrentTerminal => ActiveTerminal == ActiveTerminal.Custom ? CustomTerminal : ShellTerminal;

    public TerminalPair(string workingDirectory, Profile customProfile, Profile shellProfile)
    {
        Id = Guid.NewGuid();
        WorkingDirectory = workingDirectory;

        // Override the working directory for both profiles
        var customWithDir = CloneProfileWithWorkingDir(customProfile, workingDirectory);
        var shellWithDir = CloneProfileWithWorkingDir(shellProfile, workingDirectory);

        CustomTerminal = new TerminalSession(customWithDir);
        ShellTerminal = new TerminalSession(shellWithDir);
    }

    private static Profile CloneProfileWithWorkingDir(Profile profile, string workingDir)
    {
        return new Profile
        {
            Id = profile.Id,
            Name = profile.Name,
            Command = profile.Command,
            WorkingDir = workingDir,
            Icon = profile.Icon,
            Shortcut = profile.Shortcut,
            AutoStart = profile.AutoStart
        };
    }

    public void SwitchTerminal()
    {
        ActiveTerminal = ActiveTerminal == ActiveTerminal.Custom ? ActiveTerminal.Shell : ActiveTerminal.Custom;
    }

    public void Dispose()
    {
        CustomTerminal.Dispose();
        ShellTerminal.Dispose();
    }
}

public enum ActiveTerminal
{
    Custom,
    Shell
}
