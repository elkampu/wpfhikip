using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using wpfhikip.Models;
using wpfhikip.Protocols.Common;
using wpfhikip.ViewModels.Commands;

namespace wpfhikip.ViewModels
{
    public class NetConfViewModel : ViewModelBase
    {
        private const int PingTimeout = 3000;
        private const int PortTimeout = 5000;

        private ObservableCollection<Camera> _cameras = new();
        private bool _isCheckingCompatibility;
        private CancellationTokenSource? _compatibilityCheckCancellation;

        public ObservableCollection<Camera> Cameras
        {
            get => _cameras;
            set => SetProperty(ref _cameras, value);
        }

        public bool IsCheckingCompatibility
        {
            get => _isCheckingCompatibility;
            set => SetProperty(ref _isCheckingCompatibility, value);
        }

        /// <summary>
        /// Gets the available protocol options for the ComboBox
        /// </summary>
        public IEnumerable<CameraProtocol> ProtocolOptions { get; } = Enum.GetValues<CameraProtocol>();

        // Commands
        public ICommand AddCameraCommand { get; }
        public ICommand DeleteSelectedCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand CheckCompatibilityCommand { get; }
        public ICommand CancelCompatibilityCommand { get; }
        public ICommand SendNetworkConfigCommand { get; }
        public ICommand SendNTPConfigCommand { get; }
        public ICommand SaveConfigCommand { get; }
        public ICommand LoadConfigCommand { get; }

