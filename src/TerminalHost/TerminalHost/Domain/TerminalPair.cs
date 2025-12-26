using System.IO;
using TerminalHost.Services;

namespace TerminalHost.Domain;

/// <summary>
/// Represents a paired set of terminals for a single working directory.
/// Contains a custom command terminal (e.g., Claude Code), a shell terminal, and an optional run terminal.
/// </summary>
public class TerminalPair : IDisposable
{
    public Guid Id { get; }
    public string WorkingDirectory { get; }
    public string DirectoryName => Path.GetFileName(WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                                   ?? WorkingDirectory;

    public TerminalSession CustomTerminal { get; private set; }
    public TerminalSession ShellTerminal { get; }

    /// <summary>
    /// Optional run terminal, created lazily when first needed.
    /// </summary>
    public TerminalSession? RunTerminal { get; private set; }
    private readonly IStatisticsService _statisticsService;
    private readonly IClipboardService _clipboardService;

    /// <summary>
    /// Current state of the run terminal.
    /// </summary>
    public RunState RunState { get; set; } = RunState.Stopped;

    public ActiveTerminal ActiveTerminal { get; set; } = ActiveTerminal.Custom;

    public TerminalSession CurrentTerminal => ActiveTerminal switch
    {
        ActiveTerminal.Custom => CustomTerminal,
        ActiveTerminal.Shell => ShellTerminal,
        ActiveTerminal.Run => RunTerminal ?? ShellTerminal,
        _ => CustomTerminal
    };

    public TerminalPair(
        string workingDirectory,
        Profile customProfile,
        Profile shellProfile,
        IStatisticsService statisticsService,
        IClipboardService clipboardService)
    {
        Id = Guid.NewGuid();
        WorkingDirectory = workingDirectory;
        _statisticsService = statisticsService;
        _clipboardService = clipboardService;

        // Override the working directory for both profiles
        var customWithDir = CloneProfileWithWorkingDir(customProfile, workingDirectory);
        var shellWithDir = CloneProfileWithWorkingDir(shellProfile, workingDirectory);

        CustomTerminal = new TerminalSession(customWithDir, _statisticsService, _clipboardService, "Custom");
        ShellTerminal = new TerminalSession(shellWithDir, _statisticsService, _clipboardService, "Shell");
    }

    private static Profile CloneProfileWithWorkingDir(Profile profile, string workingDir)
    {
        return new Profile
        {
            Id = profile.Id,
            Name = profile.Name,
            Command = profile.Command,
            StartupCommand = profile.StartupCommand,
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

    /// <summary>
    /// Creates the run terminal if it doesn't exist.
    /// </summary>
    /// <param name="runProfile">The profile to use for the run terminal. If WorkingDir is empty,
    /// it will be used as-is (for commands that embed the working directory).</param>
    /// <returns>The created or existing run terminal session.</returns>
    public TerminalSession CreateRunTerminal(Profile runProfile)
    {
        if (RunTerminal == null)
        {
            // If the profile has an empty WorkingDir, use it as-is (command embeds working dir)
            // Otherwise, override with this pair's working directory
            var profile = string.IsNullOrEmpty(runProfile.WorkingDir)
                ? runProfile
                : CloneProfileWithWorkingDir(runProfile, WorkingDirectory);
            RunTerminal = new TerminalSession(profile, _statisticsService, _clipboardService, "Run");
        }
        return RunTerminal;
    }

    /// <summary>
    /// Disposes the run terminal and resets state.
    /// </summary>
    public void DisposeRunTerminal()
    {
        RunTerminal?.Dispose();
        RunTerminal = null;
        RunState = RunState.Stopped;
    }

    /// <summary>
    /// Returns true if the run terminal has been created.
    /// </summary>
    public bool HasRunTerminal => RunTerminal != null;

    /// <summary>
    /// Replaces the custom terminal with a new session (e.g., when switching AI assistant).
    /// The old session is NOT disposed here - caller is responsible for cleanup.
    /// </summary>
    public void ReplaceCustomTerminal(TerminalSession newSession)
    {
        CustomTerminal = newSession;
    }

    public void Dispose()
    {
        CustomTerminal.Dispose();
        ShellTerminal.Dispose();
        RunTerminal?.Dispose();
    }
}

public enum ActiveTerminal
{
    Custom,
    Shell,
    Run
}
