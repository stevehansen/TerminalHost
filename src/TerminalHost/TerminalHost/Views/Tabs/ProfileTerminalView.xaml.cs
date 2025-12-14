using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using TerminalHost.ViewModels;

namespace TerminalHost.Views.Tabs;

public partial class ProfileTerminalView : UserControl
{
    public ProfileTerminalView()
    {
        InitializeComponent();
    }

    private void OpenInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ProfileTerminalTabViewModel profileTab)
        {
            var workingDir = profileTab.WorkingDirectory;
            if (!string.IsNullOrEmpty(workingDir) && Directory.Exists(workingDir))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = workingDir,
                        UseShellExecute = true
                    });
                }
                catch
                {
                    // Ignore errors opening explorer
                }
            }
        }
    }
}