        public NetConfViewModel()
        {
            _cameras = new ObservableCollection<Camera> { CreateNewCamera() };

            AddCameraCommand = new RelayCommand(_ => AddCamera());
            DeleteSelectedCommand = new RelayCommand(_ => DeleteSelected(), _ => CanDeleteSelected());
            SelectAllCommand = new RelayCommand(_ => SelectAll());
            CheckCompatibilityCommand = new RelayCommand(async _ => await CheckCompatibilityAsync(), _ => CanCheckCompatibility());
            CancelCompatibilityCommand = new RelayCommand(_ => CancelCompatibilityCheck(), _ => CanCancelCompatibilityCheck());
            SendNetworkConfigCommand = new RelayCommand(async _ => await SendNetworkConfigAsync(), _ => CanSendNetworkConfig());
            SendNTPConfigCommand = new RelayCommand(async _ => await SendNTPConfigAsync(), _ => CanSendNTPConfig());
            SaveConfigCommand = new RelayCommand(_ => SaveConfig());
            LoadConfigCommand = new RelayCommand(_ => LoadConfig());
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

        private void AddCamera() => Cameras.Add(CreateNewCamera());

        private void DeleteSelected()
        {
            var selectedItems = Cameras.Where(c => c.IsSelected).ToList();
            foreach (var item in selectedItems)
            {
                Cameras.Remove(item);
            }
        }

        private bool CanDeleteSelected() => Cameras?.Any(c => c.IsSelected) == true;

        private void SelectAll()
        {
            foreach (var camera in Cameras)
            {
                camera.IsSelected = true;
            }
        }

        private async Task CheckCompatibilityAsync()
        {
            var selectedCameras = GetSelectedCamerasWithIP();
            if (selectedCameras.Count == 0)
            {
                MessageBox.Show("Please select cameras with IP addresses to check compatibility.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await RunCompatibilityCheck(selectedCameras);
        }

        private void CancelCompatibilityCheck()
        {
            _compatibilityCheckCancellation?.Cancel();
        }

        private bool CanCancelCompatibilityCheck() => IsCheckingCompatibility && _compatibilityCheckCancellation != null;

        private List<Camera> GetSelectedCamerasWithIP()
        {
            return Cameras.Where(c => c.IsSelected && !string.IsNullOrEmpty(c.CurrentIP)).ToList();
        }

        private async Task RunCompatibilityCheck(List<Camera> selectedCameras)
        {
            _compatibilityCheckCancellation?.Cancel();
            _compatibilityCheckCancellation = new CancellationTokenSource();
            IsCheckingCompatibility = true;

            try
            {
                InitializeCamerasForCheck(selectedCameras);

                var tasks = selectedCameras.Select(camera =>
                    CheckSingleCameraCompatibilityAsync(camera, _compatibilityCheckCancellation.Token));
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                HandleCancelledChecks(selectedCameras);
            }
            catch (Exception ex)
            {
                HandleCheckErrors(selectedCameras, ex);
            }
            finally
            {
                IsCheckingCompatibility = false;
                _compatibilityCheckCancellation?.Dispose();
                _compatibilityCheckCancellation = null;
            }
        }

        private static void InitializeCamerasForCheck(List<Camera> cameras)
        {
            foreach (var camera in cameras)
            {
                camera.ClearProtocolLogs();
                camera.AddProtocolLog("System", "Check Started",
                    $"Initializing compatibility check for {camera.CurrentIP}:{camera.EffectivePort}");

                Application.Current.Dispatcher.Invoke(() =>
                {
                    camera.Status = "Initializing check...";
                    camera.CellColor = Brushes.LightYellow;
                });
            }
        }

        private async Task CheckSingleCameraCompatibilityAsync(Camera camera, CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                await InitializeCameraCheck(camera, cancellationToken);

                // Step 1: Always perform connectivity checks first (ping + port)
                var connectivityResult = await CheckConnectivityAsync(camera, cancellationToken);
                if (!connectivityResult.CanConnect)
                {
                    camera.AddProtocolLog("System", "Connectivity Check",
                        "Connectivity check failed. Skipping protocol detection.", ProtocolLogLevel.Error);

                    var result = ProtocolCompatibilityResult.CreateFailure(
                        $"Cannot reach {camera.CurrentIP}:{camera.EffectivePort} - {connectivityResult.Message}");
                    SetFinalStatus(camera, result);
                    return;
                }

                camera.AddProtocolLog("System", "Connectivity Check",
                    $"Connectivity confirmed: {connectivityResult.Message}", ProtocolLogLevel.Success);

                // Step 2: Check if a specific protocol is selected (not Auto)
                ProtocolCompatibilityResult protocolResult;
                if (camera.Protocol != CameraProtocol.Auto)
                {
                    camera.AddProtocolLog("System", "Protocol Selection",
                        $"Specific protocol selected: {camera.Protocol}. Checking selected protocol first.");

                    protocolResult = await CheckSpecificProtocolAsync(camera, cancellationToken);
                    if (protocolResult.IsCompatible)
                    {
                        SetFinalStatus(camera, protocolResult);
                        return;
                    }

                    camera.AddProtocolLog("System", "Protocol Selection",
                        $"Selected protocol {camera.Protocol} failed. Falling back to auto-detection.");
                }

                // Step 3: Proceed with full protocol detection
                protocolResult = await ProtocolManager.CheckCompatibilityAsync(camera, cancellationToken);
                SetFinalStatus(camera, protocolResult);
            }
            catch (OperationCanceledException)
            {
                HandleCancelledCamera(camera);
                throw;
            }
            catch (Exception ex)
            {
                HandleCameraError(camera, ex);
            }
        }

        private async Task<ProtocolCompatibilityResult> CheckSpecificProtocolAsync(Camera camera, CancellationToken cancellationToken)
        {
            try
            {
                camera.AddProtocolLog(camera.Protocol.ToString(), "Specific Protocol Check",
                    $"Testing selected protocol: {camera.Protocol}");

                Application.Current.Dispatcher.Invoke(() =>
                {
                    camera.Status = $"Checking {camera.Protocol} protocol...";
                });

                return await ProtocolManager.CheckSingleProtocolAsync(camera, camera.Protocol, cancellationToken);
            }
            catch (Exception ex)
            {
                camera.AddProtocolLog(camera.Protocol.ToString(), "Specific Protocol Error",
                    $"Error checking {camera.Protocol}: {ex.Message}", ProtocolLogLevel.Error);
                return ProtocolCompatibilityResult.CreateFailure($"Protocol check failed: {ex.Message}", camera.Protocol);
            }
        }

        private async Task<ConnectivityResult> CheckConnectivityAsync(Camera camera, CancellationToken cancellationToken)
        {
            camera.AddProtocolLog("Network", "Connectivity Check",
                $"Checking connectivity to {camera.CurrentIP}:{camera.EffectivePort}");

            Application.Current.Dispatcher.Invoke(() =>
            {
                camera.Status = "Checking connectivity...";
            });

            // Step 1: Ping check
            var isPingSuccessful = await PingCameraAsync(camera.CurrentIP ?? string.Empty);
            LogPingResult(camera, isPingSuccessful);

            // Step 2: Port connectivity check
            var isPortAvailable = await CheckPortAvailabilityAsync(camera.CurrentIP ?? string.Empty, camera.EffectivePort, cancellationToken);
            LogPortResult(camera, isPortAvailable);

            // Determine overall connectivity
            if (isPingSuccessful && isPortAvailable)
            {
                return new ConnectivityResult(true, "Ping and port check successful");
            }
            else if (!isPingSuccessful && isPortAvailable)
            {
                return new ConnectivityResult(true, "Port accessible (ping may be blocked)");
            }
            else if (isPingSuccessful && !isPortAvailable)
            {
                return new ConnectivityResult(false, "Host reachable but port not accessible");
            }
            else
            {
                return new ConnectivityResult(false, "Host unreachable and port not accessible");
            }
        }

        private static async Task<bool> CheckPortAvailabilityAsync(string ipAddress, int port, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(ipAddress))
                return false;

            try
            {
                using var tcpClient = new TcpClient();
                using var timeoutCts = new CancellationTokenSource(PortTimeout);
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                await tcpClient.ConnectAsync(ipAddress, port, combinedCts.Token);
                return tcpClient.Connected;
            }
            catch (SocketException)
            {
                return false;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static void LogPortResult(Camera camera, bool isPortAvailable)
        {
            var level = isPortAvailable ? ProtocolLogLevel.Success : ProtocolLogLevel.Warning;
            var message = isPortAvailable
                ? $"Port {camera.EffectivePort} is accessible"
                : $"Port {camera.EffectivePort} is not accessible or connection timeout";

            camera.AddProtocolLog("Network", isPortAvailable ? "Port Check Success" : "Port Check Failed", message, level);
        }

        private async Task InitializeCameraCheck(Camera camera, CancellationToken cancellationToken)
        {
            camera.ClearProtocolLogs();
            camera.AddProtocolLog("System", "Starting Compatibility Check",
                $"Beginning enhanced compatibility check for {camera.CurrentIP}:{camera.EffectivePort}");

            Application.Current.Dispatcher.Invoke(() =>
            {
                camera.Status = "Starting compatibility check...";
                camera.CellColor = Brushes.LightYellow;
            });

            cancellationToken.ThrowIfCancellationRequested();
        }

        private static async Task<bool> PingCameraAsync(string ipAddress)
        {
            if (string.IsNullOrEmpty(ipAddress))
                return false;

            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ipAddress, PingTimeout);
                return reply.Status == IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }

        private static void LogPingResult(Camera camera, bool isPingSuccessful)
        {
            var level = isPingSuccessful ? ProtocolLogLevel.Success : ProtocolLogLevel.Warning;
            var message = isPingSuccessful
                ? "Host is reachable via ICMP ping"
                : "ICMP ping timeout (device may block ping)";

            camera.AddProtocolLog("Network", isPingSuccessful ? "Ping Success" : "Ping Failed", message, level);
        }

        private static void SetFinalStatus(Camera camera, ProtocolCompatibilityResult result)
        {
            if (!result.IsCompatible)
            {
                camera.AddProtocolLog("System", "Check Complete",
                    "No compatible protocols found", ProtocolLogLevel.Error);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    camera.Status = "No compatible protocol found";
                    camera.CellColor = Brushes.LightCoral;
                    camera.IsCompatible = false;
                    camera.RequiresAuthentication = false;
                    camera.IsAuthenticated = false;
                });
            }
            else
            {
                camera.AddProtocolLog("System", "Check Complete",
                    "Compatibility check completed successfully", ProtocolLogLevel.Success);

                UpdateCameraForProtocol(camera, result);
            }
        }

