using System.Windows;

using wpfhikip.ViewModels;

namespace wpfhikip.Views
{
    /// <summary>
    /// Interaction logic for NetworkDiscoveryView.xaml
    /// </summary>
    public partial class NetworkDiscoveryView : Window
    {
        private NetworkDiscoveryViewModel? _viewModel;

        public NetworkDiscoveryView()
        {
            InitializeComponent();
            _viewModel = new NetworkDiscoveryViewModel();
            DataContext = _viewModel;
        }

        protected override void OnClosed(EventArgs e)
        {
            // Immediately cancel and dispose without waiting
            // This allows the window to close quickly while cleanup happens in background
            try
            {
                // Clear data context first to stop any UI updates
                DataContext = null;

                // Start disposal asynchronously so it doesn't block the UI thread
                var viewModel = _viewModel;
                _viewModel = null;

                if (viewModel != null)
                {
                    // Dispose in background to not block window closing
                    Task.Run(() =>
                    {
                        try
                        {
                            viewModel.Dispose();
                        }
                        catch
                        {
                            // Ignore disposal errors
                        }
                    });
                }
            }
            catch
            {
                // Ignore any errors during cleanup
            }

            base.OnClosed(e);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Only ask user if scan is running, but don't wait for it to stop
            if (_viewModel?.IsScanning == true)
            {
                var result = MessageBox.Show(
                    "A scan is currently running. Do you want to stop it and close the window?",
                    "Stop Scan",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    return;
                }

                // Just trigger the stop command but don't wait for it
                if (_viewModel.StopScanCommand.CanExecute(null))
                {
                    _viewModel.StopScanCommand.Execute(null);
                }
            }

            base.OnClosing(e);
        }
    }
}