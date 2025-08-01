using System.Windows;

using wpfhikip.Models;
using wpfhikip.ViewModels.Dialogs;

namespace wpfhikip.Views.Dialogs
{
    /// <summary>
    /// Interaction logic for ClientDialog.xaml
    /// </summary>
    public partial class ClientDialog : Window
    {
        private readonly ClientDialogViewModel _viewModel;

        public ClientDialog(Client? client = null)
        {
            InitializeComponent();

            _viewModel = new ClientDialogViewModel(client);
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
        /// Gets the client data after dialog closes successfully
        /// </summary>
        public Client? ClientResult => _viewModel.Client;
    }
}