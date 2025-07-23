using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using wpfhikip.Models;
using wpfhikip.Protocols.Axis;
using wpfhikip.Protocols.Dahua;
using wpfhikip.Protocols.Hikvision;
using wpfhikip.Protocols.Onvif;
using wpfhikip.ViewModels.Commands;

namespace wpfhikip.ViewModels
{
    public class NetConfViewModel : ViewModelBase
    {
        private ObservableCollection<Camera> _cameras;
        private ObservableCollection<string> _protocolOptions;
        private bool _isCheckingCompatibility;
        private CancellationTokenSource _compatibilityCheckCancellation;

        public ObservableCollection<Camera> Cameras
        {
            get => _cameras;
            set => SetProperty(ref _cameras, value);
        }

        public ObservableCollection<string> ProtocolOptions
        {
            get => _protocolOptions;
            set => SetProperty(ref _protocolOptions, value);
        }

        public bool IsCheckingCompatibility
        {
            get => _isCheckingCompatibility;
            set => SetProperty(ref _isCheckingCompatibility, value);
        }

        // Commands
        public ICommand AddCameraCommand { get; set; }
        public ICommand DeleteSelectedCommand { get; set; }
        public ICommand SelectAllCommand { get; set; }
        public ICommand CheckCompatibilityCommand { get; set; }
        public ICommand SendNetworkConfigCommand { get; set; }
        public ICommand SendNTPConfigCommand { get; set; }
        public ICommand SaveConfigCommand { get; set; }
        public ICommand LoadConfigCommand { get; set; }

        public NetConfViewModel()
        {
            InitializeData();
            InitializeCommands();
        }

        private void InitializeData()
        {
            Cameras = new ObservableCollection<Camera> { CreateNewCamera() };
            ProtocolOptions = new ObservableCollection<string>
        {
            "Auto",
            "Dahua",
            "Hikvision",
            "Axis",
            "Onvif",
            "Bosch",
            "Hanwha"
        };
        }

        private Camera CreateNewCamera()
        {
            return new Camera
            {
                Protocol = CameraProtocol.Auto, // Set Auto as default
                Connection = new CameraConnection()
                {
                    Port = "80", // Default port
                },
                Settings = new CameraSettings(),
                VideoStream = new CameraVideoStream()
            };
        }

        private void InitializeCommands()
        {
            AddCameraCommand = new RelayCommand(AddCamera);
            DeleteSelectedCommand = new RelayCommand(DeleteSelected, CanDeleteSelected);
            SelectAllCommand = new RelayCommand(SelectAll);
            CheckCompatibilityCommand = new RelayCommand(async param => await CheckCompatibilityAsync(), CanCheckCompatibility);
            SendNetworkConfigCommand = new RelayCommand(async param => await SendNetworkConfigAsync(), CanSendNetworkConfig);
            SendNTPConfigCommand = new RelayCommand(async param => await SendNTPConfigAsync(), CanSendNTPConfig);
            SaveConfigCommand = new RelayCommand(SaveConfig);
            LoadConfigCommand = new RelayCommand(LoadConfig);
        }

        public void AddCamera(object parameter)
        {
            Cameras.Add(CreateNewCamera());
        }

        private void DeleteSelected(object parameter)
        {
            var selectedItems = Cameras.Where(c => c.IsSelected).ToList();
            foreach (var item in selectedItems)
            {
                Cameras.Remove(item);
            }
        }

        private bool CanDeleteSelected(object parameter)
        {
            return Cameras?.Any(c => c.IsSelected) == true;
        }

        private void SelectAll(object parameter)
        {
            foreach (var camera in Cameras)
            {
                camera.IsSelected = true;
            }
        }

