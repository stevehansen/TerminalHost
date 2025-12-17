using TerminalHost.Domain;

namespace TerminalHost.Services;

/// <summary>
/// Service for managing AI assistant configurations.
/// </summary>
public interface IAiAssistantService
{
    /// <summary>
    /// Gets all configured AI assistants.
    /// </summary>
    IReadOnlyList<AiAssistant> GetAllAssistants();

    /// <summary>
    /// Gets only enabled AI assistants.
    /// </summary>
    IReadOnlyList<AiAssistant> GetEnabledAssistants();

    /// <summary>
    /// Gets the default AI assistant.
    /// </summary>
    AiAssistant GetDefaultAssistant();

    /// <summary>
    /// Gets an assistant by ID.
    /// </summary>
    AiAssistant? GetAssistant(string id);

    /// <summary>
    /// Gets the active AI assistant for a specific directory.
    /// Falls back to default if none is set.
    /// </summary>
    AiAssistant GetAssistantForDirectory(string workingDirectory);

    /// <summary>
    /// Sets the active AI assistant for a specific directory.
    /// </summary>
    void SetAssistantForDirectory(string workingDirectory, string assistantId);

    /// <summary>
    /// Adds or updates an AI assistant configuration.
    /// </summary>
    void SaveAssistant(AiAssistant assistant);

    /// <summary>
    /// Removes an AI assistant by ID.
    /// </summary>
    void DeleteAssistant(string id);

    /// <summary>
    /// Sets which assistant is the default.
    /// </summary>
    void SetDefaultAssistant(string id);

    /// <summary>
    /// Reloads AI assistants from configuration.
    /// </summary>
    void Reload();

    /// <summary>
    /// Event fired when AI assistants are modified.
    /// </summary>
    event EventHandler? AssistantsChanged;
}
