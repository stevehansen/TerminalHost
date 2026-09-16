using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Domain;

/// <summary>
/// Surface-facing capability flags computed by the router from the panel's VM and the caller's <see cref="PanelShowOptions"/>.
/// Surfaces translate these into platform primitives (Window flags, popup attributes, etc.).
/// </summary>
/// <param name="Size">Preferred size preset for the panel.</param>
/// <param name="DismissOnClickOutside">When true, the surface should dismiss the panel when the user clicks outside it (popup semantics).</param>
/// <param name="AlwaysOnTop">When true, the surface should keep the panel above siblings (window semantics).</param>
/// <param name="ConfirmOnClose">When true, the surface should prompt before closing.</param>
public sealed record PanelMountOptions(
    PanelSizePreset Size,
    bool DismissOnClickOutside,
    bool AlwaysOnTop,
    bool ConfirmOnClose);
