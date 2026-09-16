namespace TerminalHost.Core.Domain;

/// <summary>
/// A point-in-time projection of the open panels in a given scope, suitable for persistence.
/// </summary>
/// <param name="Entries">The set of panel layout entries known to the scope.</param>
public sealed record PanelLayoutSnapshot(IReadOnlyList<PanelLayoutEntry> Entries);
