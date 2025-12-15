using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Domain;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

public partial class ProfilesTabViewModel : ObservableObject, ITabViewModel
{
    private readonly IProfileRegistry _profileRegistry;

    [ObservableProperty]
    private string _title = "Profiles";

    [ObservableProperty]
    private ObservableCollection<ProfileItemViewModel> _profiles = new();

    [ObservableProperty]
    private ProfileItemViewModel? _selectedProfile;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editName = "";

    [ObservableProperty]
    private string _editCommand = "";

    [ObservableProperty]
    private string _editIcon = "";

    [ObservableProperty]
    private string _editShortcut = "";

    [ObservableProperty]
    private bool _editAutoStart;

    [ObservableProperty]
    private string? _errorMessage;

    public string TabIcon => "\uD83D\uDC64"; // Profile icon
    public string WorkingDirectory => "Profiles";
    public bool IsCloseable => true;
    public bool IsAnyTerminalActive => false;
    public string DisplayTitle => Title;

    public event EventHandler? CloseRequested;

    /// <summary>
    /// Event raised when a profile launch is requested.
    /// </summary>
    public event EventHandler<ProfileLaunchEventArgs>? ProfileLaunchRequested;

    public ProfilesTabViewModel(IProfileRegistry profileRegistry)
    {
        _profileRegistry = profileRegistry;
        LoadProfiles();
    }

    private void LoadProfiles()
    {
        Profiles.Clear();
        foreach (var profile in _profileRegistry.Profiles)
        {
            Profiles.Add(new ProfileItemViewModel(profile));
        }
    }

    [RelayCommand]
    private void AddNew()
    {
        // Create a new profile with defaults
        var newId = $"profile-{DateTime.Now:yyyyMMddHHmmss}";
        EditName = "New Profile";
        EditCommand = "pwsh.exe";
        EditIcon = "\uD83D\uDCBB"; // Computer icon
        EditShortcut = "";
        EditAutoStart = false;
        SelectedProfile = null;
        IsEditing = true;
        ErrorMessage = null;
    }

    [RelayCommand]
    private void StartEdit()
    {
        if (SelectedProfile == null) return;

        EditName = SelectedProfile.Name;
        EditCommand = SelectedProfile.Command;
        EditIcon = SelectedProfile.Icon ?? "";
        EditShortcut = SelectedProfile.Shortcut ?? "";
        EditAutoStart = SelectedProfile.AutoStart;
        IsEditing = true;
        ErrorMessage = null;
    }

    [RelayCommand]
    private void Save()
    {
        // Validate
        if (string.IsNullOrWhiteSpace(EditName))
        {
            ErrorMessage = "Name is required";
            return;
        }

        if (string.IsNullOrWhiteSpace(EditCommand))
        {
            ErrorMessage = "Command is required";
            return;
        }

        ErrorMessage = null;

        if (SelectedProfile == null)
        {
            // Creating new profile
            var newProfile = new Profile
            {
                Id = $"profile-{DateTime.Now:yyyyMMddHHmmss}",
                Name = EditName.Trim(),
                Command = EditCommand.Trim(),
                Icon = string.IsNullOrWhiteSpace(EditIcon) ? null : EditIcon.Trim(),
                Shortcut = string.IsNullOrWhiteSpace(EditShortcut) ? null : EditShortcut.Trim(),
                AutoStart = EditAutoStart
            };
            _profileRegistry.AddProfile(newProfile);
        }
        else
        {
            // Updating existing profile
            var updatedProfile = new Profile
            {
                Id = SelectedProfile.Id,
                Name = EditName.Trim(),
                Command = EditCommand.Trim(),
                Icon = string.IsNullOrWhiteSpace(EditIcon) ? null : EditIcon.Trim(),
                Shortcut = string.IsNullOrWhiteSpace(EditShortcut) ? null : EditShortcut.Trim(),
                AutoStart = EditAutoStart
            };
            _profileRegistry.UpdateProfile(updatedProfile);
        }

        IsEditing = false;
        LoadProfiles();
    }

    [RelayCommand]
    private void Cancel()
    {
        IsEditing = false;
        ErrorMessage = null;
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedProfile == null) return;

        _profileRegistry.RemoveProfile(SelectedProfile.Id);
        IsEditing = false;
        SelectedProfile = null;
        LoadProfiles();
    }

    [RelayCommand]
    private void Launch()
    {
        if (SelectedProfile == null) return;

        var profile = CreateProfileFromItem(SelectedProfile);
        ProfileLaunchRequested?.Invoke(this, new ProfileLaunchEventArgs { Profile = profile, PickFolder = false });
    }

    [RelayCommand]
    private void LaunchWithFolder()
    {
        if (SelectedProfile == null) return;

        var profile = CreateProfileFromItem(SelectedProfile);
        ProfileLaunchRequested?.Invoke(this, new ProfileLaunchEventArgs { Profile = profile, PickFolder = true });
    }

    private static Profile CreateProfileFromItem(ProfileItemViewModel item)
    {
        return new Profile
        {
            Id = item.Id,
            Name = item.Name,
            Command = item.Command,
            Icon = item.Icon,
            Shortcut = item.Shortcut,
            AutoStart = item.AutoStart
        };
    }

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}

public class ProfileLaunchEventArgs : EventArgs
{
    public required Profile Profile { get; init; }
    public bool PickFolder { get; init; }
}

public partial class ProfileItemViewModel : ObservableObject
{
    public string Id { get; }
    public string Name { get; }
    public string Command { get; }
    public string? Icon { get; }
    public string? Shortcut { get; }
    public bool AutoStart { get; }

    public string DisplayName => $"{Icon ?? "\uD83D\uDCBB"} {Name}";
    public string DisplayInfo => AutoStart ? $"{Command} (Auto-start)" : Command;

    public ProfileItemViewModel(Profile profile)
    {
        Id = profile.Id;
        Name = profile.Name;
        Command = profile.Command;
        Icon = profile.Icon;
        Shortcut = profile.Shortcut;
        AutoStart = profile.AutoStart;
    }
}
