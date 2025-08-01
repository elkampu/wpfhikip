using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using wpfhikip.Models;
using wpfhikip.Services;
using wpfhikip.ViewModels.Commands;
using wpfhikip.Protocols.Common;
using System.Windows.Media;

namespace wpfhikip.ViewModels
{
    public class SiteManagerViewModel : ViewModelBase
    {
        private readonly SiteDataService _dataService;

        // Collections
        private ObservableCollection<Client> _clients = new();
        private ObservableCollection<Site> _sites = new();
        private ObservableCollection<Camera> _siteDevices = new();

        // Selected items
        private Client? _selectedClient;
        private Site? _selectedSite;
        private Camera? _selectedDevice;

        // Search filters
        private string _clientSearchText = string.Empty;
        private string _siteSearchText = string.Empty;

        // UI state
        private bool _isLoading;
        private bool _isCheckingCompatibility;
        private CancellationTokenSource? _compatibilityCheckCancellation;

        #region Properties

        public ObservableCollection<Client> Clients
        {
            get => _clients;
            set => SetProperty(ref _clients, value);
        }

        public ObservableCollection<Site> Sites
        {
            get => _sites;
            set => SetProperty(ref _sites, value);
        }

        public ObservableCollection<Camera> SiteDevices
        {
            get => _siteDevices;
            set => SetProperty(ref _siteDevices, value);
        }

        public Client? SelectedClient
        {
            get => _selectedClient;
            set
            {
                if (SetProperty(ref _selectedClient, value))
                {
                    UpdateSitesForSelectedClient();
                    OnPropertyChanged(nameof(IsClientSelected));
                }
            }
        }

        public Site? SelectedSite
        {
            get => _selectedSite;
            set
            {
                if (SetProperty(ref _selectedSite, value))
                {
                    UpdateDevicesForSelectedSite();
                    OnPropertyChanged(nameof(IsSiteSelected));
                }
            }
        }

        public Camera? SelectedDevice
        {
            get => _selectedDevice;
            set => SetProperty(ref _selectedDevice, value);
        }

        public string ClientSearchText
        {
            get => _clientSearchText;
            set
            {
                if (SetProperty(ref _clientSearchText, value))
                {
                    FilterClients();
                }
            }
        }

