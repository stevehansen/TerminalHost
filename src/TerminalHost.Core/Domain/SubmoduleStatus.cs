namespace TerminalHost.Core.Domain;

/// <summary>
/// Status of a git submodule.
/// </summary>
public enum SubmoduleStatus
{
    /// <summary>Not a submodule.</summary>
    None,

    /// <summary>Submodule is initialized and clean (matches tracked commit).</summary>
    Clean,

    /// <summary>Submodule has local modifications or is at a different commit.</summary>
    Modified,

    /// <summary>Submodule is not initialized (needs git submodule init).</summary>
    Uninitialized
}
