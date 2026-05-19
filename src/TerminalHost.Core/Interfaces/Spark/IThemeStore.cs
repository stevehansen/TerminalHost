namespace TerminalHost.Core.Interfaces.Spark;

/// <summary>
/// Narrow slice of <see cref="IConfigurationService"/> for the Spark canvas theme.
/// The orchestrator may NOT mutate any other setting via this port.
/// </summary>
public interface IThemeStore
{
    /// <summary>Returns the saved theme, or "holographic" if none.</summary>
    string Load();

    /// <summary>Persists the theme.</summary>
    void Save(string theme);
}
