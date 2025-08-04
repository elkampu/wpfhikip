using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

using wpfhikip.Discovery.Core;
using wpfhikip.Discovery.Models;
using wpfhikip.Discovery.Protocols.Arp;
using wpfhikip.Discovery.Protocols.Icmp;
using wpfhikip.Discovery.Protocols.Mdns;
using wpfhikip.Discovery.Protocols.OnvifProbe;
using wpfhikip.Discovery.Protocols.PortScan;
using wpfhikip.Discovery.Protocols.Ssdp;
using wpfhikip.Discovery.Protocols.WsDiscovery;
using wpfhikip.ViewModels.Commands;
using wpfhikip.Views.Dialogs;

namespace wpfhikip.ViewModels
{
    /// <summary>
    /// Discovery modes available for network scanning
    /// </summary>
    public enum DiscoveryMode
    {
        Default,
        Custom
    }

    /// <summary>
    /// ViewModel for the Network Discovery tool
    /// </summary>
    public class NetworkDiscoveryViewModel : ViewModelBase, IDisposable
    {
        private readonly NetworkDiscoveryManager _discoveryManager;
        private readonly Dictionary<DiscoveryMethod, INetworkDiscoveryService> _discoveryServices;
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly List<Task> _activeTasks = new();
        private bool _disposed = false;

        private bool _isScanning = false;
        private double _overallProgress = 0;
        private string _statusMessage = "Ready to scan";
        private int _totalDevicesFound = 0;
        private DiscoveryMode _selectedDiscoveryMode = DiscoveryMode.Default;

        // Collections for filtered results
        private readonly ObservableCollection<DiscoveryResultsByMethod> _resultsByMethodFiltered = new();
        private readonly ObservableCollection<DiscoveredDeviceWithMethods> _resultsByDeviceFiltered = new();

        #region Properties

        public ObservableCollection<DiscoveryMethodItem> DiscoveryMethods { get; } = new();
        public ObservableCollection<NetworkSegment> NetworkSegments { get; } = new();
        public ObservableCollection<DiscoveryResultsByMethod> ResultsByMethod { get; } = new();
        public ObservableCollection<DiscoveredDeviceWithMethods> ResultsByDevice { get; } = new();
        public ObservableCollection<DiscoveryResultsByMethod> ResultsByMethodFiltered => _resultsByMethodFiltered;
        public ObservableCollection<DiscoveredDeviceWithMethods> ResultsByDeviceFiltered => _resultsByDeviceFiltered;

        public bool IsScanning
        {
            get => _isScanning;
            set => SetProperty(ref _isScanning, value);
        }

