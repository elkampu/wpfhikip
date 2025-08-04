using System.Windows;

using wpfhikip.ViewModels;

namespace wpfhikip.Views.Dialogs
{
    public partial class ScanProgressDetailsWindow : Window
    {
        public ScanProgressDetailsWindow(NetworkDiscoveryViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}