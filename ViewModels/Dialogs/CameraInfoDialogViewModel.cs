using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;

using wpfhikip.Models;
using wpfhikip.Protocols.Axis;
using wpfhikip.Protocols.Hikvision;
using wpfhikip.ViewModels.Commands;

namespace wpfhikip.ViewModels.Dialogs
{
    public class CameraInfoDialogViewModel : ViewModelBase, IDisposable
    {
        private readonly Camera _camera;
        private CancellationTokenSource? _loadingCancellation;
        private DispatcherTimer? _refreshTimer;
        private bool _disposed;
        private bool _autoRefreshEnabled = true;

        private bool _isLoading;
        private bool _hasError;
        private string _errorMessage = string.Empty;
        private string _loadingStatus = "Initializing...";

        // Connection Information
        private string _ipAddress = string.Empty;
        private string _port = string.Empty;
        private string _protocol = string.Empty;
        private string _username = string.Empty;
        private string _status = string.Empty;

        // Device Information
        private string _manufacturer = string.Empty;
        private string _model = string.Empty;
        private string _firmware = string.Empty;
        private string _serialNumber = string.Empty;
        private string _macAddress = string.Empty;

        // Network Settings
        private string _currentIp = string.Empty;
        private string _subnetMask = string.Empty;
        private string _gateway = string.Empty;
        private string _dns1 = string.Empty;
        private string _dns2 = string.Empty;

        // Video Stream Information
        private string _mainStream = string.Empty;
        private string _subStream = string.Empty;
        private string _resolution = string.Empty;
        private string _frameRate = string.Empty;
        private string _bitRate = string.Empty;