        public double OverallProgress
        {
            get => _overallProgress;
            set => SetProperty(ref _overallProgress, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public int TotalDevicesFound
        {
            get => _totalDevicesFound;
            set => SetProperty(ref _totalDevicesFound, value);
        }

        public DiscoveryMode SelectedDiscoveryMode
        {
            get => _selectedDiscoveryMode;
            set
            {
                if (SetProperty(ref _selectedDiscoveryMode, value))
                {
                    ApplyDiscoveryMode();
                    OnPropertyChanged(nameof(IsDefaultMode));
                    OnPropertyChanged(nameof(IsCustomMode));
                    OnPropertyChanged(nameof(CanStartScan));
                }
            }
        }

        public bool IsDefaultMode => SelectedDiscoveryMode == DiscoveryMode.Default;
        public bool IsCustomMode => SelectedDiscoveryMode == DiscoveryMode.Custom;

        public bool HasSelectedMethods => IsDefaultMode || DiscoveryMethods.Any(m => m.IsSelected);
        public bool CanStartScan => !IsScanning && HasSelectedMethods;
        public bool HasResults => TotalDevicesFound > 0;
        public int ActiveMethodsCount => DiscoveryMethods.Count(m => m.IsRunning);
        public int SelectedMethodsCount => DiscoveryMethods.Count(m => m.IsSelected);
        public int SelectedNetworksCount => NetworkSegments.Count(n => n.IsSelected);

        #endregion

        #region Commands

        public ICommand StartScanCommand { get; }
        public ICommand StopScanCommand { get; }
        public ICommand ClearResultsCommand { get; }
        public ICommand SelectAllMethodsCommand { get; }
        public ICommand SelectNoMethodsCommand { get; }
        public ICommand SelectAllNetworksCommand { get; }
        public ICommand SelectNoNetworksCommand { get; }
        public ICommand ExportResultsCommand { get; }
        public ICommand RefreshNetworksCommand { get; }
        public ICommand ShowScanProgressCommand { get; }
        public ICommand SelectDefaultModeCommand { get; }
        public ICommand SelectCustomModeCommand { get; }

        #endregion

        public NetworkDiscoveryViewModel()
        {
            _discoveryManager = new NetworkDiscoveryManager();
            _discoveryServices = InitializeDiscoveryServices();

            // Initialize commands
            StartScanCommand = new RelayCommand(_ => StartScanAsync(), _ => CanStartScan);
            StopScanCommand = new RelayCommand(_ => StopScan(), _ => IsScanning);
            ClearResultsCommand = new RelayCommand(_ => ClearResults(), _ => !IsScanning);
            SelectAllMethodsCommand = new RelayCommand(_ => SelectAllMethods());
            SelectNoMethodsCommand = new RelayCommand(_ => SelectNoMethods());
            SelectAllNetworksCommand = new RelayCommand(_ => SelectAllNetworks());
            SelectNoNetworksCommand = new RelayCommand(_ => SelectNoNetworks());
            ExportResultsCommand = new RelayCommand(_ => ExportResults(), _ => TotalDevicesFound > 0);
            RefreshNetworksCommand = new RelayCommand(_ => RefreshNetworkSegments());
            ShowScanProgressCommand = new RelayCommand(_ => ShowScanProgress());
            SelectDefaultModeCommand = new RelayCommand(_ => SelectedDiscoveryMode = DiscoveryMode.Default);
            SelectCustomModeCommand = new RelayCommand(_ => SelectedDiscoveryMode = DiscoveryMode.Custom);

            InitializeDiscoveryMethods();
            RefreshNetworkSegments();
            SetupPropertyChangeSubscriptions();
            ApplyDiscoveryMode(); // Apply default mode settings
        }

        #region Private Methods

        private Dictionary<DiscoveryMethod, INetworkDiscoveryService> InitializeDiscoveryServices()
        {
            var services = new Dictionary<DiscoveryMethod, INetworkDiscoveryService>();
            var serviceTypes = new (DiscoveryMethod Method, Func<INetworkDiscoveryService> ServiceFactory)[]
            {
                (DiscoveryMethod.SSDP, () => new SsdpDiscoveryService()),
                (DiscoveryMethod.PortScan, () => new PortScanService()),
                (DiscoveryMethod.WSDiscovery, () => new WsDiscoveryService()),
                (DiscoveryMethod.mDNS, () => new MdnsDiscoveryService()),
                (DiscoveryMethod.ARP, () => new ArpDiscoveryService()),
                (DiscoveryMethod.ICMP, () => new IcmpDiscoveryService()),
                (DiscoveryMethod.ONVIFProbe, () => new OnvifProbeDiscoveryService())
            };

            foreach (var (method, serviceFactory) in serviceTypes)
            {
                try
                {
                    var service = serviceFactory();
                    service.DeviceDiscovered += OnServiceDeviceDiscovered;
                    service.ProgressChanged += OnServiceProgressChanged;
                    services[method] = service;
                }
                catch { /* Service not available */ }
            }

            return services;
        }

        private void InitializeDiscoveryMethods()
        {
            var defaultSelected = new DiscoveryMethod[] { DiscoveryMethod.SSDP, DiscoveryMethod.WSDiscovery, DiscoveryMethod.ARP };
            var availableMethods = new DiscoveryMethod[]
            {
                DiscoveryMethod.SSDP, DiscoveryMethod.WSDiscovery, DiscoveryMethod.mDNS,
                DiscoveryMethod.ARP, DiscoveryMethod.ICMP, DiscoveryMethod.PortScan,
                DiscoveryMethod.SNMP, DiscoveryMethod.NetBIOS, DiscoveryMethod.ONVIFProbe
            };

            foreach (var method in availableMethods)
            {
                var item = new DiscoveryMethodItem
                {
                    Method = method,
                    IsEnabled = _discoveryServices.ContainsKey(method),
                    CategoryName = method.GetCategory(),
                    IsSelected = defaultSelected.Contains(method) && _discoveryServices.ContainsKey(method)
                };
                DiscoveryMethods.Add(item);
            }
        }

        private void RefreshNetworkSegments()
        {
            NetworkSegments.Clear();

            try
            {
                var interfaces = NetworkUtils.GetLocalNetworkInterfaces();
                foreach (var kvp in interfaces)
                {
                    foreach (var address in kvp.Value.IPv4Addresses)
                    {
                        var segment = new NetworkSegment
                        {
                            Network = $"{address.NetworkAddress}/{address.PrefixLength}",
                            Description = kvp.Value.Description,
                            InterfaceId = kvp.Key,
                            InterfaceName = kvp.Value.Name,
                            InterfaceType = kvp.Value.Type,
                            Speed = kvp.Value.Speed,
                            MacAddress = kvp.Value.MacAddress,
                            AddressInfo = address,
                            IsSelected = true // Default mode: select all networks
                        };
                        NetworkSegments.Add(segment);
                    }
                }

                // Add defaults if no interfaces found
                if (!NetworkSegments.Any())
                {
                    var defaults = new string[] { "192.168.1.0/24", "192.168.0.0/24", "10.0.0.0/24" };
                    foreach (var segment in defaults)
                    {
                        NetworkSegments.Add(new NetworkSegment
                        {
                            Network = segment,
                            Description = "Default Network Range",
                            InterfaceName = "Default",
                            IsSelected = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error refreshing network segments: {ex.Message}";
            }

            OnPropertyChanged(nameof(SelectedNetworksCount));
        }

        private void ApplyDiscoveryMode()
        {
            if (IsDefaultMode)
            {
                // Default mode: Use predefined methods for comprehensive discovery
                var defaultMethods = new DiscoveryMethod[]
                {
                    DiscoveryMethod.SSDP,
                    DiscoveryMethod.WSDiscovery,
                    DiscoveryMethod.ARP,
                    DiscoveryMethod.ICMP,
                    DiscoveryMethod.mDNS
                };

                foreach (var method in DiscoveryMethods)
                {
                    method.IsSelected = method.IsEnabled && defaultMethods.Contains(method.Method);
                }

                // Select all available network segments
                foreach (var network in NetworkSegments)
                {
                    network.IsSelected = true;
                }
            }
            // Custom mode: User controls selections (no automatic changes)

            OnPropertyChanged(nameof(HasSelectedMethods));
            OnPropertyChanged(nameof(SelectedMethodsCount));
            OnPropertyChanged(nameof(SelectedNetworksCount));
        }

        private void SetupPropertyChangeSubscriptions()
        {
            foreach (var method in DiscoveryMethods)
            {
                method.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(DiscoveryMethodItem.IsSelected))
                        OnPropertyChanged(nameof(HasSelectedMethods));
                    else if (e.PropertyName == nameof(DiscoveryMethodItem.IsRunning))
                        OnPropertyChanged(nameof(ActiveMethodsCount));
                };
            }

            foreach (var network in NetworkSegments)
            {
                network.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(NetworkSegment.IsSelected))
                        OnPropertyChanged(nameof(SelectedNetworksCount));
                };
            }
        }

        private async void StartScanAsync()
        {
            if (IsScanning || _disposed) return;

            try
            {
                IsScanning = true;
                OverallProgress = 0;
                StatusMessage = "Initializing scan...";
                _cancellationTokenSource = new CancellationTokenSource();
                ClearResults();

                var selectedMethods = IsDefaultMode
                    ? DiscoveryMethods.Where(m => m.IsSelected && m.IsEnabled).ToList()
                    : DiscoveryMethods.Where(m => m.IsSelected && m.IsEnabled).ToList();

                var selectedNetworks = NetworkSegments.Where(n => n.IsSelected).Select(n => n.Network).ToList();

                if (!selectedMethods.Any())
                {
                    MessageBox.Show("Please select at least one discovery method.", "No Methods Selected",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                StatusMessage = $"Starting {(IsDefaultMode ? "default" : "custom")} scan with {selectedMethods.Count} methods...";
                var tasks = await StartDiscoveryTasks(selectedMethods, selectedNetworks);
                await Task.WhenAll(tasks);

                if (!_cancellationTokenSource.Token.IsCancellationRequested && !_disposed)
                {
                    StatusMessage = $"Scan completed. Found {TotalDevicesFound} devices.";
                    OverallProgress = 100;
                }
            }
            catch (Exception ex)
            {
                if (!_disposed)
                {
                    MessageBox.Show($"Scan error: {ex.Message}", "Scan Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusMessage = $"Scan failed: {ex.Message}";
                }
            }
            finally
            {
                IsScanning = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
                ResetMethodStates();
                _activeTasks.Clear();
            }
        }

        private async Task<List<Task>> StartDiscoveryTasks(List<DiscoveryMethodItem> selectedMethods, List<string> selectedNetworks)
        {
            var tasks = new List<Task>();
            var totalMethods = selectedMethods.Count;
            var completedMethods = 0;

            foreach (var methodItem in selectedMethods)
            {
                if (_cancellationTokenSource?.Token.IsCancellationRequested == true || _disposed) break;

                methodItem.IsRunning = true;

                if (_discoveryServices.TryGetValue(methodItem.Method, out var service))
                {
                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            if (methodItem.RequiresNetworkRange && selectedNetworks.Any())
                            {
                                foreach (var network in selectedNetworks)
                                {
                                    if (_cancellationTokenSource?.Token.IsCancellationRequested == true || _disposed) break;
                                    await service.DiscoverDevicesAsync(network, _cancellationTokenSource.Token);
                                }
                            }
                            else
                            {
                                await service.DiscoverDevicesAsync(_cancellationTokenSource?.Token ?? CancellationToken.None);
                            }
                        }
                        catch (OperationCanceledException) { }
                        catch (Exception ex)
                        {
                            if (!_disposed)
                            {
                                Application.Current.Dispatcher.BeginInvoke(() =>
                                    StatusMessage = $"Error in {methodItem.Name}: {ex.Message}");
                            }
                        }
                        finally
                        {
                            if (!_disposed)
                            {
                                Application.Current.Dispatcher.BeginInvoke(() =>
                                {
                                    methodItem.IsRunning = false;
                                    Interlocked.Increment(ref completedMethods);
                                    OverallProgress = (double)completedMethods / totalMethods * 100;
                                });
                            }
                        }
                    }, _cancellationTokenSource?.Token ?? CancellationToken.None);

                    tasks.Add(task);
                    _activeTasks.Add(task);
                }
                else
                {
                    methodItem.IsRunning = false;
                    methodItem.Progress = "Service not available";
                }
            }

            return tasks;
        }

        private void StopScan()
        {
            if (!IsScanning) return;

            _cancellationTokenSource?.Cancel();
            StatusMessage = "Stopping scan...";
            IsScanning = false;
            ResetMethodStates();

            if (_activeTasks.Any())
            {
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.WhenAll(_activeTasks).WaitAsync(TimeSpan.FromMilliseconds(500));
                    }
                    catch { }
                    finally
                    {
                        Application.Current?.Dispatcher?.BeginInvoke(() =>
                        {
                            if (!_disposed)
                            {
                                _activeTasks.Clear();
                                StatusMessage = "Scan stopped";
                            }
                        });
                    }
                });
            }
            else
            {
                StatusMessage = "Scan stopped";
            }
        }

