namespace TerminalHost.Core.Domain;

/// <summary>
/// Reason a surface raised <c>DismissRequested</c>.
/// Informational only — the router routes every dismiss trigger through the same Close path.
/// Surfaces emit the trigger for telemetry and tests.
/// </summary>
public enum PanelDismissTrigger
{
    /// <summary>The user pressed Escape.</summary>
    Escape,

    /// <summary>The user clicked outside the panel (popup semantics).</summary>
    ClickOutside,

    /// <summary>The owning element (e.g. a parent window or tab) was closed.</summary>
    OwnerClosed,

    /// <summary>A programmatic close request originated inside the surface.</summary>
    ProgrammaticClose
}
