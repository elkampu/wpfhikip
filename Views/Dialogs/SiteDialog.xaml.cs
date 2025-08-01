using System.Windows;

using wpfhikip.Models;
using wpfhikip.ViewModels.Dialogs;

namespace wpfhikip.Views.Dialogs
{
    /// <summary>
    /// Interaction logic for SiteDialog.xaml
    /// </summary>
    public partial class SiteDialog : Window
    {
        private readonly SiteDialogViewModel _viewModel;

        public SiteDialog(Site? site = null, string? clientId = null)
        {
            InitializeComponent();

            _viewModel = new SiteDialogViewModel(site, clientId);
            DataContext = _viewModel;

            // Subscribe to ViewModel events
            _viewModel.RequestClose += OnRequestClose;
        }

        private void OnRequestClose(bool? dialogResult)
        {
            DialogResult = dialogResult;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            // Unsubscribe from events
            _viewModel.RequestClose -= OnRequestClose;
            base.OnClosed(e);
        }

        /// <summary>
        /// Gets the site data after dialog closes successfully
        /// </summary>
        public Site? SiteResult => _viewModel.Site;
    }
}