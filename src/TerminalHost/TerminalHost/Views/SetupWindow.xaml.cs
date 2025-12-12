using System.Windows;
using TerminalHost.ViewModels;

namespace TerminalHost.Views
{
    public partial class SetupWindow : Window
    {
        public SetupWindow(SetupViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            Loaded += async (s, e) =>
            {
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
    }
}