        public string SiteSearchText
        {
            get => _siteSearchText;
            set
            {
                if (SetProperty(ref _siteSearchText, value))
                {
                    FilterSites();
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool IsCheckingCompatibility
        {
            get => _isCheckingCompatibility;
            set => SetProperty(ref _isCheckingCompatibility, value);
        }

        public bool IsClientSelected => SelectedClient != null;
        public bool IsSiteSelected => SelectedSite != null;

        /// <summary>
        /// Gets the available protocol options for device configuration
        /// </summary>
        public IEnumerable<CameraProtocol> ProtocolOptions { get; } = Enum.GetValues<CameraProtocol>();

        #endregion

        #region Commands

        // Client Commands
        public ICommand AddClientCommand { get; private set; }
        public ICommand EditClientCommand { get; private set; }
        public ICommand DeleteClientCommand { get; private set; }
        public ICommand ExportClientCommand { get; private set; }

        // Site Commands
        public ICommand AddSiteCommand { get; private set; }
        public ICommand EditSiteCommand { get; private set; }
        public ICommand DeleteSiteCommand { get; private set; }
        public ICommand ExportSiteCommand { get; private set; }

        // Device Commands
        public ICommand AddDeviceCommand { get; private set; }
        public ICommand DeleteSelectedDevicesCommand { get; private set; }
        public ICommand SelectAllDevicesCommand { get; private set; }
        public ICommand CheckCompatibilityCommand { get; private set; }
        public ICommand CancelCompatibilityCommand { get; private set; }

        // Data Commands
        public ICommand SaveDataCommand { get; private set; }
        public ICommand RefreshDataCommand { get; private set; }

        #endregion

        public SiteManagerViewModel()
        {
            _dataService = new SiteDataService();
            InitializeCommands();
            _ = LoadDataAsync(); // Load data asynchronously
        }

        private void InitializeCommands()
        {
            // Client Commands
            AddClientCommand = new RelayCommand(_ => AddClient());
            EditClientCommand = new RelayCommand(_ => EditClient(), _ => IsClientSelected);
            DeleteClientCommand = new RelayCommand(_ => DeleteClient(), _ => IsClientSelected);
            ExportClientCommand = new RelayCommand(async _ => await ExportClientAsync(), _ => IsClientSelected);

            // Site Commands
            AddSiteCommand = new RelayCommand(_ => AddSite(), _ => IsClientSelected);
            EditSiteCommand = new RelayCommand(_ => EditSite(), _ => IsSiteSelected);
            DeleteSiteCommand = new RelayCommand(_ => DeleteSite(), _ => IsSiteSelected);
            ExportSiteCommand = new RelayCommand(async _ => await ExportSiteAsync(), _ => IsSiteSelected);

            // Device Commands
            AddDeviceCommand = new RelayCommand(_ => AddDevice(), _ => IsSiteSelected);
            DeleteSelectedDevicesCommand = new RelayCommand(_ => DeleteSelectedDevices(), _ => CanDeleteSelectedDevices());
            SelectAllDevicesCommand = new RelayCommand(_ => SelectAllDevices(), _ => SiteDevices.Any());
            CheckCompatibilityCommand = new RelayCommand(async _ => await CheckCompatibilityAsync(), _ => CanCheckCompatibility());
            CancelCompatibilityCommand = new RelayCommand(_ => CancelCompatibilityCheck(), _ => CanCancelCompatibilityCheck());

            // Data Commands
            SaveDataCommand = new RelayCommand(async _ => await SaveDataAsync());
            RefreshDataCommand = new RelayCommand(async _ => await LoadDataAsync());
        }

        #region Data Management

        private async Task LoadDataAsync()
        {
            try
            {
                IsLoading = true;

                var clientsTask = _dataService.LoadClientsAsync();
                var sitesTask = _dataService.LoadSitesAsync();

                await Task.WhenAll(clientsTask, sitesTask);

                Clients = await clientsTask;
                var allSites = await sitesTask;

                // Associate sites with their clients
                foreach (var client in Clients)
                {
                    client.Sites.Clear();
                    var clientSites = allSites.Where(s => s.ClientId == client.Id).ToList();
                    foreach (var site in clientSites)
                    {
                        client.Sites.Add(site);
                    }
                }

                // Update filtered collections
                FilterClients();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task SaveDataAsync()
        {
            try
            {
                IsLoading = true;

                // Flatten all sites from all clients
                var allSites = new ObservableCollection<Site>();
                foreach (var client in Clients)
                {
                    foreach (var site in client.Sites)
                    {
                        allSites.Add(site);
                    }
                }

                var clientsTask = _dataService.SaveClientsAsync(Clients);
                var sitesTask = _dataService.SaveSitesAsync(allSites);

                await Task.WhenAll(clientsTask, sitesTask);

                MessageBox.Show("Data saved successfully!", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving data: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        #endregion

        #region Client Management

        private void AddClient()
        {
            var dialog = new Views.Dialogs.ClientDialog();
            dialog.Owner = Application.Current.MainWindow;

            if (dialog.ShowDialog() == true)
            {
                var newClient = dialog.ClientResult;
                if (newClient != null)
                {
                    Clients.Add(newClient);
                    SelectedClient = newClient;
                    FilterClients();
                }
            }
        }

        private void EditClient()
        {
            if (SelectedClient == null) return;

            var dialog = new Views.Dialogs.ClientDialog(SelectedClient);
            dialog.Owner = Application.Current.MainWindow;

            if (dialog.ShowDialog() == true)
            {
                var updatedClient = dialog.ClientResult;
                if (updatedClient != null)
                {
                    // Update the properties of the existing client
                    SelectedClient.Name = updatedClient.Name;
                    SelectedClient.ContactPerson = updatedClient.ContactPerson;
                    SelectedClient.Email = updatedClient.Email;
                    SelectedClient.Phone = updatedClient.Phone;
                    SelectedClient.Address = updatedClient.Address;
                    SelectedClient.Notes = updatedClient.Notes;
                    SelectedClient.LastModified = updatedClient.LastModified;

                    FilterClients();
                }
            }
        }

        private void DeleteClient()
        {
            if (SelectedClient == null) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete client '{SelectedClient.Name}' and all its sites?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                Clients.Remove(SelectedClient);
                SelectedClient = null;
                FilterClients();
            }
        }

        private async Task ExportClientAsync()
        {
            if (SelectedClient == null) return;

            var saveDialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = $"{SelectedClient.Name}_export.json"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    await _dataService.ExportClientAsync(SelectedClient, saveDialog.FileName);
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

        #endregion

        #region Site Management

        private void AddSite()
        {
            if (SelectedClient == null) return;

            var dialog = new Views.Dialogs.SiteDialog(null, SelectedClient.Id);
            dialog.Owner = Application.Current.MainWindow;

            if (dialog.ShowDialog() == true)
            {
                var newSite = dialog.SiteResult;
                if (newSite != null)
                {
                    SelectedClient.Sites.Add(newSite);
                    SelectedSite = newSite;
                    UpdateSitesForSelectedClient();
                }
            }
        }

        private void EditSite()
        {
            if (SelectedSite == null) return;

            var dialog = new Views.Dialogs.SiteDialog(SelectedSite);
            dialog.Owner = Application.Current.MainWindow;

            if (dialog.ShowDialog() == true)
            {
                var updatedSite = dialog.SiteResult;
                if (updatedSite != null)
                {
                    // Update the properties of the existing site
                    SelectedSite.Name = updatedSite.Name;
                    SelectedSite.Location = updatedSite.Location;
                    SelectedSite.Description = updatedSite.Description;
                    SelectedSite.NetworkRange = updatedSite.NetworkRange;
                    SelectedSite.VpnAccess = updatedSite.VpnAccess;
                    SelectedSite.Notes = updatedSite.Notes;
                    SelectedSite.LastModified = updatedSite.LastModified;

                    UpdateSitesForSelectedClient();
                }
            }
        }

        private void DeleteSite()
        {
            if (SelectedSite == null || SelectedClient == null) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete site '{SelectedSite.Name}' and all its devices?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                SelectedClient.Sites.Remove(SelectedSite);
                SelectedSite = null;
                UpdateSitesForSelectedClient();
            }
        }

        private async Task ExportSiteAsync()
        {
            if (SelectedSite == null) return;

            var saveDialog = new SaveFileDialog
            {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = $"{SelectedSite.Name}_export.json"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    await _dataService.ExportSiteAsync(SelectedSite, saveDialog.FileName);
                    MessageBox.Show("Site exported successfully!", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting site: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Device Management

        private void AddDevice()
        {
            if (SelectedSite == null) return;

            var newDevice = CreateNewCamera();
            SelectedSite.Devices.Add(newDevice);
            UpdateDevicesForSelectedSite();
        }

        private static Camera CreateNewCamera()
        {
            return new Camera
            {
                Protocol = CameraProtocol.Auto,
                Connection = new CameraConnection { Port = "80" },
                Settings = new CameraSettings(),
                VideoStream = new CameraVideoStream()
            };
        }

        private void DeleteSelectedDevices()
        {
            if (SelectedSite == null) return;

            var selectedDevices = SiteDevices.Where(d => d.IsSelected).ToList();
            if (selectedDevices.Count == 0) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete {selectedDevices.Count} selected devices?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                foreach (var device in selectedDevices)
                {
                    SelectedSite.Devices.Remove(device);
                }
                UpdateDevicesForSelectedSite();
            }
        }

        private void SelectAllDevices()
        {
            foreach (var device in SiteDevices)
            {
                device.IsSelected = true;
            }
        }

        private bool CanDeleteSelectedDevices() =>
            SelectedSite != null && SiteDevices.Any(d => d.IsSelected);

        #endregion

        #region Device Operations (Compatibility Check Only)

        private async Task CheckCompatibilityAsync()
        {
            var selectedDevices = GetSelectedDevicesWithIP();
            if (selectedDevices.Count == 0)
            {
                MessageBox.Show("Please select devices with IP addresses to check compatibility.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await RunCompatibilityCheck(selectedDevices);
        }

        private void CancelCompatibilityCheck()
        {
            _compatibilityCheckCancellation?.Cancel();
        }

        private List<Camera> GetSelectedDevicesWithIP()
        {
            return SiteDevices.Where(d => d.IsSelected && !string.IsNullOrEmpty(d.CurrentIP)).ToList();
        }

        private async Task RunCompatibilityCheck(List<Camera> selectedDevices)
        {
            _compatibilityCheckCancellation?.Cancel();
            _compatibilityCheckCancellation = new CancellationTokenSource();
            IsCheckingCompatibility = true;

            try
            {
                InitializeDevicesForCheck(selectedDevices);

                var tasks = selectedDevices.Select(device =>
                    CheckSingleDeviceCompatibilityAsync(device, _compatibilityCheckCancellation.Token));
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                HandleCancelledChecks(selectedDevices);
            }
            catch (Exception ex)
            {
                HandleCheckErrors(selectedDevices, ex);
            }
            finally
            {
                IsCheckingCompatibility = false;
                _compatibilityCheckCancellation?.Dispose();
                _compatibilityCheckCancellation = null;
            }
        }

        private static void InitializeDevicesForCheck(List<Camera> devices)
        {
            foreach (var device in devices)
            {
                device.ClearProtocolLogs();
                device.AddProtocolLog("System", "Check Started",
                    $"Initializing compatibility check for {device.CurrentIP}:{device.EffectivePort}");

                Application.Current.Dispatcher.Invoke(() =>
                {
                    device.Status = "Initializing check...";
                    device.CellColor = Brushes.LightYellow;
                });
            }
        }

        private async Task CheckSingleDeviceCompatibilityAsync(Camera device, CancellationToken cancellationToken)
        {
            try
            {
                // Use the same logic as NetConfViewModel for consistency
                var result = await ProtocolManager.CheckSingleProtocolAsync(device, device.Protocol, cancellationToken);
                SetFinalDeviceStatus(device, result);
            }
            catch (OperationCanceledException)
            {
                HandleCancelledDevice(device);
                throw;
            }
            catch (Exception ex)
            {
                HandleDeviceError(device, ex);
            }
        }

        private static void SetFinalDeviceStatus(Camera device, ProtocolCompatibilityResult result)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (result.IsCompatible)
                {
                    device.Status = $"{result.DetectedProtocol} compatible";
                    device.CellColor = Brushes.LightGreen;
                    device.IsCompatible = true;
                    device.Protocol = result.DetectedProtocol;
                }
                else
                {
                    device.Status = "Not compatible";
                    device.CellColor = Brushes.LightCoral;
                    device.IsCompatible = false;
                }
            });
        }

        private static void HandleCancelledChecks(List<Camera> devices)
        {
            foreach (var device in devices)
            {
                HandleCancelledDevice(device);
            }
        }

        private static void HandleCheckErrors(List<Camera> devices, Exception ex)
        {
            foreach (var device in devices)
            {
                HandleDeviceError(device, ex);
            }
        }

        private static void HandleCancelledDevice(Camera device)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                device.Status = "Check cancelled";
                device.CellColor = Brushes.LightGray;
            });
        }

        private static void HandleDeviceError(Camera device, Exception ex)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                device.Status = $"Error: {ex.Message}";
                device.CellColor = Brushes.LightCoral;
            });
        }

        private bool CanCheckCompatibility() =>
            !IsCheckingCompatibility && SiteDevices.Any(d => d.IsSelected && !string.IsNullOrEmpty(d.CurrentIP));

        private bool CanCancelCompatibilityCheck() =>
            IsCheckingCompatibility && _compatibilityCheckCancellation != null;

        #endregion

        #region Helper Methods

        private void UpdateSitesForSelectedClient()
        {
            if (SelectedClient == null)
            {
                Sites.Clear();
                return;
            }

            Sites = new ObservableCollection<Site>(SelectedClient.Sites);
            FilterSites();
        }

        private void UpdateDevicesForSelectedSite()
        {
            if (SelectedSite == null)
            {
                SiteDevices.Clear();
                return;
            }

            SiteDevices = new ObservableCollection<Camera>(SelectedSite.Devices);
        }

        private void FilterClients()
        {
            // This is a simplified filter - in a real implementation, 
            // you might want to use CollectionViewSource for better performance
            OnPropertyChanged(nameof(Clients));
        }

        private void FilterSites()
        {
            // This is a simplified filter - in a real implementation, 
            // you might want to use CollectionViewSource for better performance
            OnPropertyChanged(nameof(Sites));
        }

        #endregion
    }
}