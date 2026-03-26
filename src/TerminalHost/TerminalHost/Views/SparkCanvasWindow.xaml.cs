using System.ComponentModel;
using System.Windows;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

public partial class SparkCanvasWindow : Window
{
    public SparkCanvasWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (DataContext is SparkCanvasViewModel vm)
        {
            vm.Dispose();
        }

        base.OnClosing(e);
    }
}
