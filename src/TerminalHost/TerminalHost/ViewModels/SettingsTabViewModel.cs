using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Domain;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

public partial class SettingsTabViewModel : ObservableObject, ITabViewModel
{
    private readonly ConfigurationService _configService;
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

    public string TabIcon => "\u2699"; // Gear symbol
    public string WorkingDirectory => "Settings";
    public bool IsCloseable => true;
    public bool IsAnyTerminalActive => false;

    public event EventHandler? CloseRequested;
    public event EventHandler? JsonTextReloaded;
    public event EventHandler? ConfigSaved;

    public SettingsTabViewModel(ConfigurationService configService)
    {
        _configService = configService;
        LoadSettings();
    }

    public void LoadSettings()
    {
        _originalJson = _configService.LoadRawJson();
        JsonText = _originalJson;
        IsDirty = false;
        ErrorMessage = "";
        HasError = false;
        JsonTextReloaded?.Invoke(this, EventArgs.Empty);
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
                DialogService.ShowWarning($"Settings saved with warnings:\n\n{warning}", "Configuration Warning");
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
            var formatted = JsonSerializer.Serialize(parsed, new JsonSerializerOptions { WriteIndented = true });

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
            // Parse current config
            var parsed = JsonSerializer.Deserialize<AppConfiguration>(JsonText, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (parsed == null)
            {
                ErrorMessage = "Cannot parse current configuration";
                HasError = true;
                return;
            }

            // Create a fresh config to get default quick commands
            var defaultConfig = new AppConfiguration();

            // Replace just the quick commands
            parsed.QuickCommands = defaultConfig.QuickCommands;

            // Re-serialize
            var updatedJson = JsonSerializer.Serialize(parsed, new JsonSerializerOptions { WriteIndented = true });

            JsonText = updatedJson;
            IsDirty = updatedJson != _originalJson;
            ErrorMessage = "";
            HasError = false;
            JsonTextReloaded?.Invoke(this, EventArgs.Empty);
        }
        catch (JsonException ex)
        {
            ErrorMessage = $"Cannot reset quick commands: {ex.Message}";
            HasError = true;
        }
    }
}
