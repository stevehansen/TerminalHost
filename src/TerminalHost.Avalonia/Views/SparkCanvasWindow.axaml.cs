using Avalonia.Controls;
using TerminalHost.ViewModels;

namespace TerminalHost.Views;

public partial class SparkCanvasWindow : Window
{
    public SparkCanvasWindow()
    {
        InitializeComponent();
    }

    public SparkCanvasWindow(SparkCanvasViewModel viewModel) : this()
    {
        CanvasView.DataContext = viewModel;
    }
}
