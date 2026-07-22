namespace TerminalHost.Core.Domain;

/// <summary>
/// Optional parameters that customise a <c>Show</c> call on the panel router.
/// </summary>
/// <param name="Zone">Explicit zone override. When null, the router falls back to <c>IPanelPlacement.PreferredZone</c>, then <see cref="PanelZone.Popup"/>.</param>
/// <param name="Scope">Explicit scope override. When null, the router falls back to <c>IPanelPlacement.PreferredScope</c>, then <see cref="PanelScope.AppShell"/>.</param>
/// <param name="ForceShow">When true, disables toggle-to-close semantics for already-open panels (focus instead).</param>
/// <param name="AllowMultiInstance">When true, bypasses single-instance dedupe — multiple registrations of the same panel id may coexist.</param>
/// <param name="AlwaysOnTop">Capability flag forwarded to window-zone surfaces.</param>
/// <param name="Anchor">Optional anchor element for popup-zone surfaces (interpreted by the surface adapter).</param>
/// <param name="Context">Optional opaque payload for parameterised panels (e.g. file path for a file viewer).</param>
public sealed record PanelShowOptions(
    PanelZone? Zone = null,
    PanelScope? Scope = null,
    bool ForceShow = false,
    bool AllowMultiInstance = false,
    bool AlwaysOnTop = false,
    object? Anchor = null,
    object? Context = null);
