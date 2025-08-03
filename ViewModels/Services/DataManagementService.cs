using System.Collections.ObjectModel;
using System.Windows;

using wpfhikip.Models;
using wpfhikip.Services;

namespace wpfhikip.ViewModels.Services
{
    /// <summary>
    /// Service responsible for data loading and saving operations
    /// </summary>
    public class DataManagementService
    {
        private readonly SiteDataService _dataService;

        public DataManagementService(SiteDataService dataService)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
        }

        public async Task<(ObservableCollection<Client> clients, bool success)> LoadDataAsync()
        {
            try
            {
                var clientsTask = _dataService.LoadClientsAsync();
                var sitesTask = _dataService.LoadSitesAsync();

                await Task.WhenAll(clientsTask, sitesTask);

                var clients = await clientsTask;
                var allSites = await sitesTask;

                AssociateSitesWithClients(clients, allSites);

                return (clients, true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return (new ObservableCollection<Client>(), false);
            }
        }

        public async Task<bool> SaveDataAsync(ObservableCollection<Client> clients)
        {
            try
            {
                var allSites = FlattenSitesFromClients(clients);

                var clientsTask = _dataService.SaveClientsAsync(clients);
                var sitesTask = _dataService.SaveSitesAsync(allSites);

                await Task.WhenAll(clientsTask, sitesTask);

                MessageBox.Show("Data saved successfully!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private static void AssociateSitesWithClients(ObservableCollection<Client> clients,
            ObservableCollection<Site> allSites)
        {
            foreach (var client in clients)
            {
                client.Sites.Clear();
                var clientSites = allSites.Where(s => s.ClientId == client.Id).ToList();
                foreach (var site in clientSites)
                {
                    client.Sites.Add(site);
                }
            }
        }

        private static ObservableCollection<Site> FlattenSitesFromClients(ObservableCollection<Client> clients)
        {
            var allSites = new ObservableCollection<Site>();
            foreach (var client in clients)
            {
                foreach (var site in client.Sites)
                {
                    allSites.Add(site);
                }
            }
            return allSites;
        }
    }
}