        private static void UpdateCameraForProtocol(Camera camera, ProtocolCompatibilityResult result)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                camera.Protocol = result.DetectedProtocol;
                camera.IsCompatible = result.IsCompatible;
                camera.RequiresAuthentication = result.RequiresAuthentication;
                camera.IsAuthenticated = result.IsAuthenticated;

                if (result.RequiresAuthentication)
                {
                    if (result.IsAuthenticated)
                    {
                        camera.Status = $"{result.DetectedProtocol} compatible - Authentication OK";
                        camera.CellColor = Brushes.LightGreen;
                    }
                    else
                    {
                        camera.Status = $"{result.DetectedProtocol} compatible - Auth failed: {result.AuthenticationMessage}";
                        camera.CellColor = Brushes.Orange;
                    }
                }
                else
                {
                    camera.Status = $"{result.DetectedProtocol} compatible - No auth required";
                    camera.CellColor = Brushes.LightGreen;
                }
            });
        }

        private static void HandleCancelledChecks(List<Camera> cameras)
        {
            foreach (var camera in cameras)
            {
                HandleCancelledCamera(camera);
            }
        }

        private static void HandleCheckErrors(List<Camera> cameras, Exception ex)
        {
            foreach (var camera in cameras)
            {
                HandleCameraError(camera, ex);
            }
        }

        private static void HandleCancelledCamera(Camera camera)
        {
            camera.AddProtocolLog("System", "Check Cancelled",
                "Compatibility check was cancelled by user", ProtocolLogLevel.Warning);

            Application.Current.Dispatcher.Invoke(() =>
            {
                camera.Status = "Check cancelled";
                camera.CellColor = Brushes.LightGray;
            });
        }

        private static void HandleCameraError(Camera camera, Exception ex)
        {
            camera.AddProtocolLog("System", "Check Error",
                $"Error during compatibility check: {ex.Message}", ProtocolLogLevel.Error);

            Application.Current.Dispatcher.Invoke(() =>
            {
                camera.Status = $"Error: {ex.Message}";
                camera.CellColor = Brushes.LightCoral;
            });
        }

        private bool CanCheckCompatibility() => !IsCheckingCompatibility &&
            Cameras?.Any(c => c.IsSelected && !string.IsNullOrEmpty(c.CurrentIP)) == true;

        private async Task SendNetworkConfigAsync()
        {
            var selectedCameras = Cameras.Where(c => c.IsSelected && ProtocolConnectionFactory.IsProtocolSupported(c.Protocol)).ToList();
            if (selectedCameras.Count == 0)
            {
                MessageBox.Show("Please select cameras with supported protocols to configure.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirmation = MessageBox.Show(
                $"Are you sure you want to send network configuration to {selectedCameras.Count} selected cameras?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirmation == MessageBoxResult.Yes)
            {
                var tasks = selectedCameras.Select(SendNetworkConfigToSingleCamera);
                await Task.WhenAll(tasks);
            }
        }

        private async Task SendNetworkConfigToSingleCamera(Camera camera)
        {
            try
            {
                camera.Status = "Sending network configuration...";
                camera.CellColor = Brushes.LightYellow;

                // Clear previous logs and add detailed logging
                camera.ClearProtocolLogs();
                camera.AddProtocolLog("System", "Network Config",
                    $"Starting network configuration for {camera.CurrentIP}:{camera.EffectivePort}");
                camera.AddProtocolLog("System", "Config Values",
                    $"NewIP: {camera.NewIP}, NewMask: {camera.NewMask}, NewGateway: {camera.NewGateway}, DNS1: {camera.NewDNS1}, DNS2: {camera.NewDNS2}");

                bool success = await ProtocolManager.SendNetworkConfigAsync(camera);

                // Log the result
                camera.AddProtocolLog("System", "Network Config",
                    success ? "Network configuration sent successfully" : "Failed to send network configuration",
                    success ? ProtocolLogLevel.Success : ProtocolLogLevel.Error);

                UpdateCameraAfterNetworkConfig(camera, success);
            }
            catch (Exception ex)
            {
                camera.AddProtocolLog("System", "Network Config Error",
                    $"Exception during network configuration: {ex.Message}", ProtocolLogLevel.Error);
                camera.Status = $"Error sending network config: {ex.Message}";
                camera.CellColor = Brushes.LightCoral;
            }
        }

        private static void UpdateCameraAfterNetworkConfig(Camera camera, bool success)
        {
            if (success)
            {
                camera.Status = "Network configuration sent successfully";
                camera.CellColor = Brushes.LightGreen;

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

        private bool CanSendNetworkConfig() => Cameras?.Any(c => c.IsSelected && ProtocolConnectionFactory.IsProtocolSupported(c.Protocol)) == true;

        private async Task SendNTPConfigAsync()
        {
            var selectedCameras = Cameras.Where(c => c.IsSelected &&
                ProtocolConnectionFactory.IsProtocolSupported(c.Protocol) &&
                !string.IsNullOrEmpty(c.NewNTPServer)).ToList();

            if (selectedCameras.Count == 0)
            {
                MessageBox.Show("Please select cameras with supported protocols and NTP server configured.",
                    "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var tasks = selectedCameras.Select(SendNTPConfigToSingleCamera);
            await Task.WhenAll(tasks);
        }

        private async Task SendNTPConfigToSingleCamera(Camera camera)
        {
            try
            {
                camera.Status = "Sending NTP configuration...";
                camera.CellColor = Brushes.LightYellow;

                bool success = await ProtocolManager.SendNTPConfigAsync(camera);

                camera.Status = success ? "NTP configuration sent successfully" : "Failed to send NTP configuration";
                camera.CellColor = success ? Brushes.LightGreen : Brushes.LightCoral;
            }
            catch (Exception ex)
            {
                camera.Status = $"Error sending NTP config: {ex.Message}";
                camera.CellColor = Brushes.LightCoral;
            }
        }

        private bool CanSendNTPConfig() => Cameras?.Any(c => c.IsSelected &&
            ProtocolConnectionFactory.IsProtocolSupported(c.Protocol) &&
            !string.IsNullOrEmpty(c.NewNTPServer)) == true;

        private void SaveConfig()
        {
            // Implementation for saving configuration
            MessageBox.Show("Save configuration feature not yet implemented.", "Not Implemented",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void LoadConfig()
        {
            // Implementation for loading configuration
            MessageBox.Show("Load configuration feature not yet implemented.", "Not Implemented",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void AddCameraRange(string startIP, string endIP, string username, string password)
        {
            // Implementation for adding camera range
        }

        /// <summary>
        /// Represents the result of connectivity checks (ping + port)
        /// </summary>
        private sealed record ConnectivityResult(bool CanConnect, string Message);
    }
}