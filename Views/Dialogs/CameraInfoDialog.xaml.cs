using System.Windows;

using wpfhikip.Models;
using wpfhikip.ViewModels.Dialogs;

namespace wpfhikip.Views.Dialogs
{
    /// <summary>
    /// Interaction logic for CameraInfoDialog.xaml
    /// </summary>
    public partial class CameraInfoDialog : Window
    {
        private readonly CameraInfoDialogViewModel _viewModel;

        public CameraInfoDialog(Camera camera)
        {
            InitializeComponent();

            _viewModel = new CameraInfoDialogViewModel(camera);
            DataContext = _viewModel;

            // Set window title
            Title = $"Camera Info - {camera.CurrentIP ?? "Unknown"}";

            // Load camera information when dialog opens
            Loaded += async (s, e) => await _viewModel.LoadCameraInfoAsync();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _viewModel?.Dispose();
            base.OnClosed(e);
        }
    }
}