namespace TerminalHost.Core.Domain;

/// <summary>
/// Result of <see cref="Interfaces.ITerminalProfilesBuilder.Build"/>: the
/// (custom, shell) profile pair for a new project tab plus the container name
/// if the workspace is configured to run inside one. The container name is
/// already stamped onto both profiles — it is exposed separately so the caller
/// can decide whether to surface containerization in the UI (tab badge, etc).
/// </summary>
public sealed record TerminalProfilesResult(
    Profile CustomProfile,
    Profile ShellProfile,
    string? ContainerName);
