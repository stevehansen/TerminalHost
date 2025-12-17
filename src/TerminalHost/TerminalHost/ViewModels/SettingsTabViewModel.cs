using System.Collections.ObjectModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Domain;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

/// <summary>
/// View mode for the settings editor.
/// </summary>
public enum SettingsViewMode
{
    Rich,
    Raw
}

/// <summary>
/// Settings section for navigation.
/// </summary>
public enum SettingsSection
{
    General,
    Terminals,
    Profiles,
    QuickCommands,
    LinkPatterns,
    ProjectTypes,
    ClaudeCommands,
    DirectorySettings
}

public partial class SettingsTabViewModel : ObservableObject, ITabViewModel
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    private readonly IConfigurationService _configService;
    private readonly IDialogService _dialogService;
    private string _originalJson = "";

    [ObservableProperty]
    private string _title = "Settings";

    [ObservableProperty]
    private string _jsonText = "";

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _isDirty;

    // View mode (Rich or Raw)
    [ObservableProperty]
    private SettingsViewMode _viewMode = SettingsViewMode.Rich;

    // Selected section for sidebar navigation
    [ObservableProperty]
    private SettingsSection _selectedSection = SettingsSection.General;

    // Rich mode settings properties
    [ObservableProperty]
    private bool _confirmOnClose;

    [ObservableProperty]
    private bool _showInSystemTray;

    [ObservableProperty]
    private string _customCommand = "";

    [ObservableProperty]
    private string _customCommandName = "";

    [ObservableProperty]
    private string _customCommandIcon = "";

    [ObservableProperty]
    private string _shellCommand = "";

    [ObservableProperty]
    private string _shellCommandName = "";

    [ObservableProperty]
    private string _shellCommandIcon = "";

    // Quick commands collection
    [ObservableProperty]
    private ObservableCollection<QuickCommand> _quickCommands = [];

    [ObservableProperty]
    private QuickCommand? _selectedQuickCommand;

    // Quick command editing properties
    [ObservableProperty]
    private string _editQcLabel = "";

    [ObservableProperty]
    private string _editQcIcon = "";

    [ObservableProperty]
    private string _editQcText = "";

    [ObservableProperty]
    private QuickCommandTarget _editQcTarget = QuickCommandTarget.Custom;

    [ObservableProperty]
    private string _editQcShortcut = "";

    [ObservableProperty]
    private bool _editQcAppendNewline = true;

    [ObservableProperty]
    private bool _editQcUseUserInput = false;

    // Profiles collection
    [ObservableProperty]
    private ObservableCollection<Profile> _profiles = [];

    [ObservableProperty]
    private Profile? _selectedProfile;

    // Profile editing properties
    [ObservableProperty]
    private string _editProfileName = "";

    [ObservableProperty]
    private string _editProfileCommand = "";

    [ObservableProperty]
    private string _editProfileIcon = "";

    [ObservableProperty]
    private string _editProfileShortcut = "";

    [ObservableProperty]
    private bool _editProfileAutoStart;

    // Link patterns collection
    [ObservableProperty]
    private ObservableCollection<LinkPattern> _linkPatterns = [];

    [ObservableProperty]
    private LinkPattern? _selectedLinkPattern;

    // Project types collection
    [ObservableProperty]
    private ObservableCollection<ProjectType> _projectTypes = [];

    [ObservableProperty]
    private ProjectType? _selectedProjectType;

    // Directory settings
    [ObservableProperty]
    private ObservableCollection<string> _directories = [];

    [ObservableProperty]
    private string? _selectedDirectory;

    [ObservableProperty]
    private DirectorySettings? _currentDirectorySettings;

    public string TabIcon => "\u2699"; // Gear symbol
    public string WorkingDirectory => "Settings";
    public bool IsCloseable => true;
    public bool IsAnyTerminalActive => false;
    public string DisplayTitle => Title;

    public event EventHandler? CloseRequested;
    public event EventHandler? JsonTextReloaded;
    public event EventHandler? ConfigSaved;

    public SettingsTabViewModel(IConfigurationService configService, IDialogService dialogService) // Added IDialogService
    {
        _configService = configService;
        _dialogService = dialogService; // Initialize IDialogService
        LoadSettings();
    }

    public void LoadSettings()
    {
        _originalJson = _configService.LoadRawJson();
        JsonText = _originalJson;
        IsDirty = false;
        ErrorMessage = "";
        HasError = false;

        // Load rich mode properties from JSON
        LoadRichModeProperties();

        JsonTextReloaded?.Invoke(this, EventArgs.Empty);
    }

    private void LoadRichModeProperties()
    {
        try
        {
            var config = JsonSerializer.Deserialize<AppConfiguration>(JsonText, JsonOptions);

            if (config == null) return;

            // General settings
            ConfirmOnClose = config.Settings.ConfirmOnClose;
            ShowInSystemTray = config.Settings.ShowInSystemTray;

            // Terminal settings
            CustomCommand = config.Settings.CustomCommand;
            CustomCommandName = config.Settings.CustomCommandName;
            CustomCommandIcon = config.Settings.CustomCommandIcon;
            ShellCommand = config.Settings.ShellCommand;
            ShellCommandName = config.Settings.ShellCommandName;
            ShellCommandIcon = config.Settings.ShellCommandIcon;

            // Profiles
            Profiles = new ObservableCollection<Profile>(config.Profiles);

            // Quick commands
            QuickCommands = new ObservableCollection<QuickCommand>(config.QuickCommands);

            // Link patterns
            LinkPatterns = new ObservableCollection<LinkPattern>(config.LinkPatterns);

            // Project types
            ProjectTypes = new ObservableCollection<ProjectType>(config.ProjectTypes);

            // Directory settings
            Directories = new ObservableCollection<string>(config.DirectorySettings.Keys.OrderBy(k => k));
            if (Directories.Count > 0 && SelectedDirectory == null)
            {
                SelectedDirectory = Directories.First();
            }
            UpdateCurrentDirectorySettings();
        }
        catch (JsonException)
        {
            // If JSON is invalid, rich mode properties won't be updated
        }
    }

    private void SyncRichModeToJson()
    {
        try
        {
            var config = JsonSerializer.Deserialize<AppConfiguration>(JsonText, JsonOptions) ?? new AppConfiguration();

            // General settings
            config.Settings.ConfirmOnClose = ConfirmOnClose;
            config.Settings.ShowInSystemTray = ShowInSystemTray;

            // Terminal settings
            config.Settings.CustomCommand = CustomCommand;
            config.Settings.CustomCommandName = CustomCommandName;
            config.Settings.CustomCommandIcon = CustomCommandIcon;
            config.Settings.ShellCommand = ShellCommand;
            config.Settings.ShellCommandName = ShellCommandName;
            config.Settings.ShellCommandIcon = ShellCommandIcon;

            // Profiles
            config.Profiles = Profiles.ToList();

            // Quick commands
            config.QuickCommands = QuickCommands.ToList();

            // Link patterns
            config.LinkPatterns = LinkPatterns.ToList();

            // Project types
            config.ProjectTypes = ProjectTypes.ToList();

            // Directory settings (update current if selected)
            if (SelectedDirectory != null && CurrentDirectorySettings != null)
            {
                config.DirectorySettings[SelectedDirectory] = CurrentDirectorySettings;
            }

            // Re-serialize
            JsonText = JsonSerializer.Serialize(config, JsonOptions);
            IsDirty = JsonText != _originalJson;
        }
        catch (JsonException ex)
        {
            ErrorMessage = $"Cannot sync settings: {ex.Message}";
            HasError = true;
        }
    }

    partial void OnSelectedDirectoryChanged(string? value)
    {
        UpdateCurrentDirectorySettings();
    }

    private void UpdateCurrentDirectorySettings()
    {
        if (SelectedDirectory == null)
        {
            CurrentDirectorySettings = null;
            return;
        }

        try
        {
            var config = JsonSerializer.Deserialize<AppConfiguration>(JsonText, JsonOptions);

            if (config?.DirectorySettings.TryGetValue(SelectedDirectory, out var settings) == true)
            {
                CurrentDirectorySettings = settings;
            }
            else
            {
                CurrentDirectorySettings = new DirectorySettings();
            }
        }
        catch
        {
            CurrentDirectorySettings = new DirectorySettings();
        }
    }

    /// <summary>
    /// Mark settings as dirty when any rich mode property changes.
    /// </summary>
    private void MarkDirtyFromRichMode()
    {
        if (ViewMode == SettingsViewMode.Rich)
        {
            SyncRichModeToJson();
        }
    }

    // Property change handlers for rich mode - mark dirty
    partial void OnConfirmOnCloseChanged(bool value) => MarkDirtyFromRichMode();
    partial void OnShowInSystemTrayChanged(bool value) => MarkDirtyFromRichMode();
    partial void OnCustomCommandChanged(string value) => MarkDirtyFromRichMode();
    partial void OnCustomCommandNameChanged(string value) => MarkDirtyFromRichMode();
    partial void OnCustomCommandIconChanged(string value) => MarkDirtyFromRichMode();
    partial void OnShellCommandChanged(string value) => MarkDirtyFromRichMode();
    partial void OnShellCommandNameChanged(string value) => MarkDirtyFromRichMode();
    partial void OnShellCommandIconChanged(string value) => MarkDirtyFromRichMode();

    partial void OnViewModeChanged(SettingsViewMode value)
    {
        if (value == SettingsViewMode.Rich)
        {
            // Switching to Rich mode - reload properties from JSON
            LoadRichModeProperties();
        }
        else
        {
            // Switching to Raw mode - reload the JSON editor to show current state
            JsonTextReloaded?.Invoke(this, EventArgs.Empty);
        }
    }

    public void OnTextChanged(string currentText)
    {
        JsonText = currentText;
        IsDirty = currentText != _originalJson;

        // Clear error when user starts editing
        if (HasError)
        {
            ErrorMessage = "";
            HasError = false;
        }
    }

    [RelayCommand]
    private void Save()
    {
        var (success, error, warning) = _configService.SaveRawJson(JsonText);

        if (success)
        {
            _originalJson = JsonText;
            IsDirty = false;
            ErrorMessage = "";
            HasError = false;
            ConfigSaved?.Invoke(this, EventArgs.Empty);

            // Show warning dialog if there are warnings (save succeeded but with issues)
            if (!string.IsNullOrEmpty(warning))
            {
                _dialogService.ShowWarning($"Settings saved with warnings:\n\n{warning}", "Configuration Warning"); // Use injected IDialogService
            }
        }
        else
        {
            ErrorMessage = error ?? "Unknown error";
            HasError = true;
        }
    }

    [RelayCommand]
    private void Reload()
    {
        LoadSettings();
    }

    [RelayCommand]
    private void Format()
    {
        try
        {
            // Parse and re-serialize with indentation
            var parsed = JsonSerializer.Deserialize<JsonElement>(JsonText);
            var formatted = JsonSerializer.Serialize(parsed, JsonOptions);

            JsonText = formatted;
            IsDirty = formatted != _originalJson;
            ErrorMessage = "";
            HasError = false;
            JsonTextReloaded?.Invoke(this, EventArgs.Empty);
        }
        catch (JsonException ex)
        {
            ErrorMessage = $"Cannot format: {ex.Message}";
            HasError = true;
        }
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ResetQuickCommands()
    {
        try
        {
            // Create a fresh config to get default quick commands
            var defaultConfig = new AppConfiguration();

            if (ViewMode == SettingsViewMode.Rich)
            {
                // In rich mode, update the collection directly
                QuickCommands = new ObservableCollection<QuickCommand>(defaultConfig.QuickCommands);
                SelectedQuickCommand = null;
                SyncRichModeToJson();
            }
            else
            {
                // In raw mode, update the JSON
                var parsed = JsonSerializer.Deserialize<AppConfiguration>(JsonText, JsonOptions);

                if (parsed == null)
                {
                    ErrorMessage = "Cannot parse current configuration";
                    HasError = true;
                    return;
                }

                // Replace just the quick commands
                parsed.QuickCommands = defaultConfig.QuickCommands;

                // Re-serialize
                var updatedJson = JsonSerializer.Serialize(parsed, JsonOptions);

                JsonText = updatedJson;
                IsDirty = updatedJson != _originalJson;
                JsonTextReloaded?.Invoke(this, EventArgs.Empty);
            }

            ErrorMessage = "";
            HasError = false;
        }
        catch (JsonException ex)
        {
            ErrorMessage = $"Cannot reset quick commands: {ex.Message}";
            HasError = true;
        }
    }

    [RelayCommand]
    private void ResetProjectTypes()
    {
        try
        {
            var defaultProjectTypes = ProjectType.GetDefaults();

            if (ViewMode == SettingsViewMode.Rich)
            {
                ProjectTypes = new ObservableCollection<ProjectType>(defaultProjectTypes);
                SelectedProjectType = null;
                SyncRichModeToJson();
            }
            else
            {
                var parsed = JsonSerializer.Deserialize<AppConfiguration>(JsonText, JsonOptions);

                if (parsed == null)
                {
                    ErrorMessage = "Cannot parse current configuration";
                    HasError = true;
                    return;
                }

                parsed.ProjectTypes = defaultProjectTypes;

                var updatedJson = JsonSerializer.Serialize(parsed, JsonOptions);

                JsonText = updatedJson;
                IsDirty = updatedJson != _originalJson;
                JsonTextReloaded?.Invoke(this, EventArgs.Empty);
            }

            ErrorMessage = "";
            HasError = false;
        }
        catch (JsonException ex)
        {
            ErrorMessage = $"Cannot reset project types: {ex.Message}";
            HasError = true;
        }
    }

    // Quick command management
    partial void OnSelectedQuickCommandChanged(QuickCommand? value)
    {
        if (value != null)
        {
            EditQcLabel = value.Label;
            EditQcIcon = value.Icon;
            EditQcText = value.Text;
            EditQcTarget = value.Target;
            EditQcShortcut = value.Shortcut ?? "";
            EditQcAppendNewline = value.AppendNewline;
            EditQcUseUserInput = value.UseUserInput;
        }
        else
        {
            EditQcLabel = "";
            EditQcIcon = "";
            EditQcText = "";
            EditQcTarget = QuickCommandTarget.Custom;
            EditQcShortcut = "";
            EditQcAppendNewline = true;
            EditQcUseUserInput = false;
        }
    }

    [RelayCommand]
    private void AddQuickCommand()
    {
        var newCommand = new QuickCommand
        {
            Id = Guid.NewGuid().ToString(),
            Label = "New Command",
            Icon = "",
            Text = "",
            Target = QuickCommandTarget.Custom,
            AppendNewline = true
        };

        QuickCommands.Add(newCommand);
        SelectedQuickCommand = newCommand;
        SyncRichModeToJson();
    }

    [RelayCommand]
    private void DeleteQuickCommand()
    {
        if (SelectedQuickCommand == null) return;

        var index = QuickCommands.IndexOf(SelectedQuickCommand);
        QuickCommands.Remove(SelectedQuickCommand);

        // Select next or previous item
        if (QuickCommands.Count > 0)
        {
            SelectedQuickCommand = QuickCommands[Math.Min(index, QuickCommands.Count - 1)];
        }
        else
        {
            SelectedQuickCommand = null;
        }

        SyncRichModeToJson();
    }

    [RelayCommand]
    private void ApplyQuickCommand()
    {
        if (SelectedQuickCommand == null) return;

        SelectedQuickCommand.Label = EditQcLabel;
        SelectedQuickCommand.Icon = EditQcIcon;
        SelectedQuickCommand.Text = EditQcText;
        SelectedQuickCommand.Target = EditQcTarget;
        SelectedQuickCommand.Shortcut = string.IsNullOrWhiteSpace(EditQcShortcut) ? null : EditQcShortcut;
        SelectedQuickCommand.AppendNewline = EditQcAppendNewline;
        SelectedQuickCommand.UseUserInput = EditQcUseUserInput;

        // Force collection refresh to update the list display
        var index = QuickCommands.IndexOf(SelectedQuickCommand);
        if (index >= 0)
        {
            var cmd = SelectedQuickCommand;
            QuickCommands.RemoveAt(index);
            QuickCommands.Insert(index, cmd);
            SelectedQuickCommand = cmd;
        }

        SyncRichModeToJson();
    }

    [RelayCommand]
    private void MoveQuickCommandUp()
    {
        if (SelectedQuickCommand == null) return;

        var index = QuickCommands.IndexOf(SelectedQuickCommand);
        if (index > 0)
        {
            var cmd = SelectedQuickCommand;
            QuickCommands.RemoveAt(index);
            QuickCommands.Insert(index - 1, cmd);
            SelectedQuickCommand = cmd;
            SyncRichModeToJson();
        }
    }

    [RelayCommand]
    private void MoveQuickCommandDown()
    {
        if (SelectedQuickCommand == null) return;

        var index = QuickCommands.IndexOf(SelectedQuickCommand);
        if (index < QuickCommands.Count - 1)
        {
            var cmd = SelectedQuickCommand;
            QuickCommands.RemoveAt(index);
            QuickCommands.Insert(index + 1, cmd);
            SelectedQuickCommand = cmd;
            SyncRichModeToJson();
        }
    }

    // Profile management
    partial void OnSelectedProfileChanged(Profile? value)
    {
        if (value != null)
        {
            EditProfileName = value.Name;
            EditProfileCommand = value.Command;
            EditProfileIcon = value.Icon ?? "";
            EditProfileShortcut = value.Shortcut ?? "";
            EditProfileAutoStart = value.AutoStart;
        }
        else
        {
            EditProfileName = "";
            EditProfileCommand = "";
            EditProfileIcon = "";
            EditProfileShortcut = "";
            EditProfileAutoStart = false;
        }
    }

    [RelayCommand]
    private void AddProfile()
    {
        var newProfile = new Profile
        {
            Id = $"profile-{DateTime.Now:yyyyMMddHHmmss}",
            Name = "New Profile",
            Command = "pwsh.exe",
            WorkingDir = "%USERPROFILE%",
            Icon = "",
            AutoStart = false
        };

        Profiles.Add(newProfile);
        SelectedProfile = newProfile;
        SyncRichModeToJson();
    }

    [RelayCommand]
    private void DeleteProfile()
    {
        if (SelectedProfile == null) return;

        var index = Profiles.IndexOf(SelectedProfile);
        Profiles.Remove(SelectedProfile);

        if (Profiles.Count > 0)
        {
            SelectedProfile = Profiles[Math.Min(index, Profiles.Count - 1)];
        }
        else
        {
            SelectedProfile = null;
        }

        SyncRichModeToJson();
    }

    [RelayCommand]
    private void ApplyProfile()
    {
        if (SelectedProfile == null) return;

        SelectedProfile.Name = EditProfileName;
        SelectedProfile.Command = EditProfileCommand;
        SelectedProfile.Icon = string.IsNullOrWhiteSpace(EditProfileIcon) ? null : EditProfileIcon;
        SelectedProfile.Shortcut = string.IsNullOrWhiteSpace(EditProfileShortcut) ? null : EditProfileShortcut;
        SelectedProfile.AutoStart = EditProfileAutoStart;

        // Force collection refresh
        var index = Profiles.IndexOf(SelectedProfile);
        if (index >= 0)
        {
            var profile = SelectedProfile;
            Profiles.RemoveAt(index);
            Profiles.Insert(index, profile);
            SelectedProfile = profile;
        }

        SyncRichModeToJson();
    }

    [RelayCommand]
    private void MoveProfileUp()
    {
        if (SelectedProfile == null) return;

        var index = Profiles.IndexOf(SelectedProfile);
        if (index > 0)
        {
            var profile = SelectedProfile;
            Profiles.RemoveAt(index);
            Profiles.Insert(index - 1, profile);
            SelectedProfile = profile;
            SyncRichModeToJson();
        }
    }

    [RelayCommand]
    private void MoveProfileDown()
    {
        if (SelectedProfile == null) return;

        var index = Profiles.IndexOf(SelectedProfile);
        if (index < Profiles.Count - 1)
        {
            var profile = SelectedProfile;
            Profiles.RemoveAt(index);
            Profiles.Insert(index + 1, profile);
            SelectedProfile = profile;
            SyncRichModeToJson();
        }
    }
}