using wpfhikip.Models;
using wpfhikip.Protocols.Axis;
using wpfhikip.Protocols.Hikvision;

namespace wpfhikip.Protocols.Common
{
    /// <summary>
    /// Manages protocol detection and operations for cameras
    /// </summary>
    public static class ProtocolManager
    {
        private const int DefaultTimeoutSeconds = 15;

        private static readonly IReadOnlyList<CameraProtocol> DefaultProtocolOrder = new[]
        {
        CameraProtocol.Hikvision,
        CameraProtocol.Dahua,
        CameraProtocol.Axis,
        CameraProtocol.Onvif
    };

        /// <summary>
        /// Checks compatibility for a camera across all supported protocols
        /// </summary>
        public static async Task<ProtocolCompatibilityResult> CheckCompatibilityAsync(
            Camera camera,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(camera);

            if (string.IsNullOrWhiteSpace(camera.CurrentIP))
            {
                return ProtocolCompatibilityResult.CreateFailure("IP address is required");
            }

            var protocolsToCheck = GetProtocolCheckOrder(camera.Protocol);

            foreach (var protocol in protocolsToCheck)
            {
                cancellationToken.ThrowIfCancellationRequested();

                camera.AddProtocolLog(protocol.ToString(), "Starting Test",
                    $"Testing {protocol} protocol compatibility");

                var result = await CheckSingleProtocolAsync(camera, protocol, cancellationToken);

                if (result.Success && result.IsCompatible)
                {
                    camera.AddProtocolLog(protocol.ToString(), "Protocol Found",
                        $"{protocol} protocol confirmed and compatible", ProtocolLogLevel.Success);
                    return result;
                }

                camera.AddProtocolLog(protocol.ToString(), "Protocol Failed",
                    $"{protocol} protocol not compatible or not responding", ProtocolLogLevel.Error);
            }

            return ProtocolCompatibilityResult.CreateFailure("No compatible protocols found");
        }

        /// <summary>
        /// Checks compatibility for a specific protocol
        /// </summary>
        public static async Task<ProtocolCompatibilityResult> CheckSingleProtocolAsync(
            Camera camera,
            CameraProtocol protocol,
            CancellationToken cancellationToken = default)
        {
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(DefaultTimeoutSeconds));
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                using var connection = ProtocolConnectionFactory.CreateConnection(
                    protocol,
                    camera.CurrentIP!,
                    camera.EffectivePort,
                    camera.Username ?? "admin",
                    camera.Password ?? "");