        private void ResetMethodStates()
        {
            foreach (var method in DiscoveryMethods)
                method.IsRunning = false;
        }

        private void ClearResults()
        {
            ResultsByMethod.Clear();
            ResultsByDevice.Clear();
            _resultsByMethodFiltered.Clear();
            _resultsByDeviceFiltered.Clear();
            TotalDevicesFound = 0;
            _discoveryManager.ClearDiscoveredDevices();
            OnPropertyChanged(nameof(HasResults));
        }

        private void SelectAllMethods() => SetMethodSelection(true);
        private void SelectNoMethods() => SetMethodSelection(false);
        private void SelectAllNetworks() => SetNetworkSelection(true);
        private void SelectNoNetworks() => SetNetworkSelection(false);

        private void SetMethodSelection(bool selected)
        {
            if (IsCustomMode) // Only allow manual selection in custom mode
            {
                foreach (var method in DiscoveryMethods.Where(m => m.IsEnabled))
                    method.IsSelected = selected;
            }
        }

        private void SetNetworkSelection(bool selected)
        {
            if (IsCustomMode) // Only allow manual selection in custom mode
            {
                foreach (var network in NetworkSegments)
                    network.IsSelected = selected;
            }
        }

        private void ExportResults()
        {
            MessageBox.Show("Export functionality will be implemented here.", "Export",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ShowScanProgress()
        {
            var progressWindow = new ScanProgressDetailsWindow(this);
            progressWindow.Show();
        }

        private void OnServiceDeviceDiscovered(object? sender, DeviceDiscoveredEventArgs e)
        {
            if (_disposed) return;

            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (!_disposed)
                {
                    UpdateResultsByMethod(e.Device, e.DiscoveryMethod);
                    UpdateResultsByDevice(e.Device);
                    UpdateFilteredResults();
                    TotalDevicesFound = _resultsByDeviceFiltered.Count;
                    OnPropertyChanged(nameof(HasResults));
                }
            });
        }

