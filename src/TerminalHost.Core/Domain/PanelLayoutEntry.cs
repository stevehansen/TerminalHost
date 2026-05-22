namespace TerminalHost.Core.Domain;

/// <summary>
/// A single row in a <see cref="PanelLayoutSnapshot"/>.
/// </summary>
/// <param name="PanelId">The panel's identifier (matches <c>IPanelableViewModel.PanelId</c>).</param>
/// <param name="Zone">The zone the panel was last mounted in.</param>
/// <param name="Scope">The scope the panel was last mounted under.</param>
/// <param name="IsOpen">Whether the panel was open at the time of the snapshot.</param>
public sealed record PanelLayoutEntry(string PanelId, PanelZone Zone, PanelScope Scope, bool IsOpen);
