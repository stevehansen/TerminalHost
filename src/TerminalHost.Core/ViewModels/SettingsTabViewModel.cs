using System.Collections.ObjectModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;
using TerminalHost.Core.Services;

namespace TerminalHost.Core.ViewModels;

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
    AiAssistants,
    Profiles,
    QuickCommands,
    LinkPatterns,
    ProjectTypes,
    ClaudeCommands,
    DirectorySettings,
    ApiWebhooks,
    StatusOverlay
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
    private readonly IToastService _toastService;
    private readonly IProcessService _processService;
    private readonly IClipboardService _clipboardService;
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

    // Flag to prevent syncing during load
    private bool _isLoading;

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
    private WorkspaceSortMode _workspaceSortMode;

    [ObservableProperty]
    private GitTrackingMode _gitTrackingMode;

    [ObservableProperty]
    private bool _touchMode;

    // Sound settings
    [ObservableProperty]
    private bool _soundsEnabled;

    [ObservableProperty]
    private bool _soundsOnlyWhenUnfocused = true;

    [ObservableProperty]
    private string _successSound = "Asterisk";

    [ObservableProperty]
    private string _errorSound = "Hand";

    [ObservableProperty]
    private string _warningSound = "Exclamation";

    [ObservableProperty]
    private string _infoSound = "";

    [ObservableProperty]
    private string _inputWaitingSound = "Exclamation";

    // Voice settings
    [ObservableProperty]
    private bool _voiceEnabled;

    [ObservableProperty]
    private float _voiceConfidenceThreshold = 0.8f;

    [ObservableProperty]
    private bool _voiceFeedbackSounds = true;

    [ObservableProperty]
    private bool _voiceShowTranscript = true;

    [ObservableProperty]
    private int _voiceSpeechEngineIndex;

    [ObservableProperty]
    private int _voiceWhisperModelSizeIndex = 2; // Small

    [ObservableProperty]
    private string _voiceWhisperLanguage = "auto";

    [ObservableProperty]
    private string _voiceWhisperModelStatus = "Not downloaded";

    /// <summary>
    /// Whether the Whisper engine is selected (for conditional XAML visibility).
    /// </summary>
    public bool IsWhisperEngine => VoiceSpeechEngineIndex == 1;

    // Status Overlay settings
    [ObservableProperty]
    private bool _overlayAutoShowOnUnfocus;

    [ObservableProperty]
    private int _overlaySizeIndex;

    [ObservableProperty]
    private double _overlayOpacity = 0.9;

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

    // Quick command shortcut conflict detection
    [ObservableProperty]
    private bool _hasQcShortcutConflict;

    [ObservableProperty]
    private string _qcShortcutConflictMessage = "";

    // AI Assistants collection
    [ObservableProperty]
    private ObservableCollection<AiAssistant> _aiAssistants = [];

    [ObservableProperty]
    private AiAssistant? _selectedAiAssistant;

    // AI Assistant editing properties
    [ObservableProperty]
    private string _editAiName = "";

    [ObservableProperty]
    private string _editAiCommand = "";

    [ObservableProperty]
    private string _editAiIcon = "";

    [ObservableProperty]
    private string _editAiDetectionCommand = "";

    [ObservableProperty]
    private bool _editAiEnabled;

    [ObservableProperty]
    private bool _editAiIsDefault;

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

    // Profile shortcut conflict detection
    [ObservableProperty]
    private bool _hasProfileShortcutConflict;

    [ObservableProperty]
    private string _profileShortcutConflictMessage = "";

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

    // Link pattern editing properties
    [ObservableProperty]
    private string _editLpName = "";

    [ObservableProperty]
    private string _editLpPattern = "";

    [ObservableProperty]
    private string _editLpUrlTemplate = "";

    [ObservableProperty]
    private bool _editLpEnabled = true;

    [ObservableProperty]
    private int _editLpPriority = 0;

    // Regex test properties
    [ObservableProperty]
    private string _regexTestInput = "";

    [ObservableProperty]
    private string _regexTestMatch = "";

    [ObservableProperty]
    private string _regexTestUrl = "";

    [ObservableProperty]
    private bool _regexTestIsValid = true;

    [ObservableProperty]
    private string _regexTestError = "";

    // Project type editing properties
    [ObservableProperty]
    private string _editPtId = "";

    [ObservableProperty]
    private string _editPtName = "";

    [ObservableProperty]
    private string _editPtDetectFiles = "";

    [ObservableProperty]
    private string _editPtDefaultCommand = "";

    [ObservableProperty]
    private string _editPtWatchCommand = "";

    [ObservableProperty]
    private string _editPtUrlPattern = "";

    [ObservableProperty]
    private int _editPtPriority = 0;

    // Directory settings
    [ObservableProperty]
    private ObservableCollection<string> _directories = [];

    [ObservableProperty]
    private string? _selectedDirectory;

    [ObservableProperty]
    private DirectorySettings? _currentDirectorySettings;

    // Directory settings editing properties
    [ObservableProperty]
    private TerminalLayoutMode _editDsLayoutMode = TerminalLayoutMode.HorizontalSplit;

    [ObservableProperty]
    private double _editDsSplitRatio = 0.6;

    [ObservableProperty]
    private bool _editDsShowRunTerminal = false;

    [ObservableProperty]
    private double _editDsRunSplitRatio = 0.3;

    [ObservableProperty]
    private bool _editDsShowExplorer = false;

    [ObservableProperty]
    private double _editDsExplorerRatio = 0.25;

    [ObservableProperty]
    private bool _editDsShowLeftPanel = false;

    [ObservableProperty]
    private double _editDsLeftPanelRatio = 0.25;

    // AI Assistant selection for the directory
    [ObservableProperty]
    private AiAssistant? _editDsAiAssistant;

    // Run configurations for the selected directory
    [ObservableProperty]
    private ObservableCollection<RunConfiguration> _runConfigurations = [];

    [ObservableProperty]
    private RunConfiguration? _selectedRunConfiguration;

    // Run configuration editing properties
    [ObservableProperty]
    private string _editRcName = "";

    [ObservableProperty]
    private string _editRcCommand = "";

    [ObservableProperty]
    private bool _editRcIsDefault = false;

    [ObservableProperty]
    private string _editRcUrlPattern = "";

    // API & Webhooks settings
    [ObservableProperty]
    private bool _apiEnabled;

    [ObservableProperty]
    private int _apiPort = 19280;

    [ObservableProperty]
    private string _apiBindAddress = "127.0.0.1";

    [ObservableProperty]
    private string _apiKey = "";

    [ObservableProperty]
    private bool _apiEnableSse = true;

    [ObservableProperty]
    private string _apiCorsOrigins = "http://localhost:*";

    [ObservableProperty]
    private bool _apiEnableWebhooks;

    [ObservableProperty]
    private ObservableCollection<WebhookEndpoint> _apiWebhooks = [];

    [ObservableProperty]
    private WebhookEndpoint? _selectedWebhook;

    // Webhook editing properties
    [ObservableProperty]
    private string _editWhName = "";

    [ObservableProperty]
    private string _editWhUrl = "";

    [ObservableProperty]
    private string _editWhSecret = "";

    [ObservableProperty]
    private string _editWhEvents = "*";

    [ObservableProperty]
    private int _editWhDebounceMs = 500;

    [ObservableProperty]
    private int _editWhBatchWindowMs = 0;

    [ObservableProperty]
    private int _editWhMaxRetries = 3;

    [ObservableProperty]
    private bool _editWhEnabled = true;

    // Channel settings
    [ObservableProperty]
    private bool _channelEnabled;

    [ObservableProperty]
    private string _channelServerPath = "";

    [ObservableProperty]
    private string _channelEventFilters = "repo.git_status_changed, repo.branch_switched, repo.commit, channel.user_message";

    [ObservableProperty]
    private bool _channelUseDevelopmentFlag = true;

    [ObservableProperty]
    private bool _channelAutoRegisterMcp = true;

    // MCP Collab integration
    [ObservableProperty]
    private bool _mcpCollabInstalled;

    [ObservableProperty]
    private bool _mcpCollabDetecting;

    [ObservableProperty]
    private string _mcpCollabStatus = "";

    public string McpCollabUrl => $"http://localhost:{ApiPort}/api/mcp";

    /// <summary>
    /// Whether an API key is required (non-loopback bind address).
    /// </summary>
    public bool IsApiKeyRequired => ApiBindAddress != "127.0.0.1" && ApiBindAddress != "localhost";

    public string TabIcon => "\u2699"; // Gear symbol
    public string WorkingDirectory => "Settings";
    public bool IsCloseable => true;
    public bool CanDuplicate => false;
    public bool IsAnyTerminalActive => false;
    public bool HasUnreadActivity => false;
    public bool IsVisibleInFocusMode => true; // Settings always visible

    [ObservableProperty]
    private bool _isSelected;
    public bool ShowActivitySpinner => false;
    public bool ShowCompletedIndicator => false;
    public bool IsWaitingForInput => false;
    public bool ShowWaitingIndicator => false;
    public bool ShowClaudeTaskIndicator => false;
    public bool IsTerminalInitialized => true;
    public Task InitializeTerminalsAsync() => Task.CompletedTask;
    public void UpdateFocusModeVisibility(bool isFocusModeEnabled, IReadOnlyList<string> currentTaskProjects) { } // No-op, always visible
    public void ClearUnreadActivity() { }
    public string DisplayTitle => Title;

    public event EventHandler? CloseRequested;
    public event EventHandler? JsonTextReloaded;
    public event EventHandler? ConfigSaved;

    public SettingsTabViewModel(IConfigurationService configService, IDialogService dialogService, IToastService toastService,
        IProcessService? processService = null, IClipboardService? clipboardService = null)
    {
        _configService = configService;
        _dialogService = dialogService;
        _toastService = toastService;
        _processService = processService!;
        _clipboardService = clipboardService!;
        LoadSettings();
    }

    public void LoadSettings()
    {
        _isLoading = true;
        try
        {
            _originalJson = _configService.LoadRawJson();
            JsonText = _originalJson;
            IsDirty = false;
            ErrorMessage = "";
            HasError = false;

            // Load rich mode properties from JSON
            LoadRichModeProperties();

            // Fire-and-forget MCP collab detection
            if (_processService != null)
            {
                _ = DetectMcpCollabAsync();
            }

            JsonTextReloaded?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void LoadRichModeProperties()
    {
        if (string.IsNullOrEmpty(JsonText)) return;

        try
        {
            var config = JsonSerializer.Deserialize<AppConfiguration>(JsonText, JsonOptions);

            if (config == null) return;

            // General settings
            ConfirmOnClose = config.Settings.ConfirmOnClose;
            ShowInSystemTray = config.Settings.ShowInSystemTray;
            WorkspaceSortMode = config.Settings.WorkspaceSortMode;
            GitTrackingMode = config.Settings.GitTrackingMode;
            TouchMode = config.Settings.TouchMode;

            // Sound settings
            SoundsEnabled = config.Settings.Sounds.Enabled;
            SoundsOnlyWhenUnfocused = config.Settings.Sounds.OnlyWhenUnfocused;
            SuccessSound = config.Settings.Sounds.SuccessSound;
            ErrorSound = config.Settings.Sounds.ErrorSound;
            WarningSound = config.Settings.Sounds.WarningSound;
            InfoSound = config.Settings.Sounds.InfoSound;
            InputWaitingSound = config.Settings.Sounds.InputWaitingSound;

            // Voice settings
            VoiceEnabled = config.Settings.Voice.Enabled;
            VoiceConfidenceThreshold = config.Settings.Voice.ConfidenceThreshold;
            VoiceFeedbackSounds = config.Settings.Voice.FeedbackSounds;
            VoiceShowTranscript = config.Settings.Voice.ShowTranscript;
            VoiceSpeechEngineIndex = (int)config.Settings.Voice.SpeechEngine;
            VoiceWhisperModelSizeIndex = (int)config.Settings.Voice.WhisperModelSize;
            VoiceWhisperLanguage = config.Settings.Voice.WhisperLanguage;

            // Status Overlay settings
            OverlayAutoShowOnUnfocus = config.Settings.StatusOverlay.AutoShowOnUnfocus;
            OverlaySizeIndex = (int)config.Settings.StatusOverlay.Size;
            OverlayOpacity = config.Settings.StatusOverlay.Opacity;

            // Terminal settings
            CustomCommand = config.Settings.CustomCommand;
            CustomCommandName = config.Settings.CustomCommandName;
            CustomCommandIcon = config.Settings.CustomCommandIcon;
            ShellCommand = config.Settings.ShellCommand;
            ShellCommandName = config.Settings.ShellCommandName;
            ShellCommandIcon = config.Settings.ShellCommandIcon;

            // Profiles
            Profiles = new ObservableCollection<Profile>(config.Profiles);

            // AI Assistants
            var aiAssistants = config.AiAssistants.Count > 0
                ? config.AiAssistants
                : AiAssistant.GetDefaults();
            AiAssistants = new ObservableCollection<AiAssistant>(aiAssistants);

            // Quick commands
            QuickCommands = new ObservableCollection<QuickCommand>(config.QuickCommands);

            // Link patterns
            LinkPatterns = new ObservableCollection<LinkPattern>(config.LinkPatterns);

            // Project types
            ProjectTypes = new ObservableCollection<ProjectType>(config.ProjectTypes);

            // API & Webhooks settings
            ApiEnabled = config.Settings.Api.Enabled;
            ApiPort = config.Settings.Api.Port;
            ApiBindAddress = config.Settings.Api.BindAddress;
            ApiKey = config.Settings.Api.ApiKey ?? "";
            ApiEnableSse = config.Settings.Api.EnableSse;
            ApiCorsOrigins = string.Join(", ", config.Settings.Api.CorsOrigins);
            ApiEnableWebhooks = config.Settings.Api.EnableWebhooks;
            ApiWebhooks = new ObservableCollection<WebhookEndpoint>(config.Settings.Api.Webhooks);

            // Channel settings
            ChannelEnabled = config.Settings.Channel.Enabled;
            ChannelServerPath = config.Settings.Channel.ChannelServerPath ?? "";
            ChannelEventFilters = string.Join(", ", config.Settings.Channel.EventFilters);
            ChannelUseDevelopmentFlag = config.Settings.Channel.UseDevelopmentFlag;
            ChannelAutoRegisterMcp = config.Settings.Channel.AutoRegisterMcp;

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
            config.Settings.WorkspaceSortMode = WorkspaceSortMode;
            config.Settings.GitTrackingMode = GitTrackingMode;
            config.Settings.TouchMode = TouchMode;

            // Sound settings
            config.Settings.Sounds.Enabled = SoundsEnabled;
            config.Settings.Sounds.OnlyWhenUnfocused = SoundsOnlyWhenUnfocused;
            config.Settings.Sounds.SuccessSound = SuccessSound;
            config.Settings.Sounds.ErrorSound = ErrorSound;
            config.Settings.Sounds.WarningSound = WarningSound;
            config.Settings.Sounds.InfoSound = InfoSound;
            config.Settings.Sounds.InputWaitingSound = InputWaitingSound;

            // Voice settings
            config.Settings.Voice.Enabled = VoiceEnabled;
            config.Settings.Voice.ConfidenceThreshold = VoiceConfidenceThreshold;
            config.Settings.Voice.FeedbackSounds = VoiceFeedbackSounds;
            config.Settings.Voice.ShowTranscript = VoiceShowTranscript;
            config.Settings.Voice.SpeechEngine = (VoiceRecognitionEngine)VoiceSpeechEngineIndex;
            config.Settings.Voice.WhisperModelSize = (WhisperModelSize)VoiceWhisperModelSizeIndex;
            config.Settings.Voice.WhisperLanguage = VoiceWhisperLanguage;

            // Status Overlay settings
            config.Settings.StatusOverlay.AutoShowOnUnfocus = OverlayAutoShowOnUnfocus;
            config.Settings.StatusOverlay.Size = (StatusOverlaySize)OverlaySizeIndex;
            config.Settings.StatusOverlay.Opacity = OverlayOpacity;

            // Terminal settings
            config.Settings.CustomCommand = CustomCommand;
            config.Settings.CustomCommandName = CustomCommandName;
            config.Settings.CustomCommandIcon = CustomCommandIcon;
            config.Settings.ShellCommand = ShellCommand;
            config.Settings.ShellCommandName = ShellCommandName;
            config.Settings.ShellCommandIcon = ShellCommandIcon;

            // Profiles
            config.Profiles = Profiles.ToList();

            // AI Assistants
            config.AiAssistants = AiAssistants.ToList();

            // Quick commands
            config.QuickCommands = QuickCommands.ToList();

            // Link patterns
            config.LinkPatterns = LinkPatterns.ToList();

            // Project types
            config.ProjectTypes = ProjectTypes.ToList();

            // API & Webhooks settings
            config.Settings.Api.Enabled = ApiEnabled;
            config.Settings.Api.Port = ApiPort;
            config.Settings.Api.BindAddress = ApiBindAddress;
            config.Settings.Api.ApiKey = string.IsNullOrEmpty(ApiKey) ? null : ApiKey;
            config.Settings.Api.EnableSse = ApiEnableSse;
            config.Settings.Api.CorsOrigins = ApiCorsOrigins
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            config.Settings.Api.EnableWebhooks = ApiEnableWebhooks;
            config.Settings.Api.Webhooks = ApiWebhooks.ToList();

            // Channel settings
            config.Settings.Channel.Enabled = ChannelEnabled;
            config.Settings.Channel.ChannelServerPath = string.IsNullOrEmpty(ChannelServerPath) ? null : ChannelServerPath;
            config.Settings.Channel.EventFilters = ChannelEventFilters
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
            config.Settings.Channel.UseDevelopmentFlag = ChannelUseDevelopmentFlag;
            config.Settings.Channel.AutoRegisterMcp = ChannelAutoRegisterMcp;

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
        if (_isLoading) return;

        if (ViewMode == SettingsViewMode.Rich)
        {
            SyncRichModeToJson();
        }
    }

    // Property change handlers for rich mode - mark dirty
    partial void OnConfirmOnCloseChanged(bool value) => MarkDirtyFromRichMode();
    partial void OnShowInSystemTrayChanged(bool value) => MarkDirtyFromRichMode();
    partial void OnWorkspaceSortModeChanged(WorkspaceSortMode value) => MarkDirtyFromRichMode();
    partial void OnGitTrackingModeChanged(GitTrackingMode value) => MarkDirtyFromRichMode();
    partial void OnTouchModeChanged(bool value) => MarkDirtyFromRichMode();
    partial void OnSoundsEnabledChanged(bool value) => MarkDirtyFromRichMode();
    partial void OnSoundsOnlyWhenUnfocusedChanged(bool value) => MarkDirtyFromRichMode();
    partial void OnSuccessSoundChanged(string value) => MarkDirtyFromRichMode();
    partial void OnErrorSoundChanged(string value) => MarkDirtyFromRichMode();
    partial void OnWarningSoundChanged(string value) => MarkDirtyFromRichMode();
    partial void OnInfoSoundChanged(string value) => MarkDirtyFromRichMode();
    partial void OnInputWaitingSoundChanged(string value) => MarkDirtyFromRichMode();
    partial void OnVoiceEnabledChanged(bool value) => MarkDirtyFromRichMode();
    partial void OnVoiceConfidenceThresholdChanged(float value) => MarkDirtyFromRichMode();
    partial void OnVoiceFeedbackSoundsChanged(bool value) => MarkDirtyFromRichMode();
    partial void OnVoiceShowTranscriptChanged(bool value) => MarkDirtyFromRichMode();
    partial void OnVoiceSpeechEngineIndexChanged(int value) { OnPropertyChanged(nameof(IsWhisperEngine)); MarkDirtyFromRichMode(); }
    partial void OnVoiceWhisperModelSizeIndexChanged(int value) => MarkDirtyFromRichMode();
    partial void OnVoiceWhisperLanguageChanged(string value) => MarkDirtyFromRichMode();
    partial void OnOverlayAutoShowOnUnfocusChanged(bool value) => MarkDirtyFromRichMode();
    partial void OnOverlaySizeIndexChanged(int value) => MarkDirtyFromRichMode();
    partial void OnOverlayOpacityChanged(double value) => MarkDirtyFromRichMode();
    partial void OnCustomCommandChanged(string value) => MarkDirtyFromRichMode();
    partial void OnCustomCommandNameChanged(string value) => MarkDirtyFromRichMode();
    partial void OnCustomCommandIconChanged(string value) => MarkDirtyFromRichMode();
    partial void OnShellCommandChanged(string value) => MarkDirtyFromRichMode();
    partial void OnShellCommandNameChanged(string value) => MarkDirtyFromRichMode();
    partial void OnShellCommandIconChanged(string value) => MarkDirtyFromRichMode();

    // API & Webhooks change handlers
    partial void OnApiEnabledChanged(bool value) => MarkDirtyFromRichMode();
    partial void OnApiPortChanged(int value) { OnPropertyChanged(nameof(McpCollabUrl)); MarkDirtyFromRichMode(); }
    partial void OnApiBindAddressChanged(string value) { OnPropertyChanged(nameof(IsApiKeyRequired)); MarkDirtyFromRichMode(); }
    partial void OnApiKeyChanged(string value) => MarkDirtyFromRichMode();
    partial void OnApiEnableSseChanged(bool value) => MarkDirtyFromRichMode();
    partial void OnApiCorsOriginsChanged(string value) => MarkDirtyFromRichMode();
    partial void OnApiEnableWebhooksChanged(bool value) => MarkDirtyFromRichMode();

    // Channel change handlers
    partial void OnChannelEnabledChanged(bool value) => MarkDirtyFromRichMode();
    partial void OnChannelServerPathChanged(string value) => MarkDirtyFromRichMode();
    partial void OnChannelEventFiltersChanged(string value) => MarkDirtyFromRichMode();
    partial void OnChannelUseDevelopmentFlagChanged(bool value) => MarkDirtyFromRichMode();
    partial void OnChannelAutoRegisterMcpChanged(bool value) => MarkDirtyFromRichMode();

    partial void OnSelectedWebhookChanged(WebhookEndpoint? value)
    {
        if (value != null)
        {
            EditWhName = value.Name;
            EditWhUrl = value.Url;
            EditWhSecret = value.Secret ?? "";
            EditWhEvents = string.Join(", ", value.Events);
            EditWhDebounceMs = value.DebounceMs;
            EditWhBatchWindowMs = value.BatchWindowMs;
            EditWhMaxRetries = value.MaxRetries;
            EditWhEnabled = value.Enabled;
        }
    }

    [RelayCommand]
    private void AddWebhook()
    {
        var webhook = new WebhookEndpoint { Name = "New Webhook" };
        ApiWebhooks.Add(webhook);
        SelectedWebhook = webhook;
        SyncRichModeToJson();
    }

    [RelayCommand]
    private void RemoveWebhook()
    {
        if (SelectedWebhook == null) return;
        ApiWebhooks.Remove(SelectedWebhook);
        SelectedWebhook = ApiWebhooks.FirstOrDefault();
        SyncRichModeToJson();
    }

    [RelayCommand]
    private void ApplyWebhook()
    {
        if (SelectedWebhook == null) return;
        SelectedWebhook.Name = EditWhName;
        SelectedWebhook.Url = EditWhUrl;
        SelectedWebhook.Secret = string.IsNullOrEmpty(EditWhSecret) ? null : EditWhSecret;
        SelectedWebhook.Events = EditWhEvents
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        SelectedWebhook.DebounceMs = EditWhDebounceMs;
        SelectedWebhook.BatchWindowMs = EditWhBatchWindowMs;
        SelectedWebhook.MaxRetries = EditWhMaxRetries;
        SelectedWebhook.Enabled = EditWhEnabled;
        SyncRichModeToJson();
    }

    // MCP Collab integration commands

    /// <summary>
    /// Gets the shell executable and argument format for running commands on the current platform.
    /// </summary>
    private static (string shell, string argFormat) GetShellCommand(string command)
    {
        if (OperatingSystem.IsWindows())
            return ("powershell.exe", $"-NoProfile -Command \"{command}\"");

        // macOS / Linux: use login shell to pick up user's PATH
        // GUI apps on macOS have a minimal PATH that excludes user additions (e.g. ~/.local/bin)
        return ("/bin/zsh", $"-l -c \"{command.Replace("\"", "\\\"")}\"");
    }

    [RelayCommand]
    private async Task DetectMcpCollabAsync()
    {
        if (_processService == null) return;

        McpCollabDetecting = true;
        try
        {
            var (shell, args) = GetShellCommand("claude mcp list");
            var (exitCode, output, error) = await _processService.RunAsync(shell, args);

            var combined = output + error;
            McpCollabInstalled = combined.Contains("terminalhost-collab", StringComparison.OrdinalIgnoreCase);
            McpCollabStatus = McpCollabInstalled ? "Installed" : "Not registered";
        }
        catch
        {
            McpCollabInstalled = false;
            McpCollabStatus = "Detection failed";
        }
        finally
        {
            McpCollabDetecting = false;
        }
    }

    [RelayCommand]
    private async Task InstallMcpCollabAsync()
    {
        if (_processService == null) return;

        try
        {
            var url = McpCollabUrl;
            var (shell, args) = GetShellCommand($"claude mcp add --transport http terminalhost-collab {url} -s user");
            var (exitCode, output, error) = await _processService.RunAsync(shell, args);

            if (exitCode == 0)
            {
                _toastService.Show("MCP Collab server registered in Claude Code", ToastType.Success);
            }
            else
            {
                _toastService.Show($"MCP registration failed: {error}", ToastType.Error);
            }
        }
        catch (Exception ex)
        {
            _toastService.Show($"MCP registration failed: {ex.Message}", ToastType.Error);
        }

        await DetectMcpCollabAsync();
    }

    [RelayCommand]
    private async Task UninstallMcpCollabAsync()
    {
        if (_processService == null) return;

        try
        {
            var (shell, args) = GetShellCommand("claude mcp remove terminalhost-collab -s user");
            var (exitCode, output, error) = await _processService.RunAsync(shell, args);

            if (exitCode == 0)
            {
                _toastService.Show("MCP Collab server removed from Claude Code", ToastType.Success);
            }
            else
            {
                _toastService.Show($"MCP removal failed: {error}", ToastType.Error);
            }
        }
        catch (Exception ex)
        {
            _toastService.Show($"MCP removal failed: {ex.Message}", ToastType.Error);
        }

        await DetectMcpCollabAsync();
    }

    [RelayCommand]
    private async Task CopyMcpCollabUrlAsync()
    {
        if (_clipboardService == null) return;

        await _clipboardService.SetTextAsync(McpCollabUrl);
        _toastService.Show("MCP URL copied to clipboard", ToastType.Success);
    }

    // Shortcut conflict detection
    partial void OnEditQcShortcutChanged(string value) => CheckQuickCommandShortcutConflict();
    partial void OnEditProfileShortcutChanged(string value) => CheckProfileShortcutConflict();

    private void CheckQuickCommandShortcutConflict()
    {
        var shortcut = EditQcShortcut;

        // Check format validity
        var formatError = ShortcutConflictService.ValidateShortcutFormat(shortcut);
        if (formatError != null)
        {
            HasQcShortcutConflict = true;
            QcShortcutConflictMessage = formatError;
            return;
        }

        // Check against built-in shortcuts
        var builtInConflict = ShortcutConflictService.GetBuiltInConflict(shortcut);
        if (builtInConflict != null)
        {
            HasQcShortcutConflict = true;
            QcShortcutConflictMessage = $"Conflicts with built-in: {builtInConflict}";
            return;
        }

        // Check against other quick commands
        var otherQcShortcuts = QuickCommands.Select(qc => (qc.Shortcut, qc.Label));
        var qcConflict = ShortcutConflictService.GetUserShortcutConflict(shortcut, otherQcShortcuts, SelectedQuickCommand?.Label);
        if (qcConflict != null)
        {
            HasQcShortcutConflict = true;
            QcShortcutConflictMessage = $"Conflicts with quick command: {qcConflict}";
            return;
        }

        // Check against profile shortcuts
        var profileShortcuts = Profiles.Select(p => (p.Shortcut, p.Name));
        var profileConflict = ShortcutConflictService.GetUserShortcutConflict(shortcut, profileShortcuts);
        if (profileConflict != null)
        {
            HasQcShortcutConflict = true;
            QcShortcutConflictMessage = $"Conflicts with profile: {profileConflict}";
            return;
        }

        // No conflict
        HasQcShortcutConflict = false;
        QcShortcutConflictMessage = "";
    }

    private void CheckProfileShortcutConflict()
    {
        var shortcut = EditProfileShortcut;

        // Check format validity
        var formatError = ShortcutConflictService.ValidateShortcutFormat(shortcut);
        if (formatError != null)
        {
            HasProfileShortcutConflict = true;
            ProfileShortcutConflictMessage = formatError;
            return;
        }

        // Check against built-in shortcuts
        var builtInConflict = ShortcutConflictService.GetBuiltInConflict(shortcut);
        if (builtInConflict != null)
        {
            HasProfileShortcutConflict = true;
            ProfileShortcutConflictMessage = $"Conflicts with built-in: {builtInConflict}";
            return;
        }

        // Check against other profiles
        var otherProfileShortcuts = Profiles.Select(p => (p.Shortcut, p.Name));
        var profileConflict = ShortcutConflictService.GetUserShortcutConflict(shortcut, otherProfileShortcuts, SelectedProfile?.Name);
        if (profileConflict != null)
        {
            HasProfileShortcutConflict = true;
            ProfileShortcutConflictMessage = $"Conflicts with profile: {profileConflict}";
            return;
        }

        // Check against quick command shortcuts
        var qcShortcuts = QuickCommands.Select(qc => (qc.Shortcut, qc.Label));
        var qcConflict = ShortcutConflictService.GetUserShortcutConflict(shortcut, qcShortcuts);
        if (qcConflict != null)
        {
            HasProfileShortcutConflict = true;
            ProfileShortcutConflictMessage = $"Conflicts with quick command: {qcConflict}";
            return;
        }

        // No conflict
        HasProfileShortcutConflict = false;
        ProfileShortcutConflictMessage = "";
    }

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
                _dialogService.ShowWarning($"Settings saved with warnings:\n\n{warning}", "Configuration Warning");
            }
            else
            {
                // Show success toast when saved without warnings
                _toastService.Show("Settings saved", ToastType.Success);
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

    // AI Assistant management
    partial void OnSelectedAiAssistantChanged(AiAssistant? value)
    {
        if (value != null)
        {
            EditAiName = value.Name;
            EditAiCommand = value.Command;
            EditAiIcon = value.Icon;
            EditAiDetectionCommand = value.DetectionCommand ?? "";
            EditAiEnabled = value.Enabled;
            EditAiIsDefault = value.IsDefault;
        }
        else
        {
            EditAiName = "";
            EditAiCommand = "";
            EditAiIcon = "";
            EditAiDetectionCommand = "";
            EditAiEnabled = false;
            EditAiIsDefault = false;
        }
    }

    [RelayCommand]
    private void AddAiAssistant()
    {
        var newAi = new AiAssistant
        {
            Id = Guid.NewGuid().ToString(),
            Name = "New AI Assistant",
            Command = "",
            Icon = "AI",
            Enabled = false,
            IsDefault = false
        };

        AiAssistants.Add(newAi);
        SelectedAiAssistant = newAi;
        SyncRichModeToJson();
    }

    [RelayCommand]
    private void DeleteAiAssistant()
    {
        if (SelectedAiAssistant == null) return;

        // Don't allow deleting if it's the last enabled assistant
        if (SelectedAiAssistant.Enabled && AiAssistants.Count(a => a.Enabled) <= 1)
        {
            _dialogService.ShowWarning("Cannot delete the last enabled AI assistant.", "Warning");
            return;
        }

        var index = AiAssistants.IndexOf(SelectedAiAssistant);
        AiAssistants.Remove(SelectedAiAssistant);

        if (AiAssistants.Count > 0)
        {
            SelectedAiAssistant = AiAssistants[Math.Min(index, AiAssistants.Count - 1)];
        }
        else
        {
            SelectedAiAssistant = null;
        }

        SyncRichModeToJson();
    }

    [RelayCommand]
    private void ApplyAiAssistant()
    {
        if (SelectedAiAssistant == null) return;

        // If making this the default, clear other defaults
        if (EditAiIsDefault && !SelectedAiAssistant.IsDefault)
        {
            foreach (var ai in AiAssistants)
            {
                ai.IsDefault = false;
            }
        }

        SelectedAiAssistant.Name = EditAiName;
        SelectedAiAssistant.Command = EditAiCommand;
        SelectedAiAssistant.Icon = EditAiIcon;
        SelectedAiAssistant.DetectionCommand = string.IsNullOrWhiteSpace(EditAiDetectionCommand) ? null : EditAiDetectionCommand;
        SelectedAiAssistant.Enabled = EditAiEnabled;
        SelectedAiAssistant.IsDefault = EditAiIsDefault;

        // Ensure at least one is enabled
        if (!AiAssistants.Any(a => a.Enabled))
        {
            SelectedAiAssistant.Enabled = true;
            EditAiEnabled = true;
        }

        // Force collection refresh
        var index = AiAssistants.IndexOf(SelectedAiAssistant);
        if (index >= 0)
        {
            var ai = SelectedAiAssistant;
            AiAssistants.RemoveAt(index);
            AiAssistants.Insert(index, ai);
            SelectedAiAssistant = ai;
        }

        SyncRichModeToJson();
    }

    [RelayCommand]
    private void ResetAiAssistants()
    {
        AiAssistants = new ObservableCollection<AiAssistant>(AiAssistant.GetDefaults());
        SelectedAiAssistant = null;
        SyncRichModeToJson();
    }

    // Link pattern management
    partial void OnSelectedLinkPatternChanged(LinkPattern? value)
    {
        if (value != null)
        {
            EditLpName = value.Name;
            EditLpPattern = value.Pattern;
            EditLpUrlTemplate = value.UrlTemplate;
            EditLpEnabled = value.Enabled;
            EditLpPriority = value.Priority;
            // Clear test results
            RegexTestInput = "";
            RegexTestMatch = "";
            RegexTestUrl = "";
        }
        else
        {
            EditLpName = "";
            EditLpPattern = "";
            EditLpUrlTemplate = "";
            EditLpEnabled = true;
            EditLpPriority = 0;
        }
        UpdateRegexTest();
    }

    partial void OnEditLpPatternChanged(string value) => UpdateRegexTest();
    partial void OnEditLpUrlTemplateChanged(string value) => UpdateRegexTest();
    partial void OnRegexTestInputChanged(string value) => UpdateRegexTest();

    private void UpdateRegexTest()
    {
        if (string.IsNullOrEmpty(EditLpPattern))
        {
            RegexTestIsValid = true;
            RegexTestError = "";
            RegexTestMatch = "";
            RegexTestUrl = "";
            return;
        }

        try
        {
            var regex = new Regex(EditLpPattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));
            RegexTestIsValid = true;
            RegexTestError = "";

            if (!string.IsNullOrEmpty(RegexTestInput))
            {
                var match = regex.Match(RegexTestInput);
                if (match.Success)
                {
                    RegexTestMatch = match.Value;
                    // Build URL from template
                    var url = EditLpUrlTemplate ?? "";
                    url = url.Replace("$0", match.Value);
                    for (int i = 1; i < match.Groups.Count; i++)
                    {
                        url = url.Replace($"${i}", match.Groups[i].Value);
                    }
                    RegexTestUrl = url;
                }
                else
                {
                    RegexTestMatch = "(no match)";
                    RegexTestUrl = "";
                }
            }
            else
            {
                RegexTestMatch = "";
                RegexTestUrl = "";
            }
        }
        catch (RegexParseException ex)
        {
            RegexTestIsValid = false;
            RegexTestError = ex.Message;
            RegexTestMatch = "";
            RegexTestUrl = "";
        }
        catch (RegexMatchTimeoutException)
        {
            RegexTestIsValid = false;
            RegexTestError = "Pattern timed out (too complex)";
            RegexTestMatch = "";
            RegexTestUrl = "";
        }
    }

    [RelayCommand]
    private void AddLinkPattern()
    {
        var newPattern = new LinkPattern
        {
            Id = Guid.NewGuid().ToString(),
            Name = "New Pattern",
            Pattern = "",
            UrlTemplate = "",
            Enabled = true,
            Priority = 0
        };

        LinkPatterns.Add(newPattern);
        SelectedLinkPattern = newPattern;
        SyncRichModeToJson();
    }

    [RelayCommand]
    private void DeleteLinkPattern()
    {
        if (SelectedLinkPattern == null) return;

        var index = LinkPatterns.IndexOf(SelectedLinkPattern);
        LinkPatterns.Remove(SelectedLinkPattern);

        if (LinkPatterns.Count > 0)
        {
            SelectedLinkPattern = LinkPatterns[Math.Min(index, LinkPatterns.Count - 1)];
        }
        else
        {
            SelectedLinkPattern = null;
        }

        SyncRichModeToJson();
    }

    [RelayCommand]
    private void ApplyLinkPattern()
    {
        if (SelectedLinkPattern == null) return;

        SelectedLinkPattern.Name = EditLpName;
        SelectedLinkPattern.Pattern = EditLpPattern;
        SelectedLinkPattern.UrlTemplate = EditLpUrlTemplate;
        SelectedLinkPattern.Enabled = EditLpEnabled;
        SelectedLinkPattern.Priority = EditLpPriority;

        // Force collection refresh
        var index = LinkPatterns.IndexOf(SelectedLinkPattern);
        if (index >= 0)
        {
            var pattern = SelectedLinkPattern;
            LinkPatterns.RemoveAt(index);
            LinkPatterns.Insert(index, pattern);
            SelectedLinkPattern = pattern;
        }

        SyncRichModeToJson();
    }

    [RelayCommand]
    private void MoveLinkPatternUp()
    {
        if (SelectedLinkPattern == null) return;

        var index = LinkPatterns.IndexOf(SelectedLinkPattern);
        if (index > 0)
        {
            var pattern = SelectedLinkPattern;
            LinkPatterns.RemoveAt(index);
            LinkPatterns.Insert(index - 1, pattern);
            SelectedLinkPattern = pattern;
            SyncRichModeToJson();
        }
    }

    [RelayCommand]
    private void MoveLinkPatternDown()
    {
        if (SelectedLinkPattern == null) return;

        var index = LinkPatterns.IndexOf(SelectedLinkPattern);
        if (index < LinkPatterns.Count - 1)
        {
            var pattern = SelectedLinkPattern;
            LinkPatterns.RemoveAt(index);
            LinkPatterns.Insert(index + 1, pattern);
            SelectedLinkPattern = pattern;
            SyncRichModeToJson();
        }
    }

    // Project type management
    partial void OnSelectedProjectTypeChanged(ProjectType? value)
    {
        if (value != null)
        {
            EditPtId = value.Id;
            EditPtName = value.Name;
            EditPtDetectFiles = string.Join("\n", value.DetectFiles);
            EditPtDefaultCommand = value.DefaultCommand;
            EditPtWatchCommand = value.WatchCommand ?? "";
            EditPtUrlPattern = value.UrlPattern ?? "";
            EditPtPriority = value.Priority;
        }
        else
        {
            EditPtId = "";
            EditPtName = "";
            EditPtDetectFiles = "";
            EditPtDefaultCommand = "";
            EditPtWatchCommand = "";
            EditPtUrlPattern = "";
            EditPtPriority = 0;
        }
    }

    [RelayCommand]
    private void AddProjectType()
    {
        var newType = new ProjectType
        {
            Id = $"custom-{DateTime.Now:yyyyMMddHHmmss}",
            Name = "New Project Type",
            DetectFiles = [],
            DefaultCommand = "",
            Priority = 0
        };

        ProjectTypes.Add(newType);
        SelectedProjectType = newType;
        SyncRichModeToJson();
    }

    [RelayCommand]
    private void DeleteProjectType()
    {
        if (SelectedProjectType == null) return;

        var index = ProjectTypes.IndexOf(SelectedProjectType);
        ProjectTypes.Remove(SelectedProjectType);

        if (ProjectTypes.Count > 0)
        {
            SelectedProjectType = ProjectTypes[Math.Min(index, ProjectTypes.Count - 1)];
        }
        else
        {
            SelectedProjectType = null;
        }

        SyncRichModeToJson();
    }

    [RelayCommand]
    private void ApplyProjectType()
    {
        if (SelectedProjectType == null) return;

        SelectedProjectType.Id = EditPtId;
        SelectedProjectType.Name = EditPtName;
        SelectedProjectType.DetectFiles = EditPtDetectFiles
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        SelectedProjectType.DefaultCommand = EditPtDefaultCommand;
        SelectedProjectType.WatchCommand = string.IsNullOrWhiteSpace(EditPtWatchCommand) ? null : EditPtWatchCommand;
        SelectedProjectType.UrlPattern = string.IsNullOrWhiteSpace(EditPtUrlPattern) ? null : EditPtUrlPattern;
        SelectedProjectType.Priority = EditPtPriority;

        // Force collection refresh
        var index = ProjectTypes.IndexOf(SelectedProjectType);
        if (index >= 0)
        {
            var type = SelectedProjectType;
            ProjectTypes.RemoveAt(index);
            ProjectTypes.Insert(index, type);
            SelectedProjectType = type;
        }

        SyncRichModeToJson();
    }

    [RelayCommand]
    private void MoveProjectTypeUp()
    {
        if (SelectedProjectType == null) return;

        var index = ProjectTypes.IndexOf(SelectedProjectType);
        if (index > 0)
        {
            var type = SelectedProjectType;
            ProjectTypes.RemoveAt(index);
            ProjectTypes.Insert(index - 1, type);
            SelectedProjectType = type;
            SyncRichModeToJson();
        }
    }

    [RelayCommand]
    private void MoveProjectTypeDown()
    {
        if (SelectedProjectType == null) return;

        var index = ProjectTypes.IndexOf(SelectedProjectType);
        if (index < ProjectTypes.Count - 1)
        {
            var type = SelectedProjectType;
            ProjectTypes.RemoveAt(index);
            ProjectTypes.Insert(index + 1, type);
            SelectedProjectType = type;
            SyncRichModeToJson();
        }
    }

    // Directory settings management
    partial void OnSelectedDirectoryChanged(string? value)
    {
        UpdateCurrentDirectorySettings();
        LoadDirectorySettingsToEdit();
    }

    private void LoadDirectorySettingsToEdit()
    {
        if (CurrentDirectorySettings != null)
        {
            EditDsLayoutMode = CurrentDirectorySettings.LayoutMode;
            EditDsSplitRatio = CurrentDirectorySettings.SplitRatio;
            EditDsShowRunTerminal = CurrentDirectorySettings.IsRunTerminalVisible;
            EditDsRunSplitRatio = CurrentDirectorySettings.RunSplitRatio;
            EditDsShowExplorer = CurrentDirectorySettings.IsExplorerVisible;
            EditDsExplorerRatio = CurrentDirectorySettings.ExplorerSplitRatio;
            EditDsShowLeftPanel = CurrentDirectorySettings.IsLeftPanelVisible;
            EditDsLeftPanelRatio = CurrentDirectorySettings.LeftPanelSplitRatio;
            RunConfigurations = new ObservableCollection<RunConfiguration>(CurrentDirectorySettings.RunConfigurations);

            // Load AI assistant selection
            var aiId = CurrentDirectorySettings.ActiveAiAssistantId;
            EditDsAiAssistant = !string.IsNullOrEmpty(aiId)
                ? AiAssistants.FirstOrDefault(a => a.Id == aiId)
                : AiAssistants.FirstOrDefault(a => a.IsDefault) ?? AiAssistants.FirstOrDefault();
        }
        else
        {
            EditDsLayoutMode = TerminalLayoutMode.HorizontalSplit;
            EditDsSplitRatio = 0.6;
            EditDsShowRunTerminal = false;
            EditDsRunSplitRatio = 0.3;
            EditDsShowExplorer = false;
            EditDsExplorerRatio = 0.25;
            EditDsShowLeftPanel = false;
            EditDsLeftPanelRatio = 0.25;
            RunConfigurations = [];
            EditDsAiAssistant = AiAssistants.FirstOrDefault(a => a.IsDefault) ?? AiAssistants.FirstOrDefault();
        }
        SelectedRunConfiguration = null;
    }

    partial void OnSelectedRunConfigurationChanged(RunConfiguration? value)
    {
        if (value != null)
        {
            EditRcName = value.Name;
            EditRcCommand = value.Command;
            EditRcIsDefault = value.IsDefault;
            EditRcUrlPattern = value.UrlPattern ?? "";
        }
        else
        {
            EditRcName = "";
            EditRcCommand = "";
            EditRcIsDefault = false;
            EditRcUrlPattern = "";
        }
    }

    [RelayCommand]
    private void ApplyDirectorySettings()
    {
        if (SelectedDirectory == null || CurrentDirectorySettings == null) return;

        CurrentDirectorySettings.LayoutMode = EditDsLayoutMode;
        CurrentDirectorySettings.SplitRatio = EditDsSplitRatio;
        CurrentDirectorySettings.IsRunTerminalVisible = EditDsShowRunTerminal;
        CurrentDirectorySettings.RunSplitRatio = EditDsRunSplitRatio;
        CurrentDirectorySettings.IsExplorerVisible = EditDsShowExplorer;
        CurrentDirectorySettings.ExplorerSplitRatio = EditDsExplorerRatio;
        CurrentDirectorySettings.IsLeftPanelVisible = EditDsShowLeftPanel;
        CurrentDirectorySettings.LeftPanelSplitRatio = EditDsLeftPanelRatio;
        CurrentDirectorySettings.RunConfigurations = RunConfigurations.ToList();
        CurrentDirectorySettings.ActiveAiAssistantId = EditDsAiAssistant?.Id;

        SyncRichModeToJson();
    }

    [RelayCommand]
    private void DeleteDirectorySettings()
    {
        if (SelectedDirectory == null) return;

        try
        {
            var config = JsonSerializer.Deserialize<AppConfiguration>(JsonText, JsonOptions);
            if (config?.DirectorySettings.ContainsKey(SelectedDirectory) == true)
            {
                config.DirectorySettings.Remove(SelectedDirectory);
                JsonText = JsonSerializer.Serialize(config, JsonOptions);
                IsDirty = JsonText != _originalJson;

                // Refresh directories list
                Directories = new ObservableCollection<string>(config.DirectorySettings.Keys.OrderBy(k => k));
                if (Directories.Count > 0)
                {
                    SelectedDirectory = Directories.First();
                }
                else
                {
                    SelectedDirectory = null;
                    CurrentDirectorySettings = null;
                }
            }
        }
        catch (JsonException ex)
        {
            ErrorMessage = $"Cannot delete directory settings: {ex.Message}";
            HasError = true;
        }
    }

    [RelayCommand]
    private void AddRunConfiguration()
    {
        var newConfig = new RunConfiguration
        {
            Id = Guid.NewGuid().ToString(),
            Name = "New Configuration",
            Command = "",
            IsDefault = RunConfigurations.Count == 0
        };

        RunConfigurations.Add(newConfig);
        SelectedRunConfiguration = newConfig;
    }

    [RelayCommand]
    private void DeleteRunConfiguration()
    {
        if (SelectedRunConfiguration == null) return;

        var index = RunConfigurations.IndexOf(SelectedRunConfiguration);
        RunConfigurations.Remove(SelectedRunConfiguration);

        if (RunConfigurations.Count > 0)
        {
            SelectedRunConfiguration = RunConfigurations[Math.Min(index, RunConfigurations.Count - 1)];
        }
        else
        {
            SelectedRunConfiguration = null;
        }
    }

    [RelayCommand]
    private void ApplyRunConfiguration()
    {
        if (SelectedRunConfiguration == null) return;

        SelectedRunConfiguration.Name = EditRcName;
        SelectedRunConfiguration.Command = EditRcCommand;
        SelectedRunConfiguration.UrlPattern = string.IsNullOrWhiteSpace(EditRcUrlPattern) ? null : EditRcUrlPattern;

        // Handle default flag
        if (EditRcIsDefault)
        {
            foreach (var config in RunConfigurations)
            {
                config.IsDefault = config == SelectedRunConfiguration;
            }
        }
        else
        {
            SelectedRunConfiguration.IsDefault = false;
        }

        // Force collection refresh
        var index = RunConfigurations.IndexOf(SelectedRunConfiguration);
        if (index >= 0)
        {
            var config = SelectedRunConfiguration;
            RunConfigurations.RemoveAt(index);
            RunConfigurations.Insert(index, config);
            SelectedRunConfiguration = config;
        }
    }
}
