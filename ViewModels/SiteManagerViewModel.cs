using System.Collections.ObjectModel;
using System.Windows.Input;
using wpfhikip.Models;
using wpfhikip.Services;
using wpfhikip.ViewModels.Commands;
using wpfhikip.ViewModels.Services;
using wpfhikip.Protocols.Common;

namespace wpfhikip.ViewModels
{
    public class SiteManagerViewModel : ViewModelBase
    {
        #region Services
        private readonly DataManagementService _dataManagementService;
        private readonly ClientManagementService _clientManagementService;
        private readonly SiteManagementService _siteManagementService;
        private readonly DeviceManagementService _deviceManagementService;
        private readonly CompatibilityCheckService _compatibilityCheckService;
        #endregion

        #region Collections
        private ObservableCollection<Client> _clients = new();
        private ObservableCollection<Site> _sites = new();
        private ObservableCollection<Camera> _siteDevices = new();
        #endregion

        #region Selected Items
        private Client? _selectedClient;
        private Site? _selectedSite;
        private Camera? _selectedDevice;
        #endregion

        #region Search Filters
        private string _clientSearchText = string.Empty;
        private string _siteSearchText = string.Empty;
        #endregion

        #region UI State
        private bool _isLoading;
        private bool _isCheckingCompatibility;
        #endregion

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
            // Initialize services directly in constructor for readonly fields
            var dataService = new SiteDataService();
            _dataManagementService = new DataManagementService(dataService);
            _clientManagementService = new ClientManagementService(dataService);
            _siteManagementService = new SiteManagementService(dataService);
            _deviceManagementService = new DeviceManagementService();
            _compatibilityCheckService = new CompatibilityCheckService();

            // Subscribe to compatibility check service events
            _compatibilityCheckService.IsCheckingCompatibilityChanged += OnCompatibilityCheckStateChanged;

            InitializeCommands();
            _ = LoadDataAsync(); // Load data asynchronously
        }

        #region Initialization

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
            DeleteSelectedDevicesCommand = new RelayCommand(_ => DeleteSelectedDevices(),
                _ => _deviceManagementService.CanDeleteSelectedDevices(SelectedSite, SiteDevices));
            SelectAllDevicesCommand = new RelayCommand(_ => SelectAllDevices(), _ => SiteDevices.Any());
            CheckCompatibilityCommand = new RelayCommand(async _ => await CheckCompatibilityAsync(),
                _ => _compatibilityCheckService.CanCheckCompatibility(SiteDevices));
            CancelCompatibilityCommand = new RelayCommand(_ => CancelCompatibilityCheck(),
                _ => _compatibilityCheckService.CanCancelCompatibilityCheck());

            // Data Commands
            SaveDataCommand = new RelayCommand(async _ => await SaveDataAsync());
            RefreshDataCommand = new RelayCommand(async _ => await LoadDataAsync());
        }

        #endregion

        #region Data Operations

        private async Task LoadDataAsync()
        {
            IsLoading = true;
            try
            {
                var (clients, success) = await _dataManagementService.LoadDataAsync();
                if (success)
                {
                    Clients = clients;
                    FilterClients();
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task SaveDataAsync()
        {
            IsLoading = true;
            try
            {
                await _dataManagementService.SaveDataAsync(Clients);
            }
            finally
            {
                IsLoading = false;
            }
        }

        #endregion

        #region Client Operations

        private void AddClient()
        {
            _clientManagementService.AddClient(Clients, client =>
            {
                SelectedClient = client;
                FilterClients();
            });
        }

        private void EditClient()
        {
            _clientManagementService.EditClient(SelectedClient, FilterClients);
        }

        private void DeleteClient()
        {
            _clientManagementService.DeleteClient(Clients, SelectedClient, _ =>
            {
                SelectedClient = null;
                FilterClients();
            });
        }

        private async Task ExportClientAsync()
        {
            await _clientManagementService.ExportClientAsync(SelectedClient);
        }

        #endregion

        #region Site Operations

        private void AddSite()
        {
            _siteManagementService.AddSite(SelectedClient, site =>
            {
                SelectedSite = site;
                UpdateSitesForSelectedClient();
            });
        }

        private void EditSite()
        {
            _siteManagementService.EditSite(SelectedSite, UpdateSitesForSelectedClient);
        }

        private void DeleteSite()
        {
            _siteManagementService.DeleteSite(SelectedClient, SelectedSite, _ =>
            {
                SelectedSite = null;
                UpdateSitesForSelectedClient();
            });
        }

        private async Task ExportSiteAsync()
        {
            await _siteManagementService.ExportSiteAsync(SelectedSite);
        }

        #endregion

        #region Device Operations

        private void AddDevice()
        {
            _deviceManagementService.AddDevice(SelectedSite, UpdateDevicesForSelectedSite);
        }

        private void DeleteSelectedDevices()
        {
            _deviceManagementService.DeleteSelectedDevices(SelectedSite, SiteDevices, UpdateDevicesForSelectedSite);
        }

        private void SelectAllDevices()
        {
            _deviceManagementService.SelectAllDevices(SiteDevices);
        }

        private async Task CheckCompatibilityAsync()
        {
            await _compatibilityCheckService.CheckCompatibilityAsync(SiteDevices);
        }

        private void CancelCompatibilityCheck()
        {
            _compatibilityCheckService.CancelCompatibilityCheck();
        }

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

        private void OnCompatibilityCheckStateChanged(bool isChecking)
        {
            IsCheckingCompatibility = isChecking;
        }

        #endregion
    }
}