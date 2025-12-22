using TerminalHost.Domain;

namespace TerminalHost.Services;

/// <summary>
/// Factory for creating terminal controls.
/// </summary>
public interface ITerminalControlFactory
{
    /// <summary>
    /// Creates a terminal control for the given session.
    /// </summary>
    Task<ITerminalControl> CreateTerminalControlAsync(TerminalSession session);
}
