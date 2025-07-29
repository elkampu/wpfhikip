using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;

using wpfhikip.Discovery.Core;
using wpfhikip.Discovery.Models;
using wpfhikip.Discovery.Protocols.Arp;
using wpfhikip.Discovery.Protocols.Icmp;
using wpfhikip.Discovery.Protocols.Mdns;
using wpfhikip.Discovery.Protocols.NetBios;
using wpfhikip.Discovery.Protocols.OnvifProbe;
using wpfhikip.Discovery.Protocols.PortScan;
using wpfhikip.Discovery.Protocols.Snmp;
using wpfhikip.Discovery.Protocols.Ssdp;
using wpfhikip.Discovery.Protocols.WsDiscovery;
using wpfhikip.ViewModels.Commands;

namespace wpfhikip.ViewModels
{
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
        private int _selectedTabIndex = 0;

        // Collections for filtered results
        private readonly ObservableCollection<DiscoveryResultsByMethod> _resultsByMethodFiltered = new();
        private readonly ObservableCollection<DiscoveredDeviceWithMethods> _resultsByDeviceFiltered = new();

        #region Properties

        /// <summary>
        /// Available discovery methods
        /// </summary>
        public ObservableCollection<DiscoveryMethodItem> DiscoveryMethods { get; } = new();

        /// <summary>
        /// Available network segments
        /// </summary>
        public ObservableCollection<NetworkSegment> NetworkSegments { get; } = new();

        /// <summary>
        /// Discovery results grouped by method
        /// </summary>
        public ObservableCollection<DiscoveryResultsByMethod> ResultsByMethod { get; } = new();

        /// <summary>
        /// Discovery results grouped by device
        /// </summary>
        public ObservableCollection<DiscoveredDeviceWithMethods> ResultsByDevice { get; } = new();

        /// <summary>
        /// Filtered results by method (excluding unknown methods)
        /// </summary>
        public ObservableCollection<DiscoveryResultsByMethod> ResultsByMethodFiltered => _resultsByMethodFiltered;

        /// <summary>
        /// Filtered results by device (no duplicates)
        /// </summary>
        public ObservableCollection<DiscoveredDeviceWithMethods> ResultsByDeviceFiltered => _resultsByDeviceFiltered;

        /// <summary>
        /// Whether scanning is currently in progress
        /// </summary>
        public bool IsScanning
        {
            get => _isScanning;
            set => SetProperty(ref _isScanning, value);
        }

        /// <summary>
        /// Overall scan progress (0-100)
        /// </summary>
        public double OverallProgress
        {
            get => _overallProgress;
            set => SetProperty(ref _overallProgress, value);
        }

        /// <summary>
        /// Current status message
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>
        /// Total number of devices found
        /// </summary>
        public int TotalDevicesFound
        {
            get => _totalDevicesFound;
            set => SetProperty(ref _totalDevicesFound, value);
        }