                return await connection.CheckCompatibilityAsync(combinedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
            {
                var message = $"{protocol} protocol check timed out after {DefaultTimeoutSeconds} seconds";
                camera.AddProtocolLog(protocol.ToString(), "Timeout", message, ProtocolLogLevel.Warning);
                throw;
            }
            catch (OperationCanceledException)
            {
                camera.AddProtocolLog(protocol.ToString(), "Cancelled",
                    "Protocol test was cancelled", ProtocolLogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                camera.AddProtocolLog(protocol.ToString(), "Exception",
                    $"Protocol test failed: {ex.Message}", ProtocolLogLevel.Error);
                return ProtocolCompatibilityResult.CreateFailure(ex.Message, protocol);
            }
        }

        /// <summary>
        /// Loads comprehensive camera information including device, network, and video stream details
        /// </summary>
        public static async Task<bool> LoadCameraInfoAsync(
            Camera camera,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(camera);

            if (!ProtocolConnectionFactory.IsProtocolSupported(camera.Protocol))
            {
                camera.AddProtocolLog("System", "Info Load",
                    $"Protocol {camera.Protocol} is not supported for information retrieval", ProtocolLogLevel.Error);
                // Initialize basic structure but don't populate defaults
                EnsureBasicStructure(camera);
                return false;
            }

            if (!camera.CanShowCameraInfo)
            {
                camera.AddProtocolLog("System", "Info Load",
                    "Camera must be compatible and authenticated to load information", ProtocolLogLevel.Error);
                // Initialize basic structure but don't populate defaults
                EnsureBasicStructure(camera);
                return false;
            }

            // Always initialize Settings and VideoStream first
            camera.Settings ??= new CameraSettings();
            camera.VideoStream ??= new CameraVideoStream();

            bool networkSuccess = false;
            bool deviceSuccess = false;
            bool videoSuccess = false;

            try
            {
                camera.AddProtocolLog(camera.Protocol.ToString(), "Info Load",
                    $"Starting camera information retrieval for {camera.CurrentIP}:{camera.EffectivePort}");

                using var connection = ProtocolConnectionFactory.CreateConnection(
                    camera.Protocol,
                    camera.CurrentIP!,
                    camera.EffectivePort,
                    camera.Username ?? "admin",
                    camera.Password ?? "");

                // Load device information
                try
                {
                    await LoadDeviceInfoAsync(camera, connection, cancellationToken);
                    deviceSuccess = true;
                }
                catch (Exception ex)
                {
                    camera.AddProtocolLog(camera.Protocol.ToString(), "Device Info Error",
                        $"Failed to load device info: {ex.Message}", ProtocolLogLevel.Warning);
                }

                // Load network information  
                try
                {
                    await LoadNetworkInfoAsync(camera, connection, cancellationToken);
                    networkSuccess = true;
                }
                catch (Exception ex)
                {
                    camera.AddProtocolLog(camera.Protocol.ToString(), "Network Info Error",
                        $"Failed to load network info: {ex.Message}", ProtocolLogLevel.Warning);
                }

                // Load video stream information
                try
                {
                    await LoadVideoStreamInfoAsync(camera, connection, cancellationToken);
                    videoSuccess = true;
                }
                catch (Exception ex)
                {
                    camera.AddProtocolLog(camera.Protocol.ToString(), "Video Info Error",
                        $"Failed to load video info: {ex.Message}", ProtocolLogLevel.Warning);
                }

                camera.AddProtocolLog(camera.Protocol.ToString(), "Info Load",
                    $"Camera information retrieval completed - Device: {(deviceSuccess ? "✓" : "✗")}, Network: {(networkSuccess ? "✓" : "✗")}, Video: {(videoSuccess ? "✓" : "✗")}",
                    ProtocolLogLevel.Info);
            }
            catch (OperationCanceledException)
            {
                camera.AddProtocolLog(camera.Protocol.ToString(), "Info Load",
                    "Information loading was cancelled", ProtocolLogLevel.Warning);
                throw;
            }
            catch (Exception ex)
            {
                camera.AddProtocolLog(camera.Protocol.ToString(), "Info Load Error",
                    $"Failed to load camera information: {ex.Message}", ProtocolLogLevel.Error);
            }

            // Don't apply any fallback values - let the UI show "Not configured" for missing values

            return networkSuccess || deviceSuccess || videoSuccess; // Return true if any section loaded successfully
        }

        /// <summary>
        /// Ensures basic object structure is initialized without setting default values
        /// </summary>
        private static void EnsureBasicStructure(Camera camera)
        {
            // Initialize settings if null but don't set any default values
            camera.Settings ??= new CameraSettings();
            camera.VideoStream ??= new CameraVideoStream();
        }

        /// <summary>
        /// Loads device information (manufacturer, model, firmware, etc.)
        /// </summary>
        public static async Task LoadDeviceInfoAsync(
            Camera camera,
            IProtocolConnection connection,
            CancellationToken cancellationToken = default)
        {
            try
            {
                camera.AddProtocolLog(camera.Protocol.ToString(), "Device Info",
                    "Loading device information...");

                switch (camera.Protocol)
                {
                    case CameraProtocol.Hikvision:
                        await LoadHikvisionDeviceInfoAsync(camera, connection as HikvisionConnection, cancellationToken);
                        break;
                    case CameraProtocol.Axis:
                        await LoadAxisDeviceInfoAsync(camera, connection as AxisConnection, cancellationToken);
                        break;
                    default:
                        camera.AddProtocolLog(camera.Protocol.ToString(), "Device Info",
                            $"Device information loading not implemented for {camera.Protocol}");
                        // Don't set defaults - let UI show "Not detected"
                        break;
                }
            }
            catch (Exception ex)
            {
                camera.AddProtocolLog(camera.Protocol.ToString(), "Device Info Error",
                    $"Error loading device information: {ex.Message}", ProtocolLogLevel.Error);
                // Don't set fallback values - let the UI show "Not detected"
            }
        }

        /// <summary>
        /// Loads network configuration information
        /// </summary>
        public static async Task LoadNetworkInfoAsync(
            Camera camera,
            IProtocolConnection connection,
            CancellationToken cancellationToken = default)
        {
            try
            {
                camera.AddProtocolLog(camera.Protocol.ToString(), "Network Info",
                    "Loading network information...");

                switch (camera.Protocol)
                {
                    case CameraProtocol.Hikvision:
                        await LoadHikvisionNetworkInfoAsync(camera, connection as HikvisionConnection, cancellationToken);
                        break;
                    case CameraProtocol.Axis:
                        await LoadAxisNetworkInfoAsync(camera, connection as AxisConnection, cancellationToken);
                        break;
                    default:
                        camera.AddProtocolLog(camera.Protocol.ToString(), "Network Info",
                            $"Network information loading not implemented for {camera.Protocol}");
                        // Don't set defaults - let UI show "Not configured"
                        break;
                }
            }
            catch (Exception ex)
            {
                camera.AddProtocolLog(camera.Protocol.ToString(), "Network Info Error",
                    $"Error loading network information: {ex.Message}", ProtocolLogLevel.Error);
                // Don't set defaults - let UI show "Not configured"
            }
        }

        /// <summary>
        /// Loads video stream information and URLs
        /// </summary>
        public static async Task LoadVideoStreamInfoAsync(
            Camera camera,
            IProtocolConnection connection,
            CancellationToken cancellationToken = default)
        {
            try
            {
                camera.AddProtocolLog(camera.Protocol.ToString(), "Video Info",
                    "Loading video stream information...");

                switch (camera.Protocol)
                {
                    case CameraProtocol.Hikvision:
                        await LoadHikvisionVideoInfoAsync(camera, connection as HikvisionConnection, cancellationToken);
                        break;
                    case CameraProtocol.Axis:
                        await LoadAxisVideoInfoAsync(camera, connection as AxisConnection, cancellationToken);
                        break;
                    default:
                        camera.AddProtocolLog(camera.Protocol.ToString(), "Video Info",
                            $"Video information loading not implemented for {camera.Protocol}");
                        // Set basic stream URLs but leave other fields as null
                        camera.VideoStream ??= new CameraVideoStream();
                        if (string.IsNullOrEmpty(camera.VideoStream.MainStreamUrl))
                            camera.VideoStream.MainStreamUrl = $"rtsp://{camera.CurrentIP}/stream1";
                        if (string.IsNullOrEmpty(camera.VideoStream.SubStreamUrl))
                            camera.VideoStream.SubStreamUrl = $"rtsp://{camera.CurrentIP}/stream2";
                        break;
                }
            }
            catch (Exception ex)
            {
                camera.AddProtocolLog(camera.Protocol.ToString(), "Video Info Error",
                    $"Error loading video information: {ex.Message}", ProtocolLogLevel.Error);

                // Set basic fallback URLs only
                camera.VideoStream ??= new CameraVideoStream();
                if (string.IsNullOrEmpty(camera.VideoStream.MainStreamUrl))
                    camera.VideoStream.MainStreamUrl = $"rtsp://{camera.CurrentIP}/stream1";
                if (string.IsNullOrEmpty(camera.VideoStream.SubStreamUrl))
                    camera.VideoStream.SubStreamUrl = $"rtsp://{camera.CurrentIP}/stream2";
            }
        }

        /// <summary>
        /// Sends network configuration to a camera
        /// </summary>
        public static async Task<bool> SendNetworkConfigAsync(
            Camera camera,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(camera);

            if (!ProtocolConnectionFactory.IsProtocolSupported(camera.Protocol))
            {
                camera.AddProtocolLog("System", "Network Config",
                    $"Protocol {camera.Protocol} is not supported for network configuration", ProtocolLogLevel.Error);
                return false;
            }

            try
            {
                camera.AddProtocolLog(camera.Protocol.ToString(), "Network Config",
                    $"Creating connection to {camera.CurrentIP}:{camera.EffectivePort}");

                using var connection = ProtocolConnectionFactory.CreateConnection(
                    camera.Protocol,
                    camera.CurrentIP!,
                    camera.EffectivePort,
                    camera.Username ?? "admin",
                    camera.Password ?? "");

                var config = new NetworkConfiguration
                {
                    IPAddress = camera.NewIP,
                    SubnetMask = camera.NewMask,
                    DefaultGateway = camera.NewGateway
                };

                camera.AddProtocolLog(camera.Protocol.ToString(), "Network Config",
                    $"Sending configuration: IP={config.IPAddress}, Mask={config.SubnetMask}, Gateway={config.DefaultGateway}");

                // For Hikvision, use the specialized configuration method that logs to the camera
                if (camera.Protocol == CameraProtocol.Hikvision)
                {
                    return await SendHikvisionNetworkConfigWithLogging(camera, connection as HikvisionConnection, cancellationToken);
                }

                // For other protocols, use the standard method
                bool result = await connection.SendNetworkConfigAsync(config, cancellationToken);

                camera.AddProtocolLog(camera.Protocol.ToString(), "Network Config",
                    result ? "Network configuration sent successfully" : "Failed to send network configuration",
                    result ? ProtocolLogLevel.Success : ProtocolLogLevel.Error);

                return result;
            }
            catch (Exception ex)
            {
                camera.AddProtocolLog(camera.Protocol.ToString(), "Network Config Error",
                    $"Exception during network configuration: {ex.Message}", ProtocolLogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// Sends NTP configuration to a camera
        /// </summary>
        public static async Task<bool> SendNTPConfigAsync(
            Camera camera,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(camera);

            if (!ProtocolConnectionFactory.IsProtocolSupported(camera.Protocol) ||
                string.IsNullOrEmpty(camera.NewNTPServer))
            {
                return false;
            }

            try
            {
                using var connection = ProtocolConnectionFactory.CreateConnection(
                    camera.Protocol,
                    camera.CurrentIP!,
                    camera.EffectivePort,
                    camera.Username ?? "admin",
                    camera.Password ?? "");

                var config = new NTPConfiguration
                {
                    NTPServer = camera.NewNTPServer
                };

                return await connection.SendNTPConfigAsync(config, cancellationToken);
            }
            catch
            {
                return false;
            }
        }


        private static async Task LoadHikvisionDeviceInfoAsync(
            Camera camera,
            HikvisionConnection connection,
            CancellationToken cancellationToken)
        {
            using var hikvisionConfig = new HikvisionConfiguration(connection);

            // Get device information
            var (success, deviceInfo, error) = await hikvisionConfig.GetDeviceInfoAsync();
            if (success && deviceInfo.Count > 0)
            {
                camera.AddProtocolLog("Hikvision", "Device Info", $"Retrieved {deviceInfo.Count} device parameters", ProtocolLogLevel.Info);
                UpdateCameraDeviceInfo(camera, deviceInfo, "Hikvision");
            }
            else
            {
                camera.AddProtocolLog("Hikvision", "Device Info", $"Failed to get device info: {error}", ProtocolLogLevel.Info);
            }

            // Get system capabilities for additional info
            var (capSuccess, capabilities, capError) = await hikvisionConfig.GetSystemCapabilitiesAsync();
            if (capSuccess && capabilities.Count > 0)
            {
                camera.AddProtocolLog("Hikvision", "Device Info", $"Retrieved {capabilities.Count} capability parameters", ProtocolLogLevel.Info);
                UpdateCameraDeviceInfoFromCapabilities(camera, capabilities);
            }
        }

        private static async Task LoadAxisDeviceInfoAsync(
            Camera camera,
            AxisConnection connection,
            CancellationToken cancellationToken)
        {
            using var axisConfig = new AxisConfiguration(connection);

            // Get system parameters
            var (success, sysParams, error) = await axisConfig.GetSystemParametersAsync();
            if (success && sysParams.Count > 0)
            {
                camera.AddProtocolLog("Axis", "Device Info", $"Retrieved {sysParams.Count} system parameters", ProtocolLogLevel.Info);
                UpdateCameraDeviceInfoFromParams(camera, sysParams, "Axis Communications");
            }

            // Get device info
            var (devSuccess, deviceInfo, devError) = await axisConfig.GetDeviceInfoAsync();
            if (devSuccess && deviceInfo.Count > 0)
            {
                camera.AddProtocolLog("Axis", "Device Info", $"Retrieved {deviceInfo.Count} device info parameters", ProtocolLogLevel.Info);
                UpdateCameraDeviceInfoFromParams(camera, deviceInfo, "Axis Communications");
            }
        }

        private static async Task LoadHikvisionNetworkInfoAsync(
            Camera camera,
            HikvisionConnection connection,
            CancellationToken cancellationToken)
        {
            using var hikvisionConfig = new HikvisionConfiguration(connection);

            var (success, networkXml, error) = await hikvisionConfig.GetConfigurationAsync(HikvisionUrl.NetworkInterfaceIpAddress);
            if (success)
            {
                var networkInfo = HikvisionXmlTemplates.ParseResponseXml(networkXml);
                camera.AddProtocolLog("Hikvision", "Network Info",
                    $"Retrieved {networkInfo.Count} network parameters: {string.Join(", ", networkInfo.Keys)}", ProtocolLogLevel.Info);
                UpdateCameraNetworkInfo(camera, networkInfo);
            }
            else
            {
                camera.AddProtocolLog("Hikvision", "Network Info", $"Failed to get network info: {error}", ProtocolLogLevel.Info);
            }
        }

        private static async Task LoadAxisNetworkInfoAsync(
            Camera camera,
            AxisConnection connection,
            CancellationToken cancellationToken)
        {
            using var axisConfig = new AxisConfiguration(connection);

            bool networkConfigLoaded = false;

            var (success, networkConfig, error) = await axisConfig.GetNetworkConfigurationAsync();
            if (success && networkConfig.Count > 0)
            {
                camera.AddProtocolLog("Axis", "Network Info", $"Retrieved {networkConfig.Count} network config parameters", ProtocolLogLevel.Info);
                UpdateCameraNetworkInfoFromParams(camera, networkConfig);
                networkConfigLoaded = true;
            }

            var (netSuccess, netParams, netError) = await axisConfig.GetNetworkParametersAsync();
            if (netSuccess && netParams.Count > 0)
            {
                camera.AddProtocolLog("Axis", "Network Info", $"Retrieved {netParams.Count} network parameters", ProtocolLogLevel.Info);
                UpdateCameraNetworkInfoFromParams(camera, netParams);
                networkConfigLoaded = true;
            }

            if (!networkConfigLoaded)
            {
                camera.AddProtocolLog("Axis", "Network Info", "No network configuration retrieved from camera", ProtocolLogLevel.Warning);
            }
        }

        private static async Task LoadHikvisionVideoInfoAsync(
            Camera camera,
            HikvisionConnection connection,
            CancellationToken cancellationToken)
        {
            using var hikvisionOperation = new HikvisionOperation(connection);

            // Set stream URLs
            camera.VideoStream.MainStreamUrl = hikvisionOperation.GetRtspStreamUrl(1, 1);
            camera.VideoStream.SubStreamUrl = hikvisionOperation.GetRtspStreamUrl(1, 2);

            // Get camera status for video information
            var (success, status, error) = await hikvisionOperation.GetCameraStatusAsync();
            if (success && status.Count > 0)
            {
                UpdateCameraVideoInfo(camera, status);
            }
        }

        private static async Task LoadAxisVideoInfoAsync(
            Camera camera,
            AxisConnection connection,
            CancellationToken cancellationToken)
        {
            using var axisOperation = new AxisOperation(connection);

            // Set stream URLs
            camera.VideoStream.MainStreamUrl = axisOperation.GetMjpegStreamUrl(1, 1920);
            camera.VideoStream.SubStreamUrl = axisOperation.GetMjpegStreamUrl(1, 704);

            // Get camera status for video information
            var (success, status, error) = await axisOperation.GetCameraStatusAsync();
            if (success && status.Count > 0)
            {
                UpdateCameraVideoInfoFromParams(camera, status);
            }
        }

        private static void UpdateCameraDeviceInfo(Camera camera, Dictionary<string, string> deviceInfo, string defaultManufacturer)
        {
            camera.Manufacturer = GetValueOrDefault(deviceInfo, "manufacturer", defaultManufacturer);
            camera.Model = GetValueOrDefault(deviceInfo, "model", GetValueOrDefault(deviceInfo, "deviceName", null));
            camera.Firmware = GetValueOrDefault(deviceInfo, "firmwareVersion", GetValueOrDefault(deviceInfo, "version", null));
            camera.SerialNumber = GetValueOrDefault(deviceInfo, "serialNumber", null);
            camera.MacAddress = GetValueOrDefault(deviceInfo, "macAddress", null);
        }

        private static void UpdateCameraDeviceInfoFromParams(Camera camera, Dictionary<string, object> deviceParams, string defaultManufacturer)
        {
            camera.Manufacturer = GetValueFromMultipleKeys(deviceParams,
                new[] { "root.Brand.Brand", "Brand.Brand", "Properties.System.Brand" }, defaultManufacturer);

            camera.Model = GetValueFromMultipleKeys(deviceParams,
                new[] { "root.Properties.System.HardwareID", "Properties.System.HardwareID", "Properties.System.ProductName" }, null);

            camera.Firmware = GetValueFromMultipleKeys(deviceParams,
                new[] { "root.Properties.Firmware.Version", "Properties.Firmware.Version", "Properties.System.Version" }, null);

            camera.SerialNumber = GetValueFromMultipleKeys(deviceParams,
                new[] { "root.Properties.System.SerialNumber", "Properties.System.SerialNumber", "Properties.System.Serial" }, null);

            camera.MacAddress = GetValueFromMultipleKeys(deviceParams,
                new[] { "root.Network.eth0.MACAddress", "Network.eth0.MACAddress", "Network.Ethernet.MACAddress" }, null);
        }

        private static void UpdateCameraDeviceInfoFromCapabilities(Camera camera, Dictionary<string, string> capabilities)
        {
            if (string.IsNullOrEmpty(camera.Model))
                camera.Model = GetValueOrDefault(capabilities, "deviceType", null);

            if (string.IsNullOrEmpty(camera.SerialNumber))
                camera.SerialNumber = GetValueOrDefault(capabilities, "serialNumber", null);
        }

        private static void UpdateCameraNetworkInfo(Camera camera, Dictionary<string, string> networkInfo)
        {
            // Ensure Settings is initialized
            camera.Settings ??= new CameraSettings();

            // Log all received network info for debugging
            camera.AddProtocolLog("Hikvision", "Network Raw Data",
                $"Received {networkInfo.Count} network parameters: {string.Join("; ", networkInfo.Select(kvp => $"{kvp.Key}='{kvp.Value}'"))}",
                ProtocolLogLevel.Info);

            // Check each key individually with case-insensitive comparison
            var subnetMask = GetValueOrDefaultCaseInsensitive(networkInfo, "subnetMask", null);
            if (!string.IsNullOrWhiteSpace(subnetMask))
            {
                camera.Settings.SubnetMask = subnetMask;
                camera.AddProtocolLog("Hikvision", "Network Parsing", $"Set SubnetMask: '{subnetMask}'", ProtocolLogLevel.Success);
            }
            else
            {
                camera.AddProtocolLog("Hikvision", "Network Parsing",
                    $"SubnetMask not found. Available keys: [{string.Join(", ", networkInfo.Keys)}]", ProtocolLogLevel.Warning);
            }

            var gateway = GetValueOrDefaultCaseInsensitive(networkInfo, "defaultGateway", null);
            if (!string.IsNullOrWhiteSpace(gateway))
            {
                camera.Settings.DefaultGateway = gateway;
                camera.AddProtocolLog("Hikvision", "Network Parsing", $"Set DefaultGateway: '{gateway}'", ProtocolLogLevel.Success);
            }
            else
            {
                camera.AddProtocolLog("Hikvision", "Network Parsing",
                    $"DefaultGateway not found. Available keys: [{string.Join(", ", networkInfo.Keys)}]", ProtocolLogLevel.Warning);
            }

            var dns1 = GetValueOrDefaultCaseInsensitive(networkInfo, "primaryDNS", null);
            if (!string.IsNullOrWhiteSpace(dns1))
            {
                camera.Settings.DNS1 = dns1;
                camera.AddProtocolLog("Hikvision", "Network Parsing", $"Set PrimaryDNS: '{dns1}'", ProtocolLogLevel.Success);
            }
            else
            {
                camera.AddProtocolLog("Hikvision", "Network Parsing",
                    $"PrimaryDNS not found. Available keys: [{string.Join(", ", networkInfo.Keys)}]", ProtocolLogLevel.Warning);
            }

            var dns2 = GetValueOrDefaultCaseInsensitive(networkInfo, "secondaryDNS", null);
            if (!string.IsNullOrWhiteSpace(dns2))
            {
                camera.Settings.DNS2 = dns2;
                camera.AddProtocolLog("Hikvision", "Network Parsing", $"Set SecondaryDNS: '{dns2}'", ProtocolLogLevel.Success);
            }
            else
            {
                camera.AddProtocolLog("Hikvision", "Network Parsing",
                    $"SecondaryDNS not found. Available keys: [{string.Join(", ", networkInfo.Keys)}]", ProtocolLogLevel.Warning);
            }

            // Log final camera settings state
            camera.AddProtocolLog("Hikvision", "Network Final State",
                $"Final Settings: SubnetMask='{camera.Settings.SubnetMask}', DefaultGateway='{camera.Settings.DefaultGateway}', DNS1='{camera.Settings.DNS1}', DNS2='{camera.Settings.DNS2}'",
                ProtocolLogLevel.Info);
        }

        // Add this new helper method for case-insensitive dictionary lookup
        private static string? GetValueOrDefaultCaseInsensitive(Dictionary<string, string> dictionary, string key, string? defaultValue)
        {
            // First try exact match
            if (dictionary.TryGetValue(key, out var exactValue) && !string.IsNullOrWhiteSpace(exactValue))
            {
                return exactValue;
            }

            // Then try case-insensitive match
            var entry = dictionary.FirstOrDefault(kvp =>
                string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(kvp.Value));

            return entry.Key != null ? entry.Value : defaultValue;
        }
        private static void UpdateCameraNetworkInfoFromParams(Camera camera, Dictionary<string, object> networkParams)
        {
            // Ensure Settings is initialized
            camera.Settings ??= new CameraSettings();

            // Only update with actual values from the camera (no fallbacks)
            var subnetMask = GetValueFromMultipleKeys(networkParams,
                new[] { "root.Network.eth0.SubnetMask", "Network.eth0.SubnetMask", "subnetMask", "mask" }, null);
            if (!string.IsNullOrWhiteSpace(subnetMask))
                camera.Settings.SubnetMask = subnetMask;

            var gateway = GetValueFromMultipleKeys(networkParams,
                new[] { "root.Network.DefaultRouter", "Network.DefaultRouter", "gateway", "defaultGateway" }, null);
            if (!string.IsNullOrWhiteSpace(gateway))
                camera.Settings.DefaultGateway = gateway;

            var dns1 = GetValueFromMultipleKeys(networkParams,
                new[] { "root.Network.NameServer1.Address", "Network.NameServer1.Address", "dns1" }, null);
            if (!string.IsNullOrWhiteSpace(dns1))
                camera.Settings.DNS1 = dns1;

            var dns2 = GetValueFromMultipleKeys(networkParams,
                new[] { "root.Network.NameServer2.Address", "Network.NameServer2.Address", "dns2" }, null);
            if (!string.IsNullOrWhiteSpace(dns2))
                camera.Settings.DNS2 = dns2;

            var macAddress = GetValueFromMultipleKeys(networkParams,
                new[] { "root.Network.eth0.MACAddress", "Network.eth0.MACAddress", "Network.Ethernet.MACAddress" }, null);
            if (!string.IsNullOrWhiteSpace(macAddress))
                camera.MacAddress = macAddress;
        }

        private static void UpdateCameraVideoInfo(Camera camera, Dictionary<string, string> status)
        {
            // Ensure VideoStream is initialized
            camera.VideoStream ??= new CameraVideoStream();

            var resolution = GetValueOrDefault(status, "resolution", GetValueOrDefault(status, "videoResolution", null));
            if (!string.IsNullOrWhiteSpace(resolution))
                camera.VideoStream.Resolution = resolution;

            var frameRate = GetValueOrDefault(status, "frameRate", GetValueOrDefault(status, "videoFrameRate", null));
            if (!string.IsNullOrWhiteSpace(frameRate))
                camera.VideoStream.FrameRate = frameRate;

            var bitRate = GetValueOrDefault(status, "bitRate", GetValueOrDefault(status, "videoBitRate", null));
            if (!string.IsNullOrWhiteSpace(bitRate))
                camera.VideoStream.BitRate = bitRate;
        }

        private static void UpdateCameraVideoInfoFromParams(Camera camera, Dictionary<string, object> status)
        {
            // Ensure VideoStream is initialized
            camera.VideoStream ??= new CameraVideoStream();

            var resolution = GetValueFromMultipleKeys(status,
                new[] { "root.Properties.Image.Resolution", "Properties.Image.Resolution" }, null);
            if (!string.IsNullOrWhiteSpace(resolution))
                camera.VideoStream.Resolution = resolution;

            var frameRate = GetValueFromMultipleKeys(status,
                new[] { "root.Properties.Image.FrameRate", "Properties.Image.FrameRate", "Image.FrameRate" }, null);
            if (!string.IsNullOrWhiteSpace(frameRate))
                camera.VideoStream.FrameRate = frameRate;

            var bitRate = GetValueFromMultipleKeys(status,
                new[] { "root.Properties.Image.Compression", "Properties.Image.Compression", "Image.Compression" }, null);

            if (!string.IsNullOrWhiteSpace(bitRate))
            {
                camera.VideoStream.BitRate = bitRate;
            }
            else
            {
                var format = GetValueFromMultipleKeys(status,
                    new[] { "root.Properties.Image.Format", "Properties.Image.Format" }, null);
                if (!string.IsNullOrWhiteSpace(format))
                    camera.VideoStream.BitRate = $"Formats: {format}";
            }
        }


        private static async Task<bool> SendHikvisionNetworkConfigWithLogging(
            Camera camera,
            HikvisionConnection connection,
            CancellationToken cancellationToken)
        {
            try
            {
                using var hikvisionConfig = new HikvisionConfiguration(connection);
                var (success, errorMessage) = await hikvisionConfig.UpdateNetworkSettingsAsync(camera);

                if (!success && !string.IsNullOrEmpty(errorMessage))
                {
                    camera.AddProtocolLog("Hikvision", "Network Config Error",
                        $"Hikvision configuration failed: {errorMessage}", ProtocolLogLevel.Error);
                    return false;
                }

                return success;
            }
            catch (Exception ex)
            {
                camera.AddProtocolLog("Hikvision", "Network Config Error",
                    $"Failed to send Hikvision network configuration: {ex.Message}", ProtocolLogLevel.Error);
                return false;
            }
        }

        private static IReadOnlyList<CameraProtocol> GetProtocolCheckOrder(CameraProtocol selectedProtocol)
        {
            if (selectedProtocol == CameraProtocol.Auto)
            {
                return DefaultProtocolOrder;
            }

            var supportedProtocols = ProtocolConnectionFactory.GetSupportedProtocols().ToList();
            var orderedProtocols = new List<CameraProtocol> { selectedProtocol };
            orderedProtocols.AddRange(supportedProtocols.Where(p => p != selectedProtocol));

            return orderedProtocols;
        }

        private static string? GetValueOrDefault(Dictionary<string, string> dictionary, string key, string? defaultValue)
        {
            if (dictionary.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
            return defaultValue;
        }

        private static string? GetValueFromMultipleKeys(Dictionary<string, object> dictionary, string[] keys, string? defaultValue)
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
            return defaultValue;
        }
    }
}