        private async Task CheckCompatibilityAsync()
        {
            var selectedCameras = Cameras.Where(c => c.IsSelected && !string.IsNullOrEmpty(c.CurrentIP)).ToList();
            if (!selectedCameras.Any())
            {
                MessageBox.Show("Please select cameras with IP addresses to check compatibility.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Cancel any existing compatibility check
            _compatibilityCheckCancellation?.Cancel();
            _compatibilityCheckCancellation = new CancellationTokenSource();

            IsCheckingCompatibility = true;

            try
            {
                // Initialize all selected cameras for checking
                foreach (var camera in selectedCameras)
                {
                    camera.ClearProtocolLogs();
                    camera.AddProtocolLog("System", "Check Started", $"Initializing compatibility check for {camera.CurrentIP}:{camera.EffectivePort}");

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        camera.Status = "Initializing check...";
                        camera.CellColor = Brushes.LightYellow;
                    });
                }

                // Run compatibility checks in parallel
                var tasks = selectedCameras.Select(camera => CheckSingleCameraCompatibilityAsync(camera, _compatibilityCheckCancellation.Token));
                await Task.WhenAll(tasks);

                // No group evaluation - each camera keeps its individual result
            }
            catch (OperationCanceledException)
            {
                foreach (var camera in selectedCameras)
                {
                    camera.AddProtocolLog("System", "Check Cancelled", "Compatibility check was cancelled by user or system");

                    // Only set to cancelled/grey if the camera hasn't already been processed successfully
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        // Don't override cameras that already have a final status (Orange, Green, Red)
                        if (camera.CellColor == Brushes.LightYellow || camera.CellColor == null ||
                            camera.Status?.Contains("Testing") == true || camera.Status?.Contains("Checking") == true)
                        {
                            camera.Status = "Check cancelled";
                            camera.CellColor = Brushes.LightGray;
                        }
                        else
                        {
                            // Camera already has a final status - don't change it
                            camera.AddProtocolLog("System", "Check Cancelled", $"Check cancelled but keeping existing status: {camera.Status}", Models.ProtocolLogLevel.Info);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                foreach (var camera in selectedCameras)
                {
                    camera.AddProtocolLog("System", "Check Error", $"Unexpected error during compatibility check: {ex.Message}", Models.ProtocolLogLevel.Error);
                }
            }
            finally
            {
                IsCheckingCompatibility = false;
                _compatibilityCheckCancellation?.Dispose();
                _compatibilityCheckCancellation = null;
            }
        }
        private async Task<bool> PingCameraAsync(string ipAddress, int timeoutMs = 3000)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ipAddress, timeoutMs);
                return reply.Status == IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }

        private async Task CheckSingleCameraCompatibilityAsync(Camera camera, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                camera.ClearProtocolLogs();
                camera.AddProtocolLog("System", "Starting Protocol Check", $"Beginning compatibility check for {camera.CurrentIP}:{camera.EffectivePort}");

                Application.Current.Dispatcher.Invoke(() =>
                {
                    camera.Status = "Checking connectivity...";
                    camera.CellColor = Brushes.LightYellow;
                });

                camera.AddProtocolLog("Network", "Connectivity Test", "Testing ICMP ping connectivity");

                var isPingSuccessful = await PingCameraAsync(camera.CurrentIP);
                cancellationToken.ThrowIfCancellationRequested();

                int port = camera.EffectivePort;

                if (isPingSuccessful)
                {
                    camera.AddProtocolLog("Network", "Ping Success", "Host is reachable via ICMP ping", Models.ProtocolLogLevel.Success);
                }
                else
                {
                    camera.AddProtocolLog("Network", "Ping Failed", "ICMP ping timeout (device may block ping)", Models.ProtocolLogLevel.Warning);
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    camera.Status = isPingSuccessful
                        ? $"Ping OK, checking protocols on port {port}..."
                        : $"Ping failed, trying protocols on port {port}...";
                });

                camera.AddProtocolLog("System", "Protocol Detection", $"Starting protocol compatibility testing on port {port}");

