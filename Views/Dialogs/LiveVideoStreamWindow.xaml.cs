using System.Windows;
using System.Windows.Media;

using wpfhikip.Models;
using wpfhikip.ViewModels.Dialogs;

namespace wpfhikip.Views.Dialogs
{
    /// <summary>
    /// Interaction logic for LiveVideoStreamWindow.xaml
    /// </summary>
    public partial class LiveVideoStreamWindow : Window
    {
        private readonly LiveVideoStreamViewModel _viewModel;

        public LiveVideoStreamWindow(Camera camera)
        {
            InitializeComponent();

            _viewModel = new LiveVideoStreamViewModel(camera);
            DataContext = _viewModel;

            // Subscribe to ViewModel events
            _viewModel.RequestClose += OnRequestClose;
        }

        private void OnRequestClose()
        {
            Close();
        }

        private void VideoPlayer_Loaded(object sender, RoutedEventArgs e)
        {
            // VideoPlayer is now loaded and ready
            // The MediaPlayer binding should be active now
        }

        protected override void OnClosed(EventArgs e)
        {
            // Cleanup
            if (_viewModel != null)
            {
                _viewModel.RequestClose -= OnRequestClose;
                _viewModel.Dispose();
            }

            base.OnClosed(e);
        }
    }

    public enum VideoPlayerAction
    {
        Play,
        Pause,
        Stop,
        Close
    }
}