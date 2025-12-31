using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TerminalHost.Core.Interfaces;
using TerminalHost.Services;

namespace TerminalHost.ViewModels;

/// <summary>
/// View model for an individual toast notification.
/// </summary>
public partial class ToastViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = "";

    [ObservableProperty]
    private string _message = "";

    [ObservableProperty]
    private string _icon = "";

    [ObservableProperty]
    private ToastType _type = ToastType.Info;

    [ObservableProperty]
    private bool _isCloseable = true;

    [ObservableProperty]
    private bool _autoClose = true;

    [ObservableProperty]
    private TimeSpan _autoCloseDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Event raised when the toast requests to be closed.
    /// </summary>
    public event EventHandler? CloseRequested;

    [RelayCommand]
    private void Close()
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gets the default icon for a toast type.
    /// </summary>
    public static string GetDefaultIcon(ToastType type) => type switch
    {
        ToastType.Info => "\u2139",      // ℹ (info)
        ToastType.Success => "\u2713",   // ✓ (check mark)
        ToastType.Warning => "\u26A0",   // ⚠ (warning)
        ToastType.Error => "\u2715",     // ✕ (x mark)
        ToastType.Progress => "\u23F3",  // ⏳ (hourglass)
        _ => "\u2139"
    };
}
