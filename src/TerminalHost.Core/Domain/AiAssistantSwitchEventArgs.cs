namespace TerminalHost.Core.Domain;

/// <summary>
/// Event args for when the user switches AI assistants.
/// </summary>
public class AiAssistantSwitchEventArgs : EventArgs
{
    /// <summary>
    /// The newly selected AI assistant.
    /// </summary>
    public required AiAssistant NewAssistant { get; init; }
}
