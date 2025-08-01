using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

using wpfhikip.Models;

namespace wpfhikip.Services
{
    /// <summary>
    /// Service for managing site and client data persistence
    /// </summary>
    public class SiteDataService
    {
        private const string DataFolder = "SiteManagerData";
        private const string ClientsFileName = "clients.json";
        private const string SitesFileName = "sites.json";

        private readonly string _dataFolderPath;
        private readonly string _clientsFilePath;
        private readonly string _sitesFilePath;

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            MaxDepth = 64,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        public SiteDataService()
        {
            _dataFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "wpfhikip", DataFolder);
            _clientsFilePath = Path.Combine(_dataFolderPath, ClientsFileName);
            _sitesFilePath = Path.Combine(_dataFolderPath, SitesFileName);

            EnsureDataFolderExists();
        }

        private void EnsureDataFolderExists()
        {
            if (!Directory.Exists(_dataFolderPath))
            {
                Directory.CreateDirectory(_dataFolderPath);
            }
        }

        /// <summary>
        /// Loads all clients from storage
        /// </summary>
        public async Task<ObservableCollection<Client>> LoadClientsAsync()
        {
            try
            {
                if (!File.Exists(_clientsFilePath))
                    return new ObservableCollection<Client>();

                var json = await File.ReadAllTextAsync(_clientsFilePath);
                var clients = JsonSerializer.Deserialize<List<Client>>(json, _jsonOptions) ?? new List<Client>();
                return new ObservableCollection<Client>(clients);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load clients: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Saves all clients to storage
        /// </summary>
        public async Task SaveClientsAsync(ObservableCollection<Client> clients)
        {
            try
            {
                var json = JsonSerializer.Serialize(clients.ToList(), _jsonOptions);
                await File.WriteAllTextAsync(_clientsFilePath, json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save clients: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Loads all sites from storage
        /// </summary>
        public async Task<ObservableCollection<Site>> LoadSitesAsync()
        {
            try
            {
                if (!File.Exists(_sitesFilePath))
                    return new ObservableCollection<Site>();

                var json = await File.ReadAllTextAsync(_sitesFilePath);
                var sites = JsonSerializer.Deserialize<List<Site>>(json, _jsonOptions) ?? new List<Site>();
                return new ObservableCollection<Site>(sites);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to load sites: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Saves all sites to storage
        /// </summary>
        public async Task SaveSitesAsync(ObservableCollection<Site> sites)
        {
            try
            {
                var json = JsonSerializer.Serialize(sites.ToList(), _jsonOptions);
                await File.WriteAllTextAsync(_sitesFilePath, json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save sites: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Exports site data to a specified file
        /// </summary>
        public async Task ExportSiteAsync(Site site, string filePath)
        {
            try
            {
                var exportData = new
                {
                    Site = site,
                    ExportedAt = DateTime.Now,
                    Version = "1.0"
                };

                var json = JsonSerializer.Serialize(exportData, _jsonOptions);
                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to export site: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Exports client data with all sites to a specified file
        /// </summary>
        public async Task ExportClientAsync(Client client, string filePath)
        {
            try
            {
                var exportData = new
                {
                    Client = client,
                    ExportedAt = DateTime.Now,
                    Version = "1.0"
                };

                var json = JsonSerializer.Serialize(exportData, _jsonOptions);
                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to export client: {ex.Message}", ex);
            }
        }
    }
}