        private void OnServiceProgressChanged(object? sender, DiscoveryProgressEventArgs e)
        {
            if (_disposed) return;

            Application.Current.Dispatcher.BeginInvoke(() =>
            {
                if (!_disposed)
                {
                    var methodItem = DiscoveryMethods.FirstOrDefault(m =>
                        m.Name.Equals(e.DiscoveryMethod, StringComparison.OrdinalIgnoreCase));

                    if (methodItem != null)
                        methodItem.Progress = e.Status;

                    if (!string.IsNullOrEmpty(e.Status))
                        StatusMessage = $"{e.DiscoveryMethod}: {e.Status}";
                }
            });
        }

        private void UpdateResultsByMethod(DiscoveredDevice device, string discoveryMethodName)
        {
            var method = ParseDiscoveryMethod(discoveryMethodName);
            if (method == DiscoveryMethod.Unknown) return;

            var methodGroup = ResultsByMethod.FirstOrDefault(r => r.Method == method);
            if (methodGroup == null)
            {
                methodGroup = new DiscoveryResultsByMethod { Method = method };
                ResultsByMethod.Add(methodGroup);
            }

            if (!methodGroup.Devices.Any(d => d.IPAddress?.ToString() == device.IPAddress?.ToString()))
                methodGroup.Devices.Add(device);
        }

