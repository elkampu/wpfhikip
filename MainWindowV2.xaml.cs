using System.Windows;

using UPnP;

using wpfhikip.Views;

namespace wpfhikip
{
    /// <summary>
    /// Interaction logic for MainWindowV2.xaml
    /// </summary>
    public partial class MainWindowV2 : Window
    {
        private NetworkDiscoveryView? _networkDiscoveryWindow;
        private NetConfView? _netConfWindow;
        private SiteManagerView? _siteManagerWindow;

        public MainWindowV2()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Opens the Network Discovery Tool window (single instance)
        /// </summary>
        private void NetworkScannerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_networkDiscoveryWindow == null || !_networkDiscoveryWindow.IsLoaded)
                {
                    _networkDiscoveryWindow = new NetworkDiscoveryView();
                    _networkDiscoveryWindow.Closed += (s, args) => _networkDiscoveryWindow = null;
                }

                _networkDiscoveryWindow.Show();
                _networkDiscoveryWindow.Activate();
                _networkDiscoveryWindow.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening Network Scanner: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Opens the Network Configuration Manager window (single instance)
        /// </summary>
        private void BatchConfigurationButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_netConfWindow == null || !_netConfWindow.IsLoaded)
                {
                    _netConfWindow = new NetConfView();
                    _netConfWindow.Closed += (s, args) => _netConfWindow = null;
                }

                _netConfWindow.Show();
                _netConfWindow.Activate();
                _netConfWindow.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening Batch Configuration: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        /// <summary>
        /// Opens the Site Manager window (single instance)
        /// </summary>
        private void SiteManagerButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_siteManagerWindow == null || !_siteManagerWindow.IsLoaded)
                {
                    _siteManagerWindow = new SiteManagerView();
                    _siteManagerWindow.Closed += (s, args) => _siteManagerWindow = null;
                }

                _siteManagerWindow.Show();
                _siteManagerWindow.Activate();
                _siteManagerWindow.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening Site Manager: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // Clean up any open windows when main window closes
            _networkDiscoveryWindow?.Close();
            _netConfWindow?.Close();
            _siteManagerWindow?.Close();
            base.OnClosed(e);
        }
    }
}