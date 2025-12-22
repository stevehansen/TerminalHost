using System.Windows;
using TerminalHost.Services;
using TerminalHost.ViewModels;
using TerminalHost.Windows.Platform;

namespace TerminalHost.Views
{
    public partial class SetupWindow : Window
    {
        /// <summary>
        /// When true, shows "Continue to App" button (startup mode).
        /// When false, only shows "Close" button (in-app mode).
        /// </summary>
        public bool IsStartupMode { get; set; } = true;

        public SetupWindow(SetupViewModel viewModel, bool isStartupMode = true)
        {
            InitializeComponent();
            DataContext = viewModel;
            IsStartupMode = isStartupMode;

            SourceInitialized += (s, e) => DarkModeHelper.EnableDarkMode(this);

            Loaded += async (s, e) =>
            {
                // Hide Continue button if not in startup mode
                ContinueButton.Visibility = IsStartupMode ? Visibility.Visible : Visibility.Collapsed;

                if (DataContext is SetupViewModel vm)
                {
                    await vm.CheckAllDependenciesCommand.ExecuteAsync(null);
                }
            };
        }

        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void CopyInstallCommand_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.Tag is string command)
            {
                try
                {
                    Clipboard.SetText(command);
                    // Briefly change button text to indicate success
                    var originalContent = button.Content;
                    button.Content = "Copied!";
                    var timer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(1.5)
                    };
                    timer.Tick += (s, args) =>
                    {
                        button.Content = originalContent;
                        timer.Stop();
                    };
                    timer.Start();
                }
                catch
                {
                    // Clipboard operation failed
                }
            }
        }
    }
}