        private void UpdateResultsByDevice(DiscoveredDevice device)
        {
            var deviceKey = device.IPAddress?.ToString() ?? device.UniqueId;
            var existingDevice = ResultsByDevice.FirstOrDefault(d =>
                d.Device.IPAddress?.ToString() == deviceKey || d.Device.UniqueId == deviceKey);

            if (existingDevice == null)
            {
                ResultsByDevice.Add(new DiscoveredDeviceWithMethods(device));
            }
            else
            {
                foreach (var method in device.DiscoveryMethods)
                {
                    if (!existingDevice.DiscoveryMethods.Contains(method))
                        existingDevice.DiscoveryMethods.Add(method);
                }
                existingDevice.Device.UpdateFrom(device);
            }
        }

        private void UpdateFilteredResults()
        {
            _resultsByMethodFiltered.Clear();
            foreach (var result in ResultsByMethod.Where(r => r.Method != DiscoveryMethod.Unknown && r.DeviceCount > 0))
                _resultsByMethodFiltered.Add(result);

            _resultsByDeviceFiltered.Clear();
            var uniqueDevices = new Dictionary<string, DiscoveredDeviceWithMethods>();

            foreach (var device in ResultsByDevice)
            {
                var key = device.Device.IPAddress?.ToString() ?? device.Device.UniqueId;

                if (!uniqueDevices.ContainsKey(key))
                {
                    var enhancedDevice = new EnhancedDiscoveredDeviceWithMethods(device.Device);
                    foreach (var method in device.DiscoveryMethods.Where(m => m != DiscoveryMethod.Unknown))
                        enhancedDevice.DiscoveryMethods.Add(method);
                    uniqueDevices[key] = enhancedDevice;
                }
                else
                {
                    foreach (var method in device.DiscoveryMethods.Where(m => m != DiscoveryMethod.Unknown))
                    {
                        if (!uniqueDevices[key].DiscoveryMethods.Contains(method))
                            uniqueDevices[key].DiscoveryMethods.Add(method);
                    }
                    uniqueDevices[key].Device.UpdateFrom(device.Device);
                }
            }

            foreach (var device in uniqueDevices.Values.OrderBy(d => d.IPAddress))
                _resultsByDeviceFiltered.Add(device);
        }

