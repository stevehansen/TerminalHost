using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TerminalHost.Core.ViewModels;

namespace TerminalHost.Views;

public partial class VoiceBarView : UserControl
{
    private static readonly Color AccentBlue = (Color)ColorConverter.ConvertFromString("#0078D4");
    private static readonly Color PulseRed = (Color)ColorConverter.ConvertFromString("#F44747");
    private static readonly Color SuccessGreen = (Color)ColorConverter.ConvertFromString("#32CD32");

    private Storyboard? _pulseStoryboard;

    public VoiceBarView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is VoiceBarViewModel oldVm)
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        if (e.NewValue is VoiceBarViewModel newVm)
            newVm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(VoiceBarViewModel.State)) return;
        if (sender is not VoiceBarViewModel vm) return;

        Dispatcher.BeginInvoke(() => UpdateBorderAnimation(vm));
    }

    private void UpdateBorderAnimation(VoiceBarViewModel vm)
    {
        // Stop any existing pulse
        StopPulse();

        var brush = new SolidColorBrush(AccentBlue);
        BarBorder.BorderBrush = brush;

        switch (vm.State)
        {
            case Core.Domain.VoiceFlowState.Listening:
                // Pulse between blue and red
                _pulseStoryboard = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
                var pulse = new ColorAnimation
                {
                    From = AccentBlue,
                    To = PulseRed,
                    Duration = TimeSpan.FromSeconds(0.8),
                    AutoReverse = true
                };
                Storyboard.SetTarget(pulse, BarBorder);
                Storyboard.SetTargetProperty(pulse,
                    new PropertyPath("(Border.BorderBrush).(SolidColorBrush.Color)"));
                _pulseStoryboard.Children.Add(pulse);
                _pulseStoryboard.Begin();
                break;

            case Core.Domain.VoiceFlowState.Executed:
                BarBorder.BorderBrush = new SolidColorBrush(SuccessGreen);
                break;

            case Core.Domain.VoiceFlowState.NoMatch:
                BarBorder.BorderBrush = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#F59E0B")); // Amber
                break;
        }
    }

    private void StopPulse()
    {
        if (_pulseStoryboard is not null)
        {
            _pulseStoryboard.Stop();
            _pulseStoryboard = null;
        }
    }
}
