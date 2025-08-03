using System.Windows;
using System.Windows.Controls;

using wpfhikip.Models;
using wpfhikip.Views.Dialogs;

namespace wpfhikip.Views
{
    /// <summary>
    /// Interaction logic for SiteManagerView.xaml
    /// </summary>
    public partial class SiteManagerView : Window
    {
        public SiteManagerView()
        {
            InitializeComponent();
        }

        private void ClientActionsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.IsOpen = true;
            }
        }

        private void SiteActionsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                button.ContextMenu.PlacementTarget = button;
                button.ContextMenu.IsOpen = true;
            }
        }

        /// <summary>
        /// Event handler for device info button clicks in the DataGrid
        /// Opens the Camera Information dialog with comprehensive device details
        /// </summary>
        private void DeviceInfoButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is Camera camera)
            {
                // Only proceed if the device compatibility check succeeded
                if (camera.CanShowCameraInfo)
                {
                    // Create and show the camera information dialog
                    var cameraInfoDialog = new CameraInfoDialog(camera);
                    cameraInfoDialog.Owner = this;
                    cameraInfoDialog.ShowDialog();
                }
            }
        }
    }
}