        private DiscoveryMethod ParseDiscoveryMethod(string methodName)
        {
            if (string.IsNullOrEmpty(methodName)) return DiscoveryMethod.Unknown;

            foreach (var method in Enum.GetValues<DiscoveryMethod>())
            {
                if (method.GetDescription().Equals(methodName, StringComparison.OrdinalIgnoreCase) ||
                    method.ToString().Equals(methodName, StringComparison.OrdinalIgnoreCase))
                    return method;
            }

            return methodName.ToLowerInvariant() switch
            {
                var name when name.Contains("ssdp") || name.Contains("upnp") => DiscoveryMethod.SSDP,
                var name when name.Contains("ws-discovery") || name.Contains("wsd") => DiscoveryMethod.WSDiscovery,
                var name when name.Contains("mdns") || name.Contains("bonjour") => DiscoveryMethod.mDNS,
                var name when name.Contains("arp") => DiscoveryMethod.ARP,
                var name when name.Contains("icmp") || name.Contains("ping") => DiscoveryMethod.ICMP,
                var name when name.Contains("port") || name.Contains("scan") => DiscoveryMethod.PortScan,
                var name when name.Contains("snmp") => DiscoveryMethod.SNMP,
                var name when name.Contains("netbios") => DiscoveryMethod.NetBIOS,
                var name when name.Contains("onvif") => DiscoveryMethod.ONVIFProbe,
                _ => DiscoveryMethod.Unknown
            };
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                _cancellationTokenSource?.Cancel();

                foreach (var service in _discoveryServices.Values)
                {
                    try
                    {
                        service.DeviceDiscovered -= OnServiceDeviceDiscovered;
                        service.ProgressChanged -= OnServiceProgressChanged;
                    }
                    catch { }
                }

                Task.Run(() =>
                {
                    try
                    {
                        if (_activeTasks.Any())
                            Task.WhenAll(_activeTasks).Wait(1000);

                        _cancellationTokenSource?.Dispose();

                        foreach (var service in _discoveryServices.Values)
                        {
                            try
                            {
                                if (service is IDisposable disposableService)
                                    disposableService.Dispose();
                            }
                            catch { }
                        }

                        _discoveryManager?.Dispose();
                    }
                    catch { }
                });

                // Clear collections immediately
                DiscoveryMethods.Clear();
                NetworkSegments.Clear();
                ResultsByMethod.Clear();
                ResultsByDevice.Clear();
                _resultsByMethodFiltered.Clear();
                _resultsByDeviceFiltered.Clear();
                _activeTasks.Clear();
                _discoveryServices.Clear();
            }
            catch { }
        }

        #endregion
    }

    public class EnhancedDiscoveredDeviceWithMethods : DiscoveredDeviceWithMethods
    {
        public EnhancedDiscoveredDeviceWithMethods(DiscoveredDevice device) : base(device) { }

        public string MethodsStringFiltered => string.Join(", ",
            DiscoveryMethods.Where(m => m != DiscoveryMethod.Unknown).Select(m => m.GetDescription()));
    }
}