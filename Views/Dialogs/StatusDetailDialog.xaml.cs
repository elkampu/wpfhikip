using System.Windows;

using wpfhikip.Models;
using wpfhikip.ViewModels;

namespace wpfhikip.Views.Dialogs
{
    /// <summary>
    /// Interaction logic for StatusDetailDialog.xaml
    /// </summary>
    public partial class StatusDetailDialog : Window
    {
        private readonly StatusDetailDialogViewModel _viewModel;

        public StatusDetailDialog(Camera camera)
        {
            InitializeComponent();

            _viewModel = new StatusDetailDialogViewModel(camera);
            DataContext = _viewModel;

            // Subscribe to ViewModel events
            _viewModel.RequestClipboardCopy += OnRequestClipboardCopy;
            _viewModel.RequestClose += OnRequestClose;
        }

        private void OnRequestClipboardCopy(string text)
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to copy logs to clipboard: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnRequestClose()
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            // Unsubscribe from events
            if (_viewModel != null)
            {
                _viewModel.RequestClipboardCopy -= OnRequestClipboardCopy;
                _viewModel.RequestClose -= OnRequestClose;
                _viewModel.Dispose();
            }

            base.OnClosed(e);
        }
    }
}