        public CameraInfoDialogViewModel(Camera camera)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));

            InitializeBasicInfo();
            InitializeAutoRefresh();

            // Commands
            RefreshCommand = new RelayCommand(async _ => await RefreshCameraInfoAsync(), _ => !IsLoading);
            CancelCommand = new RelayCommand(_ => CancelLoading(), _ => IsLoading);
            ToggleAutoRefreshCommand = new RelayCommand(_ => ToggleAutoRefresh(), _ => true);
        }

        // Properties
        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool HasError
        {
            get => _hasError;
            private set => SetProperty(ref _hasError, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set => SetProperty(ref _errorMessage, value);
        }

        public string LoadingStatus
        {
            get => _loadingStatus;
            private set => SetProperty(ref _loadingStatus, value);
        }

        public bool AutoRefreshEnabled
        {
            get => _autoRefreshEnabled;
            set
            {
                if (SetProperty(ref _autoRefreshEnabled, value))
                {
                    if (value)
                        StartAutoRefresh();
                    else
                        StopAutoRefresh();
                }
            }
        }

        // Connection Information Properties
        public string IpAddress
        {
            get => _ipAddress;
            private set => SetProperty(ref _ipAddress, value);
        }

        public string Port
        {
            get => _port;
            private set => SetProperty(ref _port, value);
        }

        public string Protocol
        {
            get => _protocol;
            private set => SetProperty(ref _protocol, value);
        }

        public string Username
        {
            get => _username;
            private set => SetProperty(ref _username, value);
        }

        public string Status
        {
            get => _status;
            private set => SetProperty(ref _status, value);
        }

        // Device Information Properties
        public string Manufacturer
        {
            get => _manufacturer;
            private set => SetProperty(ref _manufacturer, value);
        }

        public string Model
        {
            get => _model;
            private set => SetProperty(ref _model, value);
        }

        public string Firmware
        {
            get => _firmware;
            private set => SetProperty(ref _firmware, value);
        }

        public string SerialNumber
        {
            get => _serialNumber;
            private set => SetProperty(ref _serialNumber, value);
        }

        public string MacAddress
        {
            get => _macAddress;
            private set => SetProperty(ref _macAddress, value);
        }

        // Network Settings Properties
        public string CurrentIp
        {
            get => _currentIp;
            private set => SetProperty(ref _currentIp, value);
        }

        public string SubnetMask
        {
            get => _subnetMask;
            private set => SetProperty(ref _subnetMask, value);
        }

        public string Gateway
        {
            get => _gateway;
            private set => SetProperty(ref _gateway, value);
        }

        public string Dns1
        {
            get => _dns1;
            private set => SetProperty(ref _dns1, value);
        }

        public string Dns2
        {
            get => _dns2;
            private set => SetProperty(ref _dns2, value);
        }

        // Video Stream Information Properties
        public string MainStream
        {
            get => _mainStream;
            private set => SetProperty(ref _mainStream, value);
        }

        public string SubStream
        {
            get => _subStream;
            private set => SetProperty(ref _subStream, value);
        }

        public string Resolution
        {
            get => _resolution;
            private set => SetProperty(ref _resolution, value);
        }

        public string FrameRate
        {
            get => _frameRate;
            private set => SetProperty(ref _frameRate, value);
        }

        public string BitRate
        {
            get => _bitRate;
            private set => SetProperty(ref _bitRate, value);
        }

        // Commands
        public ICommand RefreshCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ToggleAutoRefreshCommand { get; }

        /// <summary>
        /// Loads camera information from the device
        /// </summary>
        public async Task LoadCameraInfoAsync()
        {
            if (_camera.Protocol != CameraProtocol.Axis && _camera.Protocol != CameraProtocol.Hikvision)
            {
                SetError($"Camera information loading is currently only supported for Axis and Hikvision cameras. Current protocol: {_camera.Protocol}");
                return;
            }

            if (!_camera.CanShowCameraInfo)
            {
                SetError("Camera must be compatible and authenticated to load information.");
                return;
            }

            await RefreshCameraInfoAsync();
        }

        private void InitializeAutoRefresh()
        {
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30) // Refresh every 30 seconds
            };
            _refreshTimer.Tick += async (s, e) => await AutoRefreshTimerTick();
        }

        private async Task AutoRefreshTimerTick()
        {
            if (!IsLoading && AutoRefreshEnabled)
            {
                await RefreshCameraInfoAsync();
            }
        }

        private void StartAutoRefresh()
        {
            if (_refreshTimer != null && !_refreshTimer.IsEnabled)
            {
                _refreshTimer.Start();
            }
        }

        private void StopAutoRefresh()
        {
            _refreshTimer?.Stop();
        }

        private void ToggleAutoRefresh()
        {
            AutoRefreshEnabled = !AutoRefreshEnabled;
        }

        private async Task RefreshCameraInfoAsync()
        {
            _loadingCancellation?.Cancel();
            _loadingCancellation = new CancellationTokenSource();

            IsLoading = true;
            HasError = false;
            ErrorMessage = string.Empty;

            try
            {
                LoadingStatus = "Connecting to camera...";

                switch (_camera.Protocol)
                {
                    case CameraProtocol.Hikvision:
                        await RefreshHikvisionCameraInfoAsync(_loadingCancellation.Token);
                        break;
                    case CameraProtocol.Axis:
                        await RefreshAxisCameraInfoAsync(_loadingCancellation.Token);
                        break;
                    default:
                        SetError($"Unsupported camera protocol: {_camera.Protocol}");
                        return;
                }

                LoadingStatus = "Information loaded successfully";
                Status = $"Connected - Last updated: {DateTime.Now:HH:mm:ss}";
            }
            catch (OperationCanceledException)
            {
                LoadingStatus = "Loading cancelled";
                _camera.AddProtocolLog(_camera.Protocol.ToString(), "Info Dialog", "Information loading was cancelled", ProtocolLogLevel.Info);
            }
            catch (Exception ex)
            {
                SetError($"Failed to load camera information: {ex.Message}");
                _camera.AddProtocolLog(_camera.Protocol.ToString(), "Info Dialog", $"Failed to load information: {ex.Message}", ProtocolLogLevel.Info);
                Status = $"Error - {DateTime.Now:HH:mm:ss}";
            }
            finally
            {
                IsLoading = false;
                _loadingCancellation?.Dispose();
                _loadingCancellation = null;
            }
        }

        private async Task RefreshHikvisionCameraInfoAsync(CancellationToken cancellationToken)
        {
            _camera.AddProtocolLog("Hikvision", "Info Dialog", "Starting camera information retrieval", ProtocolLogLevel.Info);

            using var hikvisionConnection = new HikvisionConnection(
                _camera.CurrentIP!,
                _camera.EffectivePort,
                _camera.Username ?? "admin",
                _camera.Password ?? "");

            using var hikvisionConfig = new HikvisionConfiguration(hikvisionConnection);
            using var hikvisionOperation = new HikvisionOperation(hikvisionConnection);

            await LoadHikvisionDeviceInformation(hikvisionConfig, cancellationToken);
            await LoadHikvisionNetworkInformation(hikvisionConfig, cancellationToken);
            await LoadHikvisionVideoStreamInformation(hikvisionOperation, cancellationToken);

            _camera.AddProtocolLog("Hikvision", "Info Dialog", "Camera information retrieved successfully", ProtocolLogLevel.Info);
        }

        private async Task LoadHikvisionDeviceInformation(HikvisionConfiguration hikvisionConfig, CancellationToken cancellationToken)
        {
            LoadingStatus = "Loading device information...";
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Get device information
                var (success, deviceInfo, error) = await hikvisionConfig.GetDeviceInfoAsync();
                if (success && deviceInfo.Count > 0)
                {
                    _camera.AddProtocolLog("Hikvision", "Device Info", $"Retrieved {deviceInfo.Count} device parameters", ProtocolLogLevel.Info);
                    UpdateHikvisionDeviceInfo(deviceInfo);
                }
                else
                {
                    _camera.AddProtocolLog("Hikvision", "Device Info", $"Failed to get device info: {error}", ProtocolLogLevel.Info);
                }

                // Get system capabilities for additional info
                var (capSuccess, capabilities, capError) = await hikvisionConfig.GetSystemCapabilitiesAsync();
                if (capSuccess && capabilities.Count > 0)
                {
                    _camera.AddProtocolLog("Hikvision", "Device Info", $"Retrieved {capabilities.Count} capability parameters", ProtocolLogLevel.Info);
                    UpdateHikvisionDeviceInfoFromCapabilities(capabilities);
                }

                // Update the camera model with retrieved information
                UpdateCameraWithDeviceInfo();
            }
            catch (Exception ex)
            {
                _camera.AddProtocolLog("Hikvision", "Device Info", $"Error loading device information: {ex.Message}", ProtocolLogLevel.Info);
                // Continue with other operations even if device info fails
            }
        }

        private void UpdateHikvisionDeviceInfo(Dictionary<string, string> deviceInfo)
        {
            // Hikvision device info typically contains these fields
            Manufacturer = GetValueOrDefault(deviceInfo, "manufacturer", "Hikvision");
            Model = GetValueOrDefault(deviceInfo, "model", GetValueOrDefault(deviceInfo, "deviceName", "Not detected"));
            Firmware = GetValueOrDefault(deviceInfo, "firmwareVersion", GetValueOrDefault(deviceInfo, "version", "Not detected"));
            SerialNumber = GetValueOrDefault(deviceInfo, "serialNumber", "Not detected");
            MacAddress = GetValueOrDefault(deviceInfo, "macAddress", "Not detected");

            // Try alternative field names
            if (Model == "Not detected")
                Model = GetValueOrDefault(deviceInfo, "deviceType", "Not detected");

            if (Firmware == "Not detected")
                Firmware = GetValueOrDefault(deviceInfo, "firmwareReleasedDate", "Not detected");
        }

        private void UpdateHikvisionDeviceInfoFromCapabilities(Dictionary<string, string> capabilities)
        {
            // System capabilities might contain additional device information
            if (Model == "Not detected")
                Model = GetValueOrDefault(capabilities, "deviceType", Model);

            if (SerialNumber == "Not detected")
                SerialNumber = GetValueOrDefault(capabilities, "serialNumber", SerialNumber);
        }

        private async Task LoadHikvisionNetworkInformation(HikvisionConfiguration hikvisionConfig, CancellationToken cancellationToken)
        {
            LoadingStatus = "Loading network information...";
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Get network interface configuration
                var (success, networkXml, error) = await hikvisionConfig.GetConfigurationAsync(HikvisionUrl.NetworkInterfaceIpAddress);
                if (success)
                {
                    var networkInfo = HikvisionXmlTemplates.ParseResponseXml(networkXml);
                    _camera.AddProtocolLog("Hikvision", "Network Info", $"Retrieved {networkInfo.Count} network parameters", ProtocolLogLevel.Info);
                    UpdateHikvisionNetworkInfo(networkInfo);
                }
                else
                {
                    _camera.AddProtocolLog("Hikvision", "Network Info", $"Failed to get network info: {error}", ProtocolLogLevel.Info);
                    // Set basic IP from what we know
                    CurrentIp = _camera.CurrentIP ?? "Not configured";
                }

                // Update camera settings with current configuration
                UpdateCameraWithNetworkInfo();
            }
            catch (Exception ex)
            {
                _camera.AddProtocolLog("Hikvision", "Network Info", $"Error loading network information: {ex.Message}", ProtocolLogLevel.Info);
                CurrentIp = _camera.CurrentIP ?? "Not configured";
            }
        }

        private void UpdateHikvisionNetworkInfo(Dictionary<string, string> networkInfo)
        {
            CurrentIp = GetValueOrDefault(networkInfo, "ipAddress", _camera.CurrentIP ?? "Not configured");
            SubnetMask = GetValueOrDefault(networkInfo, "subnetMask", "Not configured");
            Gateway = GetValueOrDefault(networkInfo, "ipAddress", "Not configured"); // Gateway might be nested

            // DNS servers might be in separate calls or nested
            Dns1 = "Not configured";
            Dns2 = "Not configured";

            // Try to extract gateway from nested structure if available
            if (Gateway == "Not configured")
            {
                Gateway = GetValueOrDefault(networkInfo, "DefaultGateway", "Not configured");
            }
        }

        private async Task LoadHikvisionVideoStreamInformation(HikvisionOperation hikvisionOperation, CancellationToken cancellationToken)
        {
            LoadingStatus = "Loading video stream information...";
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Get stream URLs
                MainStream = hikvisionOperation.GetRtspStreamUrl(1, 1); // Main stream
                SubStream = hikvisionOperation.GetRtspStreamUrl(1, 2);  // Sub stream

                // Get HTTP stream URLs as alternatives
                var httpMainStream = hikvisionOperation.GetHttpStreamUrl(1, 1);
                var httpSubStream = hikvisionOperation.GetHttpStreamUrl(1, 2);

                // Get camera status for additional video information
                var (success, status, error) = await hikvisionOperation.GetCameraStatusAsync();
                if (success && status.Count > 0)
                {
                    UpdateHikvisionVideoInfo(status);
                }
                else
                {
                    // Set default values
                    Resolution = "Not detected";
                    FrameRate = "Not detected";
                    BitRate = "Not detected";
                }

                // Update camera video stream information
                _camera.VideoStream.MainStreamUrl = MainStream;
                _camera.VideoStream.SubStreamUrl = SubStream;

                if (Resolution != "Not detected")
                    _camera.VideoStream.Resolution = Resolution;
                if (FrameRate != "Not detected")
                    _camera.VideoStream.FrameRate = FrameRate;
                if (BitRate != "Not detected")
                    _camera.VideoStream.BitRate = BitRate;
            }
            catch (Exception ex)
            {
                _camera.AddProtocolLog("Hikvision", "Video Info", $"Error loading video information: {ex.Message}", ProtocolLogLevel.Info);
                // Continue with basic stream URLs even if detailed info fails
                MainStream = hikvisionOperation.GetRtspStreamUrl(1, 1);
                SubStream = hikvisionOperation.GetRtspStreamUrl(1, 2);
            }
        }

        private void UpdateHikvisionVideoInfo(Dictionary<string, string> status)
        {
            // Extract video information from status
            Resolution = GetValueOrDefault(status, "resolution", "Not detected");
            FrameRate = GetValueOrDefault(status, "frameRate", "Not detected");
            BitRate = GetValueOrDefault(status, "bitRate", "Not detected");

            // Try alternative field names
            if (Resolution == "Not detected")
                Resolution = GetValueOrDefault(status, "videoResolution", "Not detected");

            if (FrameRate == "Not detected")
                FrameRate = GetValueOrDefault(status, "videoFrameRate", "Not detected");

            if (BitRate == "Not detected")
                BitRate = GetValueOrDefault(status, "videoBitRate", "Not detected");
        }

        private async Task RefreshAxisCameraInfoAsync(CancellationToken cancellationToken)
        {
            _camera.AddProtocolLog("Axis", "Info Dialog", "Starting camera information retrieval", ProtocolLogLevel.Info);

            using var axisConnection = new AxisConnection(
                _camera.CurrentIP!,
                _camera.EffectivePort,
                _camera.Username ?? "admin",
                _camera.Password ?? "");

            using var axisConfig = new AxisConfiguration(axisConnection);
            using var axisOperation = new AxisOperation(axisConnection);

            await LoadAxisDeviceInformation(axisConfig, axisConnection, cancellationToken);
            await LoadAxisNetworkInformation(axisConfig, cancellationToken);
            await LoadAxisVideoStreamInformation(axisOperation, axisConnection, cancellationToken);

            _camera.AddProtocolLog("Axis", "Info Dialog", "Camera information retrieved successfully", ProtocolLogLevel.Info);
        }

        private async Task LoadAxisDeviceInformation(AxisConfiguration axisConfig, AxisConnection axisConnection, CancellationToken cancellationToken)
        {
            LoadingStatus = "Loading device information...";
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Get system parameters directly - this is what contains the actual data
                var (success, sysParams, error) = await axisConfig.GetSystemParametersAsync();
                if (success && sysParams.Count > 0)
                {
                    _camera.AddProtocolLog("Axis", "Device Info", $"Retrieved {sysParams.Count} system parameters", ProtocolLogLevel.Info);

                    // Log some of the actual keys we received for debugging
                    var logMessage = "Available keys: " + string.Join(", ", sysParams.Keys.Take(10));
                    _camera.AddProtocolLog("Axis", "Device Info", logMessage, ProtocolLogLevel.Info);

                    UpdateAxisDeviceInfoFromSystemParams(sysParams);
                }
                else
                {
                    _camera.AddProtocolLog("Axis", "Device Info", $"Failed to get system parameters: {error}", ProtocolLogLevel.Info);
                }

                // Also try to get device info (this might be different API)
                var (devSuccess, deviceInfo, devError) = await axisConfig.GetDeviceInfoAsync();
                if (devSuccess && deviceInfo.Count > 0)
                {
                    _camera.AddProtocolLog("Axis", "Device Info", $"Retrieved {deviceInfo.Count} device info parameters", ProtocolLogLevel.Info);
                    UpdateAxisDeviceInfoFromDictionary(deviceInfo);
                }

                // Try to get full Properties group for additional information
                await LoadAxisPropertiesInformation(axisConnection, cancellationToken);

                // Update the camera model with retrieved information
                UpdateCameraWithDeviceInfo();
            }
            catch (Exception ex)
            {
                _camera.AddProtocolLog("Axis", "Device Info", $"Error loading device information: {ex.Message}", ProtocolLogLevel.Info);
                // Continue with other operations even if device info fails
            }
        }

        private async Task LoadAxisPropertiesInformation(AxisConnection axisConnection, CancellationToken cancellationToken)
        {
            try
            {
                // Make a direct HTTP call to get all Properties which includes Image.Resolution and other details
                using var httpClient = axisConnection.CreateAuthenticatedHttpClient();
                var url = $"http://{_camera.CurrentIP}:{_camera.EffectivePort}/axis-cgi/param.cgi?action=list&group=Properties";

                var response = await httpClient.GetAsync(url, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var props = ParseAxisParameterResponse(content);

                    _camera.AddProtocolLog("Axis", "Properties", $"Retrieved {props.Count} properties", ProtocolLogLevel.Info);

                    // Update device info from properties if we haven't got it yet
                    UpdateAxisDeviceInfoFromSystemParams(props);

                    // Update video info from properties
                    UpdateAxisVideoInfoFromStatus(props);
                }
            }
            catch (Exception ex)
            {
                _camera.AddProtocolLog("Axis", "Properties", $"Error loading properties: {ex.Message}", ProtocolLogLevel.Info);
            }
        }

        private void UpdateAxisDeviceInfoFromSystemParams(Dictionary<string, object> sysParams)
        {
            // Based on the actual API response format you provided
            var manufacturer = GetValueFromMultipleKeys(sysParams,
                new[] { "root.Brand.Brand", "Brand.Brand" },
                null);
            if (!string.IsNullOrEmpty(manufacturer))
                Manufacturer = manufacturer;
            else if (Manufacturer == "Not detected")
                Manufacturer = "Axis Communications";

            // Hardware ID from the actual response
            var model = GetValueFromMultipleKeys(sysParams,
                new[] { "root.Properties.System.HardwareID", "Properties.System.HardwareID" },
                null);
            if (!string.IsNullOrEmpty(model))
                Model = model;

            // Firmware version from the actual response
            var firmware = GetValueFromMultipleKeys(sysParams,
                new[] { "root.Properties.Firmware.Version", "Properties.Firmware.Version" },
                null);
            if (!string.IsNullOrEmpty(firmware))
                Firmware = firmware;

            // Serial number from the actual response
            var serial = GetValueFromMultipleKeys(sysParams,
                new[] { "root.Properties.System.SerialNumber", "Properties.System.SerialNumber" },
                null);
            if (!string.IsNullOrEmpty(serial))
                SerialNumber = serial;

            // Try to get MAC address from network parameters
            var mac = GetValueFromMultipleKeys(sysParams,
                new[] { "root.Network.eth0.MACAddress", "Network.eth0.MACAddress", "root.Network.Ethernet.MACAddress" },
                null);
            if (!string.IsNullOrEmpty(mac))
                MacAddress = mac;

            // Also try to get resolution from image properties in this response
            var imageRes = GetValueFromMultipleKeys(sysParams,
                new[] { "root.Properties.Image.Resolution", "Properties.Image.Resolution" },
                null);
            if (!string.IsNullOrEmpty(imageRes))
                Resolution = imageRes;
        }

        private void UpdateAxisDeviceInfoFromDictionary(Dictionary<string, object> deviceInfo)
        {
            // This method handles the GetDeviceInfoAsync response which might have different structure
            var tempManufacturer = GetValueFromMultipleKeys(deviceInfo,
                new[] { "root.Brand.Brand", "Brand.Brand", "Properties.System.Brand" },
                null);
            if (!string.IsNullOrEmpty(tempManufacturer))
                Manufacturer = tempManufacturer;

            var tempModel = GetValueFromMultipleKeys(deviceInfo,
                new[] { "root.Properties.System.HardwareID", "Properties.System.HardwareID", "Properties.System.ProductName", "root.Properties.System.ProductName" },
                null);
            if (!string.IsNullOrEmpty(tempModel))
                Model = tempModel;

            var tempFirmware = GetValueFromMultipleKeys(deviceInfo,
                new[] { "root.Properties.Firmware.Version", "Properties.Firmware.Version", "root.Properties.System.Version" },
                null);
            if (!string.IsNullOrEmpty(tempFirmware))
                Firmware = tempFirmware;

            var tempSerial = GetValueFromMultipleKeys(deviceInfo,
                new[] { "root.Properties.System.SerialNumber", "Properties.System.SerialNumber", "root.Properties.System.Serial" },
                null);
            if (!string.IsNullOrEmpty(tempSerial))
                SerialNumber = tempSerial;
        }

        private void UpdateCameraWithDeviceInfo()
        {
            if (Manufacturer != "Not detected" && !string.IsNullOrEmpty(Manufacturer))
                _camera.Manufacturer = Manufacturer;

            if (Model != "Not detected" && !string.IsNullOrEmpty(Model))
                _camera.Model = Model;

            if (Firmware != "Not detected" && !string.IsNullOrEmpty(Firmware))
                _camera.Firmware = Firmware;

            if (SerialNumber != "Not detected" && !string.IsNullOrEmpty(SerialNumber))
                _camera.SerialNumber = SerialNumber;

            if (MacAddress != "Not detected" && !string.IsNullOrEmpty(MacAddress))
                _camera.MacAddress = MacAddress;
        }

        private async Task LoadAxisNetworkInformation(AxisConfiguration axisConfig, CancellationToken cancellationToken)
        {
            LoadingStatus = "Loading network information...";
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Get network parameters first (this is more reliable)
                var (netSuccess, netParams, netError) = await axisConfig.GetNetworkParametersAsync();
                if (netSuccess && netParams.Count > 0)
                {
                    _camera.AddProtocolLog("Axis", "Network Info", $"Retrieved {netParams.Count} network parameters", ProtocolLogLevel.Info);
                    UpdateAxisNetworkInfoFromParams(netParams);
                }

                // Also try to get network configuration using JSON API
                var (success, networkConfig, error) = await axisConfig.GetNetworkConfigurationAsync();
                if (success && networkConfig.Count > 0)
                {
                    _camera.AddProtocolLog("Axis", "Network Info", $"Retrieved {networkConfig.Count} network config parameters", ProtocolLogLevel.Info);
                    UpdateAxisNetworkInfoFromConfig(networkConfig);
                }

                // Update camera settings with current configuration
                UpdateCameraWithNetworkInfo();
            }
            catch (Exception ex)
            {
                _camera.AddProtocolLog("Axis", "Network Info", $"Error loading network information: {ex.Message}", ProtocolLogLevel.Info);
                // Set basic IP from what we know
                CurrentIp = _camera.CurrentIP ?? "Not configured";
            }
        }

        private void UpdateAxisNetworkInfoFromParams(Dictionary<string, object> netParams)
        {
            // Try various possible keys for network information
            var tempIp = GetValueFromMultipleKeys(netParams,
                new[] { "root.Network.eth0.IPAddress", "Network.eth0.IPAddress", "root.Network.Interface.I0.IPAddress" },
                null);
            if (!string.IsNullOrEmpty(tempIp))
                CurrentIp = tempIp;
            else
                CurrentIp = _camera.CurrentIP ?? "Not configured";

            var tempMask = GetValueFromMultipleKeys(netParams,
                new[] { "root.Network.eth0.SubnetMask", "Network.eth0.SubnetMask", "root.Network.Interface.I0.SubnetMask" },
                "Not configured");
            SubnetMask = tempMask;

            var tempGateway = GetValueFromMultipleKeys(netParams,
                new[] { "root.Network.DefaultRouter", "Network.DefaultRouter", "root.Network.Gateway" },
                "Not configured");
            Gateway = tempGateway;

            var tempDns1 = GetValueFromMultipleKeys(netParams,
                new[] { "root.Network.NameServer1.Address", "Network.NameServer1.Address", "root.Network.DNS.Server1" },
                "Not configured");
            Dns1 = tempDns1;

            var tempDns2 = GetValueFromMultipleKeys(netParams,
                new[] { "root.Network.NameServer2.Address", "Network.NameServer2.Address", "root.Network.DNS.Server2" },
                "Not configured");
            Dns2 = tempDns2;

            // Try to get MAC address if we haven't found it yet
            if (MacAddress == "Not detected")
            {
                var tempMac = GetValueFromMultipleKeys(netParams,
                    new[] { "root.Network.eth0.MACAddress", "Network.eth0.MACAddress", "root.Network.Ethernet.MACAddress" },
                    "Not detected");
                MacAddress = tempMac;
            }
        }

        private void UpdateAxisNetworkInfoFromConfig(Dictionary<string, object> networkConfig)
        {
            // Use ConfigExtractor helper class from AxisJsonTemplates
            // Note: You'll need to implement AxisJsonTemplates.ConfigExtractor or adapt this code
            var extractedIp = GetValueFromMultipleKeys(networkConfig, new[] { "ipAddress", "ip" }, null);
            if (!string.IsNullOrEmpty(extractedIp) && extractedIp != "" && CurrentIp == "Not configured")
                CurrentIp = extractedIp;

            var extractedMask = GetValueFromMultipleKeys(networkConfig, new[] { "subnetMask", "mask" }, null);
            if (!string.IsNullOrEmpty(extractedMask) && SubnetMask == "Not configured")
                SubnetMask = extractedMask;

            var extractedGateway = GetValueFromMultipleKeys(networkConfig, new[] { "gateway", "defaultGateway" }, null);
            if (!string.IsNullOrEmpty(extractedGateway) && Gateway == "Not configured")
                Gateway = extractedGateway;
        }

        private void UpdateCameraWithNetworkInfo()
        {
            if (SubnetMask != "Not configured")
                _camera.Settings.SubnetMask = SubnetMask;

            if (Gateway != "Not configured")
                _camera.Settings.DefaultGateway = Gateway;

            if (Dns1 != "Not configured")
                _camera.Settings.DNS1 = Dns1;

            if (Dns2 != "Not configured")
                _camera.Settings.DNS2 = Dns2;

            if (MacAddress != "Not detected")
                _camera.MacAddress = MacAddress;
        }

        private async Task LoadAxisVideoStreamInformation(AxisOperation axisOperation, AxisConnection axisConnection, CancellationToken cancellationToken)
        {
            LoadingStatus = "Loading video stream information...";
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // Use AxisOperation to get proper stream URLs
                MainStream = axisOperation.GetMjpegStreamUrl(1, 1920);
                SubStream = axisOperation.GetMjpegStreamUrl(1, 704);

                // Try to get actual video parameters from the camera status
                var (success, status, error) = await axisOperation.GetCameraStatusAsync();
                if (success && status.Count > 0)
                {
                    UpdateAxisVideoInfoFromStatus(status);
                }

                // Update camera video stream information
                _camera.VideoStream.MainStreamUrl = MainStream;
                _camera.VideoStream.SubStreamUrl = SubStream;

                if (Resolution != "Not detected")
                    _camera.VideoStream.Resolution = Resolution;
                if (FrameRate != "Not detected")
                    _camera.VideoStream.FrameRate = FrameRate;
                if (BitRate != "Not detected")
                    _camera.VideoStream.BitRate = BitRate;
            }
            catch (Exception ex)
            {
                _camera.AddProtocolLog("Axis", "Video Info", $"Error loading video information: {ex.Message}", ProtocolLogLevel.Info);
                // Continue with basic stream URLs even if detailed info fails
                var protocol = _camera.EffectivePort == 443 ? "https" : "http";
                MainStream = $"{protocol}://{_camera.CurrentIP}:{_camera.EffectivePort}/axis-cgi/mjpg/video.cgi?resolution=1920x1080";
                SubStream = $"{protocol}://{_camera.CurrentIP}:{_camera.EffectivePort}/axis-cgi/mjpg/video.cgi?resolution=704x576";
            }
        }

        private void UpdateAxisVideoInfoFromStatus(Dictionary<string, object> status)
        {
            // Based on your API response, resolution is in Properties.Image.Resolution
            var res = GetValueFromMultipleKeys(status,
                new[] { "root.Properties.Image.Resolution", "Properties.Image.Resolution" },
                null);
            if (!string.IsNullOrEmpty(res))
                Resolution = res;

            // These might not be available in all cameras
            var frameRate = GetValueFromMultipleKeys(status,
                new[] { "root.Properties.Image.FrameRate", "Properties.Image.FrameRate", "Image.FrameRate" },
                null);
            if (!string.IsNullOrEmpty(frameRate))
                FrameRate = frameRate;

            var bitRate = GetValueFromMultipleKeys(status,
                new[] { "root.Properties.Image.Compression", "Properties.Image.Compression", "Image.Compression" },
                null);
            if (!string.IsNullOrEmpty(bitRate))
                BitRate = bitRate;

            // Try to get video formats
            var format = GetValueFromMultipleKeys(status,
                new[] { "root.Properties.Image.Format", "Properties.Image.Format" },
                null);
            if (!string.IsNullOrEmpty(format) && BitRate == "Not detected")
                BitRate = $"Formats: {format}";
        }

        private static Dictionary<string, object> ParseAxisParameterResponse(string response)
        {
            var result = new Dictionary<string, object>();

            if (string.IsNullOrWhiteSpace(response))
                return result;

            var lines = response.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var equalIndex = line.IndexOf('=');
                if (equalIndex > 0)
                {
                    var key = line[..equalIndex].Trim();
                    var value = line[(equalIndex + 1)..].Trim();
                    result[key] = value;
                }
            }

            return result;
        }

        private void InitializeBasicInfo()
        {
            // Initialize with basic camera information
            IpAddress = _camera.CurrentIP ?? "N/A";
            Port = _camera.EffectivePort.ToString();
            Protocol = _camera.Protocol.ToString();
            Username = string.IsNullOrEmpty(_camera.Username) ? "N/A" : _camera.Username;
            Status = _camera.Status ?? "N/A";

            // Initialize with existing camera information if available
            Manufacturer = _camera.Manufacturer ?? "Not detected";
            Model = _camera.Model ?? "Not detected";
            Firmware = _camera.Firmware ?? "Not detected";
            SerialNumber = _camera.SerialNumber ?? "Not detected";
            MacAddress = _camera.MacAddress ?? "Not detected";

            CurrentIp = _camera.CurrentIP ?? "N/A";
            SubnetMask = _camera.Settings.SubnetMask ?? "Not configured";
            Gateway = _camera.Settings.DefaultGateway ?? "Not configured";
            Dns1 = _camera.Settings.DNS1 ?? "Not configured";
            Dns2 = _camera.Settings.DNS2 ?? "Not configured";

            MainStream = _camera.VideoStream.MainStreamUrl ?? "Not available";
            SubStream = _camera.VideoStream.SubStreamUrl ?? "Not available";
            Resolution = _camera.VideoStream.Resolution ?? "Not detected";
            FrameRate = _camera.VideoStream.FrameRate ?? "Not detected";
            BitRate = _camera.VideoStream.BitRate ?? "Not detected";
        }

        private void CancelLoading()
        {
            _loadingCancellation?.Cancel();
        }

        private void SetError(string message)
        {
            HasError = true;
            ErrorMessage = message;
            LoadingStatus = "Error occurred";
        }

        private static string GetValueOrDefault(Dictionary<string, string> dictionary, string key, string defaultValue)
        {
            if (dictionary.TryGetValue(key, out var value) && value != null)
            {
                var stringValue = value.ToString();
                return string.IsNullOrWhiteSpace(stringValue) ? defaultValue : stringValue;
            }
            return defaultValue;
        }

        private static string GetValueFromMultipleKeys(Dictionary<string, object> dictionary, string[] keys, string? defaultValue)
        {
            foreach (var key in keys)
            {
                if (dictionary.TryGetValue(key, out var value) && value != null)
                {
                    var stringValue = value.ToString();
                    if (!string.IsNullOrWhiteSpace(stringValue))
                        return stringValue;
                }
            }
            return defaultValue ?? "Not detected";
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _loadingCancellation?.Cancel();
                    _loadingCancellation?.Dispose();
                    _refreshTimer?.Stop();
                    _refreshTimer = null;
                }
                _disposed = true;
            }
        }
    }
}