                var protocolsToCheck = GetProtocolCheckOrder(camera.Protocol);
                camera.AddProtocolLog("System", "Protocol Order", $"Testing protocols in order: {string.Join(", ", protocolsToCheck)}");

                bool protocolFound = false;
                CameraProtocol? detectedProtocol = null;
                bool requiresAuth = false;
                bool isAuthenticated = false;
                string authMessage = string.Empty;

                foreach (var protocol in protocolsToCheck)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    camera.AddProtocolLog(protocol.ToString(), "Starting Test", $"Testing {protocol} protocol compatibility");

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        camera.Status = $"Testing {protocol} protocol...";
                    });

                    bool isCompatible = await CheckProtocolCompatibilityAsync(camera, port, protocol, cancellationToken);
                    if (isCompatible)
                    {
                        camera.AddProtocolLog(protocol.ToString(), "Protocol Found", $"{protocol} protocol confirmed and compatible", Models.ProtocolLogLevel.Success);
                        protocolFound = true;
                        detectedProtocol = protocol;
                        // The UpdateCameraForProtocol already set the status and color, but let's capture the details
                        var currentStatus = camera.Status ?? "";
                        requiresAuth = currentStatus.Contains("Auth failed") || currentStatus.Contains("Authentication");
                        isAuthenticated = currentStatus.Contains("Authentication OK");
                        break;
                    }
                    else
                    {
                        camera.AddProtocolLog(protocol.ToString(), "Protocol Failed", $"{protocol} protocol not compatible or not responding", Models.ProtocolLogLevel.Error);
                    }
                }

                // Final status check - DO NOT override if protocol was found and status was already set correctly
                if (!protocolFound)
                {
                    camera.AddProtocolLog("System", "Check Complete", "No compatible protocols found", Models.ProtocolLogLevel.Error);

                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        camera.Status = isPingSuccessful
                            ? "No compatible protocol found"
                            : "Ping failed, no compatible protocol found";
                        camera.CellColor = Brushes.LightCoral; // RED for complete failure
                        camera.AddProtocolLog("System", "Final Status", $"Camera final status set to: {camera.Status}, Color: LightCoral", Models.ProtocolLogLevel.Info);
                    });
                }
                else
                {
                    // Protocol was found - just log the completion without changing status
                    camera.AddProtocolLog("System", "Check Complete", "Compatibility check completed successfully", Models.ProtocolLogLevel.Success);

                    // Debug log to confirm the final state
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        var colorName = GetBrushName(camera.CellColor);
                        camera.AddProtocolLog("System", "Final Status", $"Camera final status: {camera.Status}, Color: {colorName}", Models.ProtocolLogLevel.Success);
                    });
                }
            }
            catch (OperationCanceledException)
            {
                camera.AddProtocolLog("System", "Check Cancelled", "Compatibility check was cancelled", Models.ProtocolLogLevel.Warning);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    camera.Status = "Check cancelled";
                    camera.CellColor = Brushes.LightGray;
                });
                throw;
            }
            catch (Exception ex)
            {
                camera.AddProtocolLog("System", "Exception", $"Error during compatibility check: {ex.Message}", Models.ProtocolLogLevel.Error);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    camera.Status = $"Exception: {ex.Message}";
                    camera.CellColor = Brushes.Red;
                });
            }
        }

        private string GetBrushName(Brush? brush)
        {
            if (brush == Brushes.LightGreen) return "LightGreen";
            if (brush == Brushes.Orange) return "Orange";
            if (brush == Brushes.LightCoral) return "LightCoral";
            if (brush == Brushes.LightGray) return "LightGray";
            if (brush == Brushes.Red) return "Red";
            if (brush == Brushes.LightYellow) return "LightYellow";
            return brush?.ToString() ?? "null";
        }


        private List<CameraProtocol> GetProtocolCheckOrder(CameraProtocol selectedProtocol)
        {
            var allProtocols = Enum.GetValues<CameraProtocol>().Where(p => p != CameraProtocol.Auto).ToList();

            // If Auto is selected or specific protocol is selected, test all protocols
            if (selectedProtocol == CameraProtocol.Auto)
            {
                // For Auto mode, test in a logical order based on popularity/reliability
                return new List<CameraProtocol>
            {
                CameraProtocol.Hikvision,
                CameraProtocol.Dahua,
                CameraProtocol.Axis,
                CameraProtocol.Onvif,
                CameraProtocol.Bosch,
                CameraProtocol.Hanwha
            };
            }
            else
            {
                // Prioritize the selected protocol, then test others
                var orderedProtocols = new List<CameraProtocol> { selectedProtocol };
                orderedProtocols.AddRange(allProtocols.Where(p => p != selectedProtocol));
                return orderedProtocols;
            }
        }

        private async Task<bool> CheckProtocolCompatibilityAsync(Camera camera, int port, CameraProtocol protocol, CancellationToken cancellationToken = default)
        {
            var timeoutDuration = TimeSpan.FromSeconds(15);
            using var timeoutCts = new CancellationTokenSource(timeoutDuration);
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                combinedCts.Token.ThrowIfCancellationRequested();

                switch (protocol)
                {
                    case CameraProtocol.Hikvision:
                        camera.AddProtocolLog("Hikvision", "HTTP Client Init", "Initializing HTTP client for Hikvision API");
                        using (var connection = new HikvisionConnection(camera.CurrentIP, port, camera.Username ?? "admin", camera.Password ?? ""))
                        {
                            camera.AddProtocolLog("Hikvision", "API Request", "Sending GET request to DeviceInfo endpoint");
                            var result = await connection.CheckCompatibilityAsync();

                            camera.AddProtocolLog("Hikvision", "Response Analysis", $"HTTP Status: {result.Message}");

                            if (result.Success && result.IsHikvisionCompatible)
                            {
                                if (result.RequiresAuthentication)
                                {
                                    camera.AddProtocolLog("Hikvision", "Auth Test", $"Testing authentication: {result.AuthenticationMessage}");
                                }

                                UpdateCameraForProtocol(camera, CameraProtocol.Hikvision, result.RequiresAuthentication, result.IsAuthenticated, result.AuthenticationMessage);
                                return true;
                            }
                        }
                        break;

                    case CameraProtocol.Dahua:
                        camera.AddProtocolLog("Dahua", "HTTP Client Init", "Initializing HTTP client for Dahua API");
                        using (var connection = new DahuaConnection(camera.CurrentIP, port, camera.Username ?? "admin", camera.Password ?? ""))
                        {
                            camera.AddProtocolLog("Dahua", "API Request", "Sending GET request to configManager endpoint");
                            var result = await connection.CheckCompatibilityAsync();

                            camera.AddProtocolLog("Dahua", "Response Analysis", $"HTTP Status: {result.Message}");

                            if (result.Success && result.IsDahuaCompatible)
                            {
                                if (result.RequiresAuthentication)
                                {
                                    camera.AddProtocolLog("Dahua", "Auth Test", $"Testing authentication: {result.AuthenticationMessage}");
                                }

                                UpdateCameraForProtocol(camera, CameraProtocol.Dahua, result.RequiresAuthentication, result.IsAuthenticated, result.AuthenticationMessage);
                                return true;
                            }
                        }
                        break;

                    case CameraProtocol.Axis:
                        camera.AddProtocolLog("Axis", "HTTP Client Init", "Initializing HTTP client for Axis API");
                        using (var connection = new AxisConnection(camera.CurrentIP, port, camera.Username ?? "admin", camera.Password ?? ""))
                        {
                            camera.AddProtocolLog("Axis", "API Request", "Sending GET request to Axis device info endpoint");
                            var result = await connection.CheckCompatibilityAsync();

                            camera.AddProtocolLog("Axis", "Response Analysis", $"HTTP Status: {result.Message}");

                            if (result.Success && result.IsAxisCompatible)
                            {
                                if (result.RequiresAuthentication)
                                {
                                    camera.AddProtocolLog("Axis", "Auth Test", $"Testing authentication: {result.AuthenticationMessage}");
                                }

                                UpdateCameraForProtocol(camera, CameraProtocol.Axis, result.RequiresAuthentication, result.IsAuthenticated, result.AuthenticationMessage);
                                return true;
                            }
                        }
                        break;

                    case CameraProtocol.Onvif:
                        camera.AddProtocolLog("ONVIF", "Service Discovery", "Discovering ONVIF device service endpoints");
                        camera.AddProtocolLog("ONVIF", "Timeout Check", $"ONVIF protocol timeout set to {timeoutDuration.TotalSeconds} seconds");

                        try
                        {
                            using (var connection = new OnvifConnection(camera.CurrentIP, port, camera.Username ?? "admin", camera.Password ?? ""))
                            {
                                camera.AddProtocolLog("ONVIF", "SOAP Request", "Sending SOAP request to GetDeviceInformation");

                                var compatibilityTask = connection.CheckCompatibilityAsync();
                                var result = await compatibilityTask.WaitAsync(combinedCts.Token);

                                camera.AddProtocolLog("ONVIF", "Response Analysis", $"SOAP Status: {result.Message}");

                                if (result.Success && result.IsOnvifCompatible)
                                {
                                    if (result.RequiresAuthentication)
                                    {
                                        camera.AddProtocolLog("ONVIF", "Auth Test", $"Testing WS-Security authentication: {result.AuthenticationMessage}");
                                    }

                                    UpdateCameraForProtocol(camera, CameraProtocol.Onvif, result.RequiresAuthentication, result.IsAuthenticated, result.AuthenticationMessage);
                                    return true;
                                }
                            }
                        }
                        catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
                        {
                            camera.AddProtocolLog("ONVIF", "Timeout", $"ONVIF protocol check timed out after {timeoutDuration.TotalSeconds} seconds", Models.ProtocolLogLevel.Warning);
                            throw;
                        }
                        break;
                }
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
            {
                camera.AddProtocolLog(protocol.ToString(), "Timeout", $"{protocol} protocol check timed out after {timeoutDuration.TotalSeconds} seconds", Models.ProtocolLogLevel.Warning);
                throw;
            }
            catch (OperationCanceledException)
            {
                camera.AddProtocolLog(protocol.ToString(), "Cancelled", "Protocol test was cancelled", Models.ProtocolLogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                camera.AddProtocolLog(protocol.ToString(), "Exception", $"Protocol test failed: {ex.Message}", Models.ProtocolLogLevel.Error);
            }

            return false;
        }

        // Update the Camera access methods to work with UI properties
        private void UpdateCameraForProtocol(Camera camera, CameraProtocol protocol, bool requiresAuthentication, bool isAuthenticated, string authenticationMessage)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Set the detected protocol
                camera.Protocol = protocol;

                // Update UI properties based on authentication results
                if (requiresAuthentication)
                {
                    if (isAuthenticated)
                    {
                        camera.Status = $"{protocol} compatible - Authentication OK";
                        camera.CellColor = Brushes.LightGreen;
                        camera.AddProtocolLog("System", "UpdateCameraForProtocol", $"Set to GREEN: {camera.Status}", Models.ProtocolLogLevel.Success);
                    }
                    else
                    {
                        camera.Status = $"{protocol} compatible - Auth failed: {authenticationMessage}";
                        camera.CellColor = Brushes.Orange;
                        camera.AddProtocolLog("System", "UpdateCameraForProtocol", $"Set to ORANGE: {camera.Status}", Models.ProtocolLogLevel.Warning);
                    }
                }
                else
                {
                    camera.Status = $"{protocol} compatible - No auth required";
                    camera.CellColor = Brushes.LightGreen;
                    camera.AddProtocolLog("System", "UpdateCameraForProtocol", $"Set to GREEN: {camera.Status}", Models.ProtocolLogLevel.Success);
                }
            });
        }

        private bool CanCheckCompatibility(object parameter)
        {
            return !IsCheckingCompatibility && Cameras?.Any(c => c.IsSelected && !string.IsNullOrEmpty(c.CurrentIP)) == true;
        }

        private async Task SendNetworkConfigAsync()
        {
            var selectedCameras = Cameras.Where(c => c.IsSelected && IsProtocolSupported(c.Protocol)).ToList();
            if (!selectedCameras.Any())
            {
                MessageBox.Show("Please select cameras with supported protocols to configure.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirmation = MessageBox.Show(
                $"Are you sure you want to send network configuration to {selectedCameras.Count} selected cameras?",
                "Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmation != MessageBoxResult.Yes)
                return;

            foreach (var camera in selectedCameras)
            {
                await SendNetworkConfigToSingleCamera(camera);
            }
        }

        private bool IsProtocolSupported(CameraProtocol protocol)
        {
            return protocol switch
            {
                CameraProtocol.Hikvision => true,
                CameraProtocol.Dahua => true,
                CameraProtocol.Axis => true,
                CameraProtocol.Onvif => true,
                CameraProtocol.Auto => false, // Auto itself doesn't support sending config
                _ => false
            };
        }

        private async Task SendNetworkConfigToSingleCamera(Camera camera)
        {
            try
            {
                camera.Status = "Sending network configuration...";
                camera.CellColor = Brushes.LightYellow;

                bool success = false;
                int port = camera.EffectivePort;

                switch (camera.Protocol)
                {
                    case CameraProtocol.Hikvision:
                        success = await SendHikvisionNetworkConfigAsync(camera, port);
                        break;
                    case CameraProtocol.Dahua:
                        success = await SendDahuaNetworkConfigAsync(camera, port);
                        break;
                    case CameraProtocol.Axis:
                        success = await SendAxisNetworkConfigAsync(camera, port);
                        break;
                    case CameraProtocol.Onvif:
                        success = await SendOnvifNetworkConfigAsync(camera, port);
                        break;
                }

                if (success)
                {
                    camera.Status = "Network configuration sent successfully";
                    camera.CellColor = Brushes.LightGreen;

                    // Update current IP if successful
                    if (!string.IsNullOrEmpty(camera.NewIP))
                    {
                        camera.CurrentIP = camera.NewIP;
                    }
                }
                else
                {
                    camera.Status = "Failed to send network configuration";
                    camera.CellColor = Brushes.LightCoral;
                }
            }
            catch (Exception ex)
            {
                camera.Status = $"Error sending network config: {ex.Message}";
                camera.CellColor = Brushes.LightCoral;
            }
        }

        private async Task<bool> SendHikvisionNetworkConfigAsync(Camera camera, int port)
        {
            try
            {
                using var connection = new HikvisionConnection(
                    camera.CurrentIP,
                    port,
                    camera.Username ?? "admin",
                    camera.Password ?? "");

                // Here you would integrate with your HikvisionApiClient for the full GET/PUT workflow
                await Task.Delay(1000); // Simulate API call
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> SendDahuaNetworkConfigAsync(Camera camera, int port)
        {
            try
            {
                using var connection = new DahuaConnection(
                    camera.CurrentIP,
                    port,
                    camera.Username ?? "admin",
                    camera.Password ?? "");

                // Note: You'll need to update DahuaConnection to accept Camera instead of NetworkConfiguration
                // var result = await connection.SendNetworkConfigurationAsync(camera);
                await Task.Delay(1000); // Simulate API call
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> SendAxisNetworkConfigAsync(Camera camera, int port)
        {
            try
            {
                using var connection = new AxisConnection(
                    camera.CurrentIP,
                    port,
                    camera.Username ?? "admin",
                    camera.Password ?? "");

                // Note: You'll need to update AxisConnection to accept Camera instead of NetworkConfiguration
                // var result = await connection.SendNetworkConfigurationAsync(camera);
                await Task.Delay(1000); // Simulate API call
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> SendOnvifNetworkConfigAsync(Camera camera, int port)
        {
            try
            {
                using var connection = new OnvifConnection(
                    camera.CurrentIP,
                    port,
                    camera.Username ?? "admin",
                    camera.Password ?? "");

                // Note: You'll need to update OnvifConnection to accept Camera instead of NetworkConfiguration
                // var result = await connection.SendNetworkConfigurationAsync(camera);
                await Task.Delay(1000); // Simulate API call
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool CanSendNetworkConfig(object parameter)
        {
            return Cameras?.Any(c => c.IsSelected && IsProtocolSupported(c.Protocol)) == true;
        }

        private async Task SendNTPConfigAsync()
        {
            var selectedCameras = Cameras.Where(c => c.IsSelected &&
                (c.Protocol == CameraProtocol.Hikvision || c.Protocol == CameraProtocol.Dahua) &&
                !string.IsNullOrEmpty(c.NewNTPServer)).ToList();

            if (!selectedCameras.Any())
            {
                MessageBox.Show("Please select Hikvision or Dahua cameras with NTP server configured.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            foreach (var camera in selectedCameras)
            {
                await SendNTPConfigToSingleCamera(camera);
            }
        }

        private async Task SendNTPConfigToSingleCamera(Camera camera)
        {
            try
            {
                camera.Status = "Sending NTP configuration...";
                camera.CellColor = Brushes.LightYellow;

                bool success = false;
                int port = camera.EffectivePort;

                switch (camera.Protocol)
                {
                    case CameraProtocol.Hikvision:
                        success = await SendHikvisionNTPConfigAsync(camera, port);
                        break;
                    case CameraProtocol.Dahua:
                        success = await SendDahuaNTPConfigAsync(camera, port);
                        break;
                }

                if (success)
                {
                    camera.Status = "NTP configuration sent successfully";
                    camera.CellColor = Brushes.LightGreen;
                }
                else
                {
                    camera.Status = "Failed to send NTP configuration";
                    camera.CellColor = Brushes.LightCoral;
                }
            }
            catch (Exception ex)
            {
                camera.Status = $"Error sending NTP config: {ex.Message}";
                camera.CellColor = Brushes.LightCoral;
            }
        }

        private async Task<bool> SendHikvisionNTPConfigAsync(Camera camera, int port)
        {
            try
            {
                // Here you would integrate with your HikvisionApiClient for NTP configuration
                await Task.Delay(1000); // Simulate API call
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> SendDahuaNTPConfigAsync(Camera camera, int port)
        {
            try
            {
                using var connection = new DahuaConnection(
                    camera.CurrentIP,
                    port,
                    camera.Username ?? "admin",
                    camera.Password ?? "");

                // Note: You'll need to update DahuaConnection to accept Camera instead of NetworkConfiguration
                // var result = await connection.SendNtpConfigurationAsync(camera);
                await Task.Delay(1000); // Simulate API call
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool CanSendNTPConfig(object parameter)
        {
            return Cameras?.Any(c => c.IsSelected &&
                (c.Protocol == CameraProtocol.Hikvision || c.Protocol == CameraProtocol.Dahua) &&
                !string.IsNullOrEmpty(c.NewNTPServer)) == true;
        }

        private void SaveConfig(object parameter)
        {
            MessageBox.Show("Save functionality to be implemented", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void LoadConfig(object parameter)
        {
            MessageBox.Show("Load functionality to be implemented", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void AddSingleCamera()
        {
            Cameras.Add(CreateNewCamera());
        }

        public void AddCameraRange(string startIP, string endIP, string username, string password)
        {
            // Implementation for adding camera range
            // This would parse the IP range and add multiple Camera objects
        }
    }
}