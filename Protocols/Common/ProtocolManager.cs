using wpfhikip.Models;

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

                var result = await CheckSingleProtocolAsync(camera, protocol, cancellationToken).ConfigureAwait(false);

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

                return await connection.CheckCompatibilityAsync(combinedCts.Token).ConfigureAwait(false);
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

            if (!ProtocolFactoryRegistry.IsProtocolSupported(camera.Protocol))
            {
                camera.AddProtocolLog("System", "Info Load",
                    $"Protocol {camera.Protocol} is not supported for information retrieval", ProtocolLogLevel.Error);
                EnsureBasicStructure(camera);
                return false;
            }

            if (!camera.CanShowCameraInfo)
            {
                camera.AddProtocolLog("System", "Info Load",
                    "Camera must be compatible and authenticated to load information", ProtocolLogLevel.Error);
                EnsureBasicStructure(camera);
                return false;
            }

            // Always initialize Settings and VideoStream first
            camera.Settings ??= new CameraSettings();
            camera.VideoStream ??= new CameraVideoStream();

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

                var factory = ProtocolFactoryRegistry.GetFactory(camera.Protocol);
                if (factory == null)
                {
                    camera.AddProtocolLog(camera.Protocol.ToString(), "Factory Error",
                        $"No factory available for protocol {camera.Protocol}", ProtocolLogLevel.Error);
                    return false;
                }

                using var configuration = factory.CreateConfiguration(connection);
                using var operation = factory.CreateOperation(connection);

                // Load all information concurrently for better performance
                var loadTasks = new[]
                {
                    LoadDeviceInfoAsync(camera, configuration, cancellationToken),
                    LoadNetworkInfoAsync(camera, configuration, cancellationToken),
                    LoadVideoStreamInfoAsync(camera, configuration, operation, cancellationToken)
                };

                var results = await Task.WhenAll(loadTasks).ConfigureAwait(false);
                var successCount = results.Count(r => r);

                camera.AddProtocolLog(camera.Protocol.ToString(), "Info Load",
                    $"Camera information retrieval completed - {successCount}/{results.Length} sections loaded successfully",
                    successCount > 0 ? ProtocolLogLevel.Info : ProtocolLogLevel.Warning);

                return successCount > 0;
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
                return false;
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

            if (!ProtocolFactoryRegistry.IsProtocolSupported(camera.Protocol))
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

                var factory = ProtocolFactoryRegistry.GetFactory(camera.Protocol);
                if (factory == null)
                {
                    camera.AddProtocolLog(camera.Protocol.ToString(), "Network Config",
                        $"No factory available for protocol {camera.Protocol}", ProtocolLogLevel.Error);
                    return false;
                }

                camera.AddProtocolLog(camera.Protocol.ToString(), "Network Config",
                    $"Sending configuration: IP={camera.NewIP}, Mask={camera.NewMask}, Gateway={camera.NewGateway}, DNS1={camera.NewDNS1}, DNS2={camera.NewDNS2}");

                using var configuration = factory.CreateConfiguration(connection);
                var (success, _, errorMessage) = await configuration.SetNetworkConfigurationAsync(camera, cancellationToken).ConfigureAwait(false);

                camera.AddProtocolLog(camera.Protocol.ToString(), "Network Config",
                    success ? "Network configuration sent successfully" : $"Failed to send network configuration: {errorMessage}",
                    success ? ProtocolLogLevel.Success : ProtocolLogLevel.Error);

                return success;
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

            if (!ProtocolFactoryRegistry.IsProtocolSupported(camera.Protocol) ||
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

                return await connection.SendNTPConfigAsync(config, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Ensures basic object structure is initialized without setting default values
        /// </summary>
        private static void EnsureBasicStructure(Camera camera)
        {
            camera.Settings ??= new CameraSettings();
            camera.VideoStream ??= new CameraVideoStream();
        }

        /// <summary>
        /// Loads device information using the generic protocol configuration
        /// </summary>
        private static async Task<bool> LoadDeviceInfoAsync(
            Camera camera,
            IProtocolConfiguration configuration,
            CancellationToken cancellationToken)
        {
            try
            {
                camera.AddProtocolLog(camera.Protocol.ToString(), "Device Info",
                    "Loading device information...");

                var (success, deviceData, errorMessage) = await configuration.GetDeviceInfoAsync(cancellationToken).ConfigureAwait(false);

                if (success && deviceData != null)
                {
                    camera.AddProtocolLog(camera.Protocol.ToString(), "Device Info",
                        $"Retrieved {deviceData.Count} device parameters", ProtocolLogLevel.Info);

                    CameraDataProcessor.UpdateDeviceInfo(camera, deviceData, camera.Protocol.ToString());
                    return true;
                }
                else
                {
                    camera.AddProtocolLog(camera.Protocol.ToString(), "Device Info",
                        $"Failed to load device information: {errorMessage}", ProtocolLogLevel.Warning);
                    return false;
                }
            }
            catch (Exception ex)
            {
                camera.AddProtocolLog(camera.Protocol.ToString(), "Device Info Error",
                    $"Error loading device information: {ex.Message}", ProtocolLogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// Loads network configuration information using the generic protocol configuration
        /// </summary>
        private static async Task<bool> LoadNetworkInfoAsync(
            Camera camera,
            IProtocolConfiguration configuration,
            CancellationToken cancellationToken)
        {
            try
            {
                camera.AddProtocolLog(camera.Protocol.ToString(), "Network Info",
                    "Loading network information...");

                var (success, networkData, errorMessage) = await configuration.GetNetworkInfoAsync(cancellationToken).ConfigureAwait(false);

                if (success && networkData != null)
                {
                    camera.AddProtocolLog(camera.Protocol.ToString(), "Network Info",
                        $"Retrieved {networkData.Count} network parameters", ProtocolLogLevel.Info);

                    CameraDataProcessor.UpdateNetworkInfo(camera, networkData);
                    return true;
                }
                else
                {
                    camera.AddProtocolLog(camera.Protocol.ToString(), "Network Info",
                        $"Failed to load network information: {errorMessage}", ProtocolLogLevel.Warning);
                    return false;
                }
            }
            catch (Exception ex)
            {
                camera.AddProtocolLog(camera.Protocol.ToString(), "Network Info Error",
                    $"Error loading network information: {ex.Message}", ProtocolLogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// Loads video stream information using the generic protocol configuration and operation
        /// </summary>
        private static async Task<bool> LoadVideoStreamInfoAsync(
            Camera camera,
            IProtocolConfiguration configuration,
            IProtocolOperation operation,
            CancellationToken cancellationToken)
        {
            try
            {
                camera.AddProtocolLog(camera.Protocol.ToString(), "Video Info",
                    "Loading video stream information...");

                // Set stream URLs
                camera.VideoStream.MainStreamUrl = operation.GetMainStreamUrl(1);
                camera.VideoStream.SubStreamUrl = operation.GetSubStreamUrl(1);

                // Get detailed video information
                var (success, videoData, errorMessage) = await configuration.GetVideoInfoAsync(cancellationToken).ConfigureAwait(false);

                if (success && videoData != null)
                {
                    camera.AddProtocolLog(camera.Protocol.ToString(), "Video Info",
                        $"Retrieved {videoData.Count} video parameters", ProtocolLogLevel.Info);

                    CameraDataProcessor.UpdateVideoInfo(camera, videoData);
                }
                else if (!success)
                {
                    camera.AddProtocolLog(camera.Protocol.ToString(), "Video Info",
                        $"Failed to get detailed video info: {errorMessage}", ProtocolLogLevel.Warning);

                    // Set basic fallback URLs only (already set above)
                }

                return true; // Always return true since we at least set the stream URLs
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

                return false;
            }
        }

        private static IReadOnlyList<CameraProtocol> GetProtocolCheckOrder(CameraProtocol selectedProtocol)
        {
            if (selectedProtocol == CameraProtocol.Auto)
            {
                return DefaultProtocolOrder;
            }

            var supportedProtocols = ProtocolFactoryRegistry.GetSupportedProtocols().ToList();
            var orderedProtocols = new List<CameraProtocol> { selectedProtocol };
            orderedProtocols.AddRange(supportedProtocols.Where(p => p != selectedProtocol));

            return orderedProtocols;
        }
    }
}