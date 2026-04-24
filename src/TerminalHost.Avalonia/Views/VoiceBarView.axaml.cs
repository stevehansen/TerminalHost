using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using TerminalHost.Core.Domain;
using TerminalHost.Core.ViewModels;

namespace TerminalHost.Views;

public partial class VoiceBarView : UserControl
{
    private static readonly Color AccentBlue = Color.Parse("#0078D4");
    private static readonly Color PulseRed = Color.Parse("#F44747");
    private static readonly Color SuccessGreen = Color.Parse("#32CD32");
    private static readonly Color AmberWarning = Color.Parse("#F59E0B");

    public VoiceBarView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is VoiceBarViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(VoiceBarViewModel.State)) return;
        if (sender is not VoiceBarViewModel vm) return;

        Dispatcher.UIThread.Post(() => UpdateBorderColor(vm));
    }

    private void UpdateBorderColor(VoiceBarViewModel vm)
    {
        // Update border color based on state
        // Note: In Avalonia, we set the color directly. For pulse animation,
        // we rely on CSS-style animations in the AXAML which are state-driven.
        var color = vm.State switch
        {
            VoiceFlowState.Listening => AccentBlue, // Animation handles the pulse
            VoiceFlowState.Executed => SuccessGreen,
            VoiceFlowState.NoMatch => AmberWarning,
            _ => AccentBlue
        };

        BarBorder.BorderBrush = new SolidColorBrush(color);
    }
}