        /// <summary>
        /// Selected tab index (0 = By Method, 1 = By Device)
        /// </summary>
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => SetProperty(ref _selectedTabIndex, value);
        }

        /// <summary>
        /// Whether any discovery method is selected
        /// </summary>
        public bool HasSelectedMethods => DiscoveryMethods.Any(m => m.IsSelected);

        /// <summary>
        /// Whether any network segment is selected
        /// </summary>
        public bool HasSelectedNetworks => NetworkSegments.Any(n => n.IsSelected);

        /// <summary>
        /// Whether scan can be started
        /// </summary>
        public bool CanStartScan => !IsScanning && HasSelectedMethods;

        /// <summary>
        /// Whether there are any results to display
        /// </summary>
        public bool HasResults => TotalDevicesFound > 0;

        /// <summary>
        /// Number of active (running) methods
        /// </summary>
        public int ActiveMethodsCount => DiscoveryMethods.Count(m => m.IsRunning);

        /// <summary>
        /// Number of selected methods
        /// </summary>
        public int SelectedMethodsCount => DiscoveryMethods.Count(m => m.IsSelected);

        /// <summary>
        /// Number of selected network segments
        /// </summary>
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

        #endregion

        public NetworkDiscoveryViewModel()
        {
            // Initialize discovery manager and services
            _discoveryManager = new NetworkDiscoveryManager();
            _discoveryServices = InitializeDiscoveryServices();

            // Initialize commands using your existing RelayCommand
            StartScanCommand = new RelayCommand(_ => StartScanAsync(), _ => CanStartScan);
            StopScanCommand = new RelayCommand(_ => StopScan(), _ => IsScanning);
            ClearResultsCommand = new RelayCommand(_ => ClearResults(), _ => !IsScanning);
            SelectAllMethodsCommand = new RelayCommand(_ => SelectAllMethods());
            SelectNoMethodsCommand = new RelayCommand(_ => SelectNoMethods());
            SelectAllNetworksCommand = new RelayCommand(_ => SelectAllNetworks());
            SelectNoNetworksCommand = new RelayCommand(_ => SelectNoNetworks());
            ExportResultsCommand = new RelayCommand(_ => ExportResults(), _ => TotalDevicesFound > 0);
            RefreshNetworksCommand = new RelayCommand(_ => RefreshNetworkSegments());

            // Initialize data
            InitializeDiscoveryMethods();
            RefreshNetworkSegments();

            // Subscribe to property changes for command updates
            foreach (var method in DiscoveryMethods)
            {
                method.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(DiscoveryMethodItem.IsSelected))
                    {
                        OnPropertyChanged(nameof(HasSelectedMethods));
                        OnPropertyChanged(nameof(SelectedMethodsCount));
                    }
                    else if (e.PropertyName == nameof(DiscoveryMethodItem.IsRunning))
                    {
                        OnPropertyChanged(nameof(ActiveMethodsCount));
                    }
                };
            }

            foreach (var network in NetworkSegments)
            {
                network.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(NetworkSegment.IsSelected))
                    {
                        OnPropertyChanged(nameof(HasSelectedNetworks));
                        OnPropertyChanged(nameof(SelectedNetworksCount));
                    }
                };
            }
        }

        #region Private Methods

        private Dictionary<DiscoveryMethod, INetworkDiscoveryService> InitializeDiscoveryServices()
        {
            var services = new Dictionary<DiscoveryMethod, INetworkDiscoveryService>();

            // Add available services and subscribe to their events
            try
            {
                var ssdpService = new SsdpDiscoveryService();
                ssdpService.DeviceDiscovered += OnServiceDeviceDiscovered;
                ssdpService.ProgressChanged += OnServiceProgressChanged;
                services[DiscoveryMethod.SSDP] = ssdpService;
            }
            catch { /* Service not available */ }

            try
            {
                var portScanService = new PortScanService();
                portScanService.DeviceDiscovered += OnServiceDeviceDiscovered;
                portScanService.ProgressChanged += OnServiceProgressChanged;
                services[DiscoveryMethod.PortScan] = portScanService;
            }
            catch { /* Service not available */ }

            try
            {
                var wsDiscoveryService = new WsDiscoveryService();
                wsDiscoveryService.DeviceDiscovered += OnServiceDeviceDiscovered;
                wsDiscoveryService.ProgressChanged += OnServiceProgressChanged;
                services[DiscoveryMethod.WSDiscovery] = wsDiscoveryService;
            }
            catch { /* Service not available */ }

            try
            {
                var mdnsService = new MdnsDiscoveryService();
                mdnsService.DeviceDiscovered += OnServiceDeviceDiscovered;
                mdnsService.ProgressChanged += OnServiceProgressChanged;
                services[DiscoveryMethod.mDNS] = mdnsService;
            }
            catch { /* Service not available */ }

            try
            {
                var arpService = new ArpDiscoveryService();
                arpService.DeviceDiscovered += OnServiceDeviceDiscovered;
                arpService.ProgressChanged += OnServiceProgressChanged;
                services[DiscoveryMethod.ARP] = arpService;
            }
            catch { /* Service not available */ }

            try
            {
                var icmpService = new IcmpDiscoveryService();
                icmpService.DeviceDiscovered += OnServiceDeviceDiscovered;
                icmpService.ProgressChanged += OnServiceProgressChanged;
                services[DiscoveryMethod.ICMP] = icmpService;
            }
            catch { /* Service not available */ }

            // Add the three new discovery services
            try
            {
                var snmpService = new SnmpDiscoveryService();
                snmpService.DeviceDiscovered += OnServiceDeviceDiscovered;
                snmpService.ProgressChanged += OnServiceProgressChanged;
                services[DiscoveryMethod.SNMP] = snmpService;
            }
            catch { /* Service not available */ }

            try
            {
                var netbiosService = new NetBiosDiscoveryService();
                netbiosService.DeviceDiscovered += OnServiceDeviceDiscovered;
                netbiosService.ProgressChanged += OnServiceProgressChanged;
                services[DiscoveryMethod.NetBIOS] = netbiosService;
            }
            catch { /* Service not available */ }

            try
            {
                var onvifProbeService = new OnvifProbeDiscoveryService();
                onvifProbeService.DeviceDiscovered += OnServiceDeviceDiscovered;
                onvifProbeService.ProgressChanged += OnServiceProgressChanged;
                services[DiscoveryMethod.ONVIFProbe] = onvifProbeService;
            }
            catch { /* Service not available */ }

            return services;
        }

        private void InitializeDiscoveryMethods()
        {
            var availableMethods = new[]
            {
                DiscoveryMethod.SSDP,
                DiscoveryMethod.WSDiscovery,
                DiscoveryMethod.mDNS,
                DiscoveryMethod.ARP,
                DiscoveryMethod.ICMP,
                DiscoveryMethod.PortScan,
                DiscoveryMethod.SNMP,
                DiscoveryMethod.NetBIOS,
                DiscoveryMethod.ONVIFProbe
            };

            foreach (var method in availableMethods)
            {
                var item = new DiscoveryMethodItem
                {
                    Method = method,
                    IsEnabled = _discoveryServices.ContainsKey(method),
                    CategoryName = method.GetCategory()
                };

                // Select common methods by default
                if (method == DiscoveryMethod.SSDP || method == DiscoveryMethod.WSDiscovery || method == DiscoveryMethod.ARP)
                {
                    item.IsSelected = item.IsEnabled; // Only select if available
                }

                DiscoveryMethods.Add(item);
            }
        }

        private void RefreshNetworkSegments()
        {
            NetworkSegments.Clear();

            try
            {
                // Use your existing NetworkUtils to get detailed interface information
                var interfaces = NetworkUtils.GetLocalNetworkInterfaces();

                foreach (var kvp in interfaces)
                {
                    var interfaceId = kvp.Key;
                    var interfaceInfo = kvp.Value;

                    foreach (var address in interfaceInfo.IPv4Addresses)
                    {
                        var segment = new NetworkSegment
                        {
                            Network = $"{address.NetworkAddress}/{address.PrefixLength}",
                            Description = interfaceInfo.Description,
                            InterfaceId = interfaceId,
                            InterfaceName = interfaceInfo.Name,
                            InterfaceType = interfaceInfo.Type,
                            Speed = interfaceInfo.Speed,
                            MacAddress = interfaceInfo.MacAddress,
                            AddressInfo = address
                        };

                        NetworkSegments.Add(segment);
                    }
                }

                // If no interfaces found, add some common defaults
                if (!NetworkSegments.Any())
                {
                    var defaultSegments = new[]
                    {
                        "192.168.1.0/24",
                        "192.168.0.0/24",
                        "10.0.0.0/24"
                    };

                    foreach (var segment in defaultSegments)
                    {
                        NetworkSegments.Add(new NetworkSegment
                        {
                            Network = segment,
                            Description = "Default Network Range",
                            InterfaceName = "Default"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error refreshing network segments: {ex.Message}";
            }

            OnPropertyChanged(nameof(HasSelectedNetworks));
            OnPropertyChanged(nameof(SelectedNetworksCount));
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

                var selectedMethods = DiscoveryMethods.Where(m => m.IsSelected && m.IsEnabled).ToList();
                var selectedNetworks = NetworkSegments.Where(n => n.IsSelected).Select(n => n.Network).ToList();

                if (!selectedMethods.Any())
                {
                    MessageBox.Show("Please select at least one discovery method.", "No Methods Selected",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                StatusMessage = $"Starting scan with {selectedMethods.Count} methods...";

                var tasks = new List<Task>();
                var totalMethods = selectedMethods.Count;
                var completedMethods = 0;

                foreach (var methodItem in selectedMethods)
                {
                    if (_cancellationTokenSource.Token.IsCancellationRequested || _disposed) break;

                    methodItem.IsRunning = true;

                    if (_discoveryServices.TryGetValue(methodItem.Method, out var service))
                    {
                        var task = Task.Run(async () =>
                        {
                            try
                            {
                                if (methodItem.RequiresNetworkRange && selectedNetworks.Any())
                                {
                                    // Run on specific network segments
                                    foreach (var network in selectedNetworks)
                                    {
                                        if (_cancellationTokenSource.Token.IsCancellationRequested || _disposed) break;

                                        await service.DiscoverDevicesAsync(network, _cancellationTokenSource.Token);
                                    }
                                }
                                else
                                {
                                    // Run on all networks (for multicast methods like SSDP)
                                    await service.DiscoverDevicesAsync(_cancellationTokenSource.Token);
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                // Expected when cancelled
                            }
                            catch (Exception ex)
                            {
                                if (!_disposed)
                                {
                                    Application.Current.Dispatcher.BeginInvoke(() =>
                                    {
                                        StatusMessage = $"Error in {methodItem.Name}: {ex.Message}";
                                    });
                                }
                            }
                            finally
                            {
                                if (!_disposed)
                                {
                                    Application.Current.Dispatcher.BeginInvoke(() =>
                                    {
                                        methodItem.IsRunning = false;
                                        completedMethods++;
                                        OverallProgress = (double)completedMethods / totalMethods * 100;
                                    });
                                }
                            }
                        }, _cancellationTokenSource.Token);

                        tasks.Add(task);
                        _activeTasks.Add(task);
                    }
                    else
                    {
                        methodItem.IsRunning = false;
                        methodItem.Progress = "Service not available";
                    }
                }

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

                // Reset method states
                foreach (var method in DiscoveryMethods)
                {
                    method.IsRunning = false;
                }

                // Clean up completed tasks
                _activeTasks.Clear();
            }
        }

        private void StopScan()
        {
            if (!IsScanning) return;

            // Cancel immediately
            _cancellationTokenSource?.Cancel();
            StatusMessage = "Stopping scan...";

            // Set scanning to false immediately for UI responsiveness
            IsScanning = false;

            // Reset method states immediately
            foreach (var method in DiscoveryMethods)
            {
                method.IsRunning = false;
            }

            // Clean up tasks in background without blocking
            if (_activeTasks.Any())
            {
                Task.Run(async () =>
                {
                    try
                    {
                        // Much shorter timeout for quick cancellation
                        await Task.WhenAll(_activeTasks).WaitAsync(TimeSpan.FromMilliseconds(500));
                    }
                    catch
                    {
                        // Ignore cancellation exceptions
                    }
                    finally
                    {
                        // Clear completed tasks
                        try
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
                        catch
                        {
                            // Ignore dispatcher errors if app is shutting down
                        }
                    }
                });
            }
            else
            {
                StatusMessage = "Scan stopped";
            }
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

        private void SelectAllMethods()
        {
            foreach (var method in DiscoveryMethods.Where(m => m.IsEnabled))
            {
                method.IsSelected = true;
            }
        }

        private void SelectNoMethods()
        {
            foreach (var method in DiscoveryMethods)
            {
                method.IsSelected = false;
            }
        }

        private void SelectAllNetworks()
        {
            foreach (var network in NetworkSegments)
            {
                network.IsSelected = true;
            }
        }

        private void SelectNoNetworks()
        {
            foreach (var network in NetworkSegments)
            {
                network.IsSelected = false;
            }
        }

        private void ExportResults()
        {
            // TODO: Implement export functionality
            MessageBox.Show("Export functionality will be implemented here.", "Export",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Handles device discovered events from individual discovery services
        /// </summary>
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

        /// <summary>
        /// Handles progress changed events from individual discovery services
        /// </summary>
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
                    {
                        methodItem.Progress = e.Status;
                    }

                    if (!string.IsNullOrEmpty(e.Status))
                    {
                        StatusMessage = $"{e.DiscoveryMethod}: {e.Status}";
                    }
                }
            });
        }

        private void UpdateResultsByMethod(DiscoveredDevice device, string discoveryMethodName)
        {
            // Parse discovery method from string name
            var method = ParseDiscoveryMethod(discoveryMethodName);

            // Skip unknown methods
            if (method == DiscoveryMethod.Unknown)
                return;

            var methodGroup = ResultsByMethod.FirstOrDefault(r => r.Method == method);
            if (methodGroup == null)
            {
                methodGroup = new DiscoveryResultsByMethod { Method = method };
                ResultsByMethod.Add(methodGroup);
            }

            // Check if device already exists in this method group (by IP address)
            if (!methodGroup.Devices.Any(d => d.IPAddress?.ToString() == device.IPAddress?.ToString()))
            {
                methodGroup.Devices.Add(device);
            }
        }

        private void UpdateResultsByDevice(DiscoveredDevice device)
        {
            // Use IP address as the unique identifier for device grouping
            var deviceKey = device.IPAddress?.ToString() ?? device.UniqueId;
            var existingDevice = ResultsByDevice.FirstOrDefault(d =>
                d.Device.IPAddress?.ToString() == deviceKey || d.Device.UniqueId == deviceKey);

            if (existingDevice == null)
            {
                var deviceWithMethods = new DiscoveredDeviceWithMethods(device);
                ResultsByDevice.Add(deviceWithMethods);
            }
            else
            {
                // Update existing device with new discovery methods
                foreach (var method in device.DiscoveryMethods)
                {
                    if (!existingDevice.DiscoveryMethods.Contains(method))
                    {
                        existingDevice.DiscoveryMethods.Add(method);
                    }
                }

                // Update device information with the best available data
                existingDevice.Device.UpdateFrom(device);
            }
        }

        private void UpdateFilteredResults()
        {
            // Update filtered results by method (exclude unknown methods)
            _resultsByMethodFiltered.Clear();
            foreach (var result in ResultsByMethod.Where(r => r.Method != DiscoveryMethod.Unknown && r.DeviceCount > 0))
            {
                _resultsByMethodFiltered.Add(result);
            }

            // Update filtered results by device (remove duplicates based on IP)
            _resultsByDeviceFiltered.Clear();
            var uniqueDevices = new Dictionary<string, DiscoveredDeviceWithMethods>();

            foreach (var device in ResultsByDevice)
            {
                var key = device.Device.IPAddress?.ToString() ?? device.Device.UniqueId;

                if (!uniqueDevices.ContainsKey(key))
                {
                    // Create enhanced device with filtered method names
                    var enhancedDevice = new EnhancedDiscoveredDeviceWithMethods(device.Device);
                    foreach (var method in device.DiscoveryMethods.Where(m => m != DiscoveryMethod.Unknown))
                    {
                        enhancedDevice.DiscoveryMethods.Add(method);
                    }

                    uniqueDevices[key] = enhancedDevice;
                }
                else
                {
                    // Merge discovery methods
                    foreach (var method in device.DiscoveryMethods.Where(m => m != DiscoveryMethod.Unknown))
                    {
                        if (!uniqueDevices[key].DiscoveryMethods.Contains(method))
                        {
                            uniqueDevices[key].DiscoveryMethods.Add(method);
                        }
                    }

                    // Update device information
                    uniqueDevices[key].Device.UpdateFrom(device.Device);
                }
            }

            foreach (var device in uniqueDevices.Values.OrderBy(d => d.IPAddress))
            {
                _resultsByDeviceFiltered.Add(device);
            }
        }

        private DiscoveryMethod ParseDiscoveryMethod(string methodName)
        {
            if (string.IsNullOrEmpty(methodName))
                return DiscoveryMethod.Unknown;

            // Try to match against known method descriptions
            foreach (var method in Enum.GetValues<DiscoveryMethod>())
            {
                if (method.GetDescription().Equals(methodName, StringComparison.OrdinalIgnoreCase) ||
                    method.ToString().Equals(methodName, StringComparison.OrdinalIgnoreCase))
                {
                    return method;
                }
            }

            // Additional pattern matching for common variations
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
                // Cancel operations immediately
                _cancellationTokenSource?.Cancel();

                // Unsubscribe from events first to prevent callbacks
                foreach (var service in _discoveryServices.Values)
                {
                    try
                    {
                        service.DeviceDiscovered -= OnServiceDeviceDiscovered;
                        service.ProgressChanged -= OnServiceProgressChanged;
                    }
                    catch
                    {
                        // Ignore unsubscribe errors
                    }
                }

                // Dispose resources in background to not block
                Task.Run(() =>
                {
                    try
                    {
                        // Very short timeout for task completion
                        if (_activeTasks.Any())
                        {
                            var waitTask = Task.WhenAll(_activeTasks);
                            waitTask.Wait(1000); // Only wait 1 second maximum
                        }

                        // Dispose cancellation token source
                        _cancellationTokenSource?.Dispose();

                        // Dispose services
                        foreach (var service in _discoveryServices.Values)
                        {
                            try
                            {
                                if (service is IDisposable disposableService)
                                {
                                    disposableService.Dispose();
                                }
                            }
                            catch
                            {
                                // Ignore disposal errors
                            }
                        }

                        // Dispose discovery manager
                        _discoveryManager?.Dispose();

                        System.Diagnostics.Debug.WriteLine("NetworkDiscoveryViewModel disposed successfully");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error during NetworkDiscoveryViewModel disposal: {ex.Message}");
                    }
                });

                // Clear collections immediately for UI responsiveness
                try
                {
                    DiscoveryMethods.Clear();
                    NetworkSegments.Clear();
                    ResultsByMethod.Clear();
                    ResultsByDevice.Clear();
                    _resultsByMethodFiltered.Clear();
                    _resultsByDeviceFiltered.Clear();
                    _activeTasks.Clear();
                    _discoveryServices.Clear();
                }
                catch
                {
                    // Ignore clearing errors
                }
            }
            catch
            {
                // Ignore any disposal errors
            }
        }

        #endregion
    }

    /// <summary>
    /// Enhanced version that filters out unknown methods in the display string
    /// </summary>
    public class EnhancedDiscoveredDeviceWithMethods : DiscoveredDeviceWithMethods
    {
        public EnhancedDiscoveredDeviceWithMethods(DiscoveredDevice device) : base(device)
        {
        }

        /// <summary>
        /// Comma-separated list of discovery methods (excluding unknown methods)
        /// </summary>
        public string MethodsStringFiltered => string.Join(", ",
            DiscoveryMethods.Where(m => m != DiscoveryMethod.Unknown).Select(m => m.GetDescription()));
    }
}