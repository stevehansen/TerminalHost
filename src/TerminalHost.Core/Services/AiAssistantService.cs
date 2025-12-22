using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Manages AI assistant configurations with persistence.
/// </summary>
public sealed class AiAssistantService : IAiAssistantService
{
    private readonly IConfigurationService _configurationService;
    private readonly IProfileRegistry _profileRegistry;
    private AppConfiguration _configuration;

    public event EventHandler? AssistantsChanged;

    public AiAssistantService(IConfigurationService configurationService, IProfileRegistry profileRegistry)
    {
        _configurationService = configurationService;
        _profileRegistry = profileRegistry;
        _configuration = configurationService.Load();

        // Initialize with defaults if empty
        EnsureAiAssistantsInitialized();
    }

    /// <summary>
    /// Ensures AI assistants are initialized, performing migration from legacy settings if needed.
    /// </summary>
    private void EnsureAiAssistantsInitialized()
    {
        if (_configuration.AiAssistants.Count == 0)
        {
            // Initialize with defaults
            _configuration.AiAssistants = AiAssistant.GetDefaults();

            // Migrate legacy CustomCommand settings if they differ from defaults
            MigrateLegacySettings();

            Save();
        }
    }

    /// <summary>
    /// Migrates legacy CustomCommand/Name/Icon settings to the Claude assistant.
    /// </summary>
    private void MigrateLegacySettings()
    {
        var settings = _configuration.Settings;
        var claudeAssistant = _configuration.AiAssistants.FirstOrDefault(a => a.Id == "claude");

        if (claudeAssistant == null)
            return;

        var defaultCommand = @"%USERPROFILE%\.local\bin\claude.exe";
        var defaultName = "Claude Code";
        var defaultIcon = "Claude";

        // Only migrate if the user has customized settings
        bool hasCustomSettings =
            !string.Equals(settings.CustomCommand, defaultCommand, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(settings.CustomCommandName, defaultName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(settings.CustomCommandIcon, defaultIcon, StringComparison.OrdinalIgnoreCase);

        if (hasCustomSettings)
        {
            claudeAssistant.Command = settings.CustomCommand;
            claudeAssistant.Name = settings.CustomCommandName;
            claudeAssistant.Icon = settings.CustomCommandIcon;
        }
    }

    public IReadOnlyList<AiAssistant> GetAllAssistants()
    {
        return _configuration.AiAssistants.AsReadOnly();
    }

    public IReadOnlyList<AiAssistant> GetEnabledAssistants()
    {
        return _configuration.AiAssistants
            .Where(a => a.Enabled)
            .ToList()
            .AsReadOnly();
    }

    public AiAssistant GetDefaultAssistant()
    {
        // First try to find the one marked as default
        var defaultAssistant = _configuration.AiAssistants.FirstOrDefault(a => a.IsDefault && a.Enabled);

        // Fall back to first enabled
        defaultAssistant ??= _configuration.AiAssistants.FirstOrDefault(a => a.Enabled);

        // Fall back to Claude (always exists)
        defaultAssistant ??= _configuration.AiAssistants.FirstOrDefault(a => a.Id == "claude");

        // Ultimate fallback - create Claude if somehow missing
        if (defaultAssistant == null)
        {
            defaultAssistant = AiAssistant.GetDefaults().First();
            _configuration.AiAssistants.Add(defaultAssistant);
            Save();
        }

        return defaultAssistant;
    }

    public AiAssistant? GetAssistant(string id)
    {
        return _configuration.AiAssistants.FirstOrDefault(a =>
            a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    public AiAssistant GetAssistantForDirectory(string workingDirectory)
    {
        var dirSettings = _profileRegistry.GetDirectorySettings(workingDirectory);

        if (!string.IsNullOrEmpty(dirSettings.ActiveAiAssistantId))
        {
            var assistant = GetAssistant(dirSettings.ActiveAiAssistantId);
            if (assistant != null && assistant.Enabled)
            {
                return assistant;
            }
        }

        return GetDefaultAssistant();
    }

    public void SetAssistantForDirectory(string workingDirectory, string assistantId)
    {
        var dirSettings = _profileRegistry.GetDirectorySettings(workingDirectory);
        dirSettings.ActiveAiAssistantId = assistantId;
        _profileRegistry.SaveConfiguration();
        AssistantsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SaveAssistant(AiAssistant assistant)
    {
        var existing = GetAssistant(assistant.Id);
        if (existing != null)
        {
            // Update existing
            var index = _configuration.AiAssistants.IndexOf(existing);
            _configuration.AiAssistants[index] = assistant;
        }
        else
        {
            // Add new
            _configuration.AiAssistants.Add(assistant);
        }

        Save();
        AssistantsChanged?.Invoke(this, EventArgs.Empty);
    }

    public void DeleteAssistant(string id)
    {
        var assistant = GetAssistant(id);
        if (assistant != null)
        {
            // Don't allow deleting the last enabled assistant
            var enabledCount = _configuration.AiAssistants.Count(a => a.Enabled);
            if (assistant.Enabled && enabledCount <= 1)
            {
                return; // Can't delete the last enabled assistant
            }

            _configuration.AiAssistants.Remove(assistant);

            // If we deleted the default, assign a new default
            if (assistant.IsDefault)
            {
                var newDefault = _configuration.AiAssistants.FirstOrDefault(a => a.Enabled);
                if (newDefault != null)
                {
                    newDefault.IsDefault = true;
                }
            }

            Save();
            AssistantsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetDefaultAssistant(string id)
    {
        // Clear existing default
        foreach (var assistant in _configuration.AiAssistants)
        {
            assistant.IsDefault = false;
        }

        // Set new default
        var newDefault = GetAssistant(id);
        if (newDefault != null)
        {
            newDefault.IsDefault = true;
            newDefault.Enabled = true; // Default must be enabled
            Save();
            AssistantsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Reload()
    {
        _configuration = _configurationService.Load();
        EnsureAiAssistantsInitialized();
        AssistantsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Save()
    {
        _configurationService.Save(_configuration);
    }
}
