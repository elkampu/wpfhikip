using System.Collections.ObjectModel;
using System.Windows;

using Microsoft.Win32;

using wpfhikip.Models;
using wpfhikip.Services;

namespace wpfhikip.ViewModels.Services
{
    /// <summary>
    /// Service responsible for managing client operations
    /// </summary>
    public class ClientManagementService
    {
        private readonly SiteDataService _dataService;

        public ClientManagementService(SiteDataService dataService)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        }

        public void AddClient(ObservableCollection<Client> clients, Action<Client> onClientAdded)
        {
            var dialog = new Views.Dialogs.ClientDialog();
            dialog.Owner = Application.Current.MainWindow;

            if (dialog.ShowDialog() == true)
            {
                var newClient = dialog.ClientResult;
                if (newClient != null)
                {
                    clients.Add(newClient);
                    onClientAdded?.Invoke(newClient);
                }
            }
        }

        public void EditClient(Client selectedClient, Action onClientUpdated)
        {
            if (selectedClient == null) return;

            var dialog = new Views.Dialogs.ClientDialog(selectedClient);
            dialog.Owner = Application.Current.MainWindow;

            if (dialog.ShowDialog() == true)
            {
                var updatedClient = dialog.ClientResult;
                if (updatedClient != null)
                {
                    UpdateClientProperties(selectedClient, updatedClient);
                    onClientUpdated?.Invoke();
                }
            }
        }

        public void DeleteClient(ObservableCollection<Client> clients, Client selectedClient,
            Action<Client> onClientDeleted)
        {
            if (selectedClient == null) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete client '{selectedClient.Name}' and all its sites?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                clients.Remove(selectedClient);
                onClientDeleted?.Invoke(selectedClient);
            }
        }

        public async Task ExportClientAsync(Client selectedClient)
        {
            if (selectedClient == null) return;

            var saveDialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = $"{selectedClient.Name}_export.json"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    await _dataService.ExportClientAsync(selectedClient, saveDialog.FileName);
                    MessageBox.Show("Client exported successfully!", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting client: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private static void UpdateClientProperties(Client target, Client source)
        {
            target.Name = source.Name;
            target.ContactPerson = source.ContactPerson;
            target.Email = source.Email;
            target.Phone = source.Phone;
            target.Address = source.Address;
            target.Notes = source.Notes;
            target.LastModified = source.LastModified;
        }
    }
}