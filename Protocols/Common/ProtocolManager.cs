using wpfhikip.Models;
using System.Diagnostics;

namespace wpfhikip.Protocols.Common
{
    /// <summary>
    /// Manages protocol detection and operations for cameras with enhanced logging
    /// </summary>
    public static class ProtocolManager
    {
        private const int DefaultTimeoutSeconds = 15;
        private static readonly ActivitySource ActivitySource = new("ProtocolManager");

        private static readonly IReadOnlyList<CameraProtocol> DefaultProtocolOrder = new[]
        {
            CameraProtocol.Hikvision,
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

                // Skip protocols that aren't supported
                if (!ProtocolFactoryRegistry.IsProtocolSupported(protocol))
                {
                    camera.AddProtocolLog(protocol.ToString(), "Skipped",
                        $"{protocol} protocol is not currently supported", ProtocolLogLevel.Warning);
                    continue;
                }

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
        /// Checks compatibility for a specific protocol with detailed logging
        /// </summary>
        public static async Task<ProtocolCompatibilityResult> CheckSingleProtocolAsync(
            Camera camera,
            CameraProtocol protocol,
            CancellationToken cancellationToken = default)
        {
            using var activity = ActivitySource.StartActivity("CheckSingleProtocol");
            activity?.SetTag("protocol", protocol.ToString());
            activity?.SetTag("ip.address", camera.CurrentIP);
            activity?.SetTag("port", camera.EffectivePort);

            // Enhanced logging for protocol validation
            camera.AddProtocolLog(protocol.ToString(), "Validation",
                $"Starting protocol validation for {protocol}", ProtocolLogLevel.Info);

            // Validate protocol support before attempting connection
            if (!ProtocolFactoryRegistry.IsProtocolSupported(protocol))
            {
                var message = $"{protocol} protocol is not currently supported";
                camera.AddProtocolLog(protocol.ToString(), "Not Supported", message, ProtocolLogLevel.Error);
                activity?.SetStatus(ActivityStatusCode.Error, "Protocol not supported");
                return ProtocolCompatibilityResult.CreateFailure(message, protocol);
            }

            camera.AddProtocolLog(protocol.ToString(), "Validation",
                $"Protocol {protocol} is supported - proceeding with compatibility check", ProtocolLogLevel.Info);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(DefaultTimeoutSeconds));
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var stopwatch = Stopwatch.StartNew();

            try
            {
                camera.AddProtocolLog(protocol.ToString(), "Connection",
                    $"Creating {protocol} connection to {camera.CurrentIP}:{camera.EffectivePort}", ProtocolLogLevel.Info);

                using var connection = ProtocolConnectionFactory.CreateConnection(
                    protocol,
                    camera.CurrentIP!,
                    camera.EffectivePort,
                    camera.Username ?? "admin",
                    camera.Password ?? "");

                camera.AddProtocolLog(protocol.ToString(), "Connection",
                    $"Connection created successfully - testing compatibility", ProtocolLogLevel.Info);

                var result = await connection.CheckCompatibilityAsync(combinedCts.Token).ConfigureAwait(false);

                stopwatch.Stop();

                // Log detailed results
                camera.AddProtocolLog(protocol.ToString(), "Result",
                    $"Compatibility check completed in {stopwatch.ElapsedMilliseconds}ms", ProtocolLogLevel.Info);

                camera.AddProtocolLog(protocol.ToString(), "Result",
                    $"Compatible: {result.IsCompatible}, RequiresAuth: {result.RequiresAuthentication}, " +
                    $"Authenticated: {result.IsAuthenticated}", ProtocolLogLevel.Info);

                if (!string.IsNullOrEmpty(result.AuthenticationMessage))
                {
                    camera.AddProtocolLog(protocol.ToString(), "Auth Message",
                        result.AuthenticationMessage,
                        result.IsAuthenticated ? ProtocolLogLevel.Info : ProtocolLogLevel.Warning);
                }

                activity?.SetTag("compatible", result.IsCompatible);
                activity?.SetTag("requires_auth", result.RequiresAuthentication);
                activity?.SetTag("authenticated", result.IsAuthenticated);

                return result;
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
            {
                stopwatch.Stop();
                var message = $"{protocol} protocol check timed out after {DefaultTimeoutSeconds} seconds";
                camera.AddProtocolLog(protocol.ToString(), "Timeout",
                    $"{message} (Total time: {stopwatch.ElapsedMilliseconds}ms)", ProtocolLogLevel.Warning);
                activity?.SetStatus(ActivityStatusCode.Error, "Timeout");
                throw;
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                camera.AddProtocolLog(protocol.ToString(), "Cancelled",
                    $"Protocol test was cancelled after {stopwatch.ElapsedMilliseconds}ms", ProtocolLogLevel.Warning);
                activity?.SetStatus(ActivityStatusCode.Error, "Cancelled");
                throw;
            }
            catch (NotSupportedException ex)
            {
                stopwatch.Stop();
                camera.AddProtocolLog(protocol.ToString(), "Not Supported",
                    $"Protocol not supported: {ex.Message} (Time: {stopwatch.ElapsedMilliseconds}ms)", ProtocolLogLevel.Error);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                return ProtocolCompatibilityResult.CreateFailure(ex.Message, protocol);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                camera.AddProtocolLog(protocol.ToString(), "Exception",
                    $"Protocol test failed after {stopwatch.ElapsedMilliseconds}ms: {ex.GetType().Name} - {ex.Message}", ProtocolLogLevel.Error);

                // Log stack trace for debugging
                if (ex.StackTrace != null)
                {
                    camera.AddProtocolLog(protocol.ToString(), "Stack Trace",
                        ex.StackTrace, ProtocolLogLevel.Error);
                }

                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                return ProtocolCompatibilityResult.CreateFailure(ex.Message, protocol);
            }
        }

        /// <summary>
        /// Loads comprehensive camera information with detailed progress logging
        /// </summary>
        public static async Task<bool> LoadCameraInfoAsync(
            Camera camera,
            CancellationToken cancellationToken = default)
        {
            using var activity = ActivitySource.StartActivity("LoadCameraInfo");
            activity?.SetTag("protocol", camera.Protocol.ToString());
            activity?.SetTag("ip.address", camera.CurrentIP);

            ArgumentNullException.ThrowIfNull(camera);

            camera.AddProtocolLog("System", "Info Load Start",
                $"Starting camera information load for {camera.Protocol} protocol", ProtocolLogLevel.Info);

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
                    $"Camera info load blocked - Compatible: {camera.IsCompatible}, " +
                    $"RequiresAuth: {camera.RequiresAuthentication}, Authenticated: {camera.IsAuthenticated}",
                    ProtocolLogLevel.Error);
                EnsureBasicStructure(camera);
                return false;
            }

            // Always initialize Settings and VideoStream first
            camera.Settings ??= new CameraSettings();
            camera.VideoStream ??= new CameraVideoStream();

            var overallStopwatch = Stopwatch.StartNew();

            try
            {
                camera.AddProtocolLog(camera.Protocol.ToString(), "Info Load",
                    $"Creating connection to {camera.CurrentIP}:{camera.EffectivePort} with user '{camera.Username ?? "admin"}'");

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

                camera.AddProtocolLog(camera.Protocol.ToString(), "Info Load",
                    "Creating configuration and operation instances", ProtocolLogLevel.Info);

                using var configuration = factory.CreateConfiguration(connection);
                using var operation = factory.CreateOperation(connection);

                // Load all information with individual timing
                var deviceTask = LoadDeviceInfoAsync(camera, configuration, cancellationToken);
                var networkTask = LoadNetworkInfoAsync(camera, configuration, cancellationToken);
                var videoTask = LoadVideoStreamInfoAsync(camera, configuration, operation, cancellationToken);

                camera.AddProtocolLog(camera.Protocol.ToString(), "Info Load",
                    "Starting concurrent load of device, network, and video information", ProtocolLogLevel.Info);

                var results = await Task.WhenAll(deviceTask, networkTask, videoTask).ConfigureAwait(false);

                overallStopwatch.Stop();

                var successCount = results.Count(r => r);

                camera.AddProtocolLog(camera.Protocol.ToString(), "Info Load Complete",
                    $"Information retrieval completed in {overallStopwatch.ElapsedMilliseconds}ms - " +
                    $"{successCount}/{results.Length} sections loaded successfully",
                    successCount > 0 ? ProtocolLogLevel.Success : ProtocolLogLevel.Warning);

                activity?.SetTag("success_count", successCount);
                activity?.SetTag("total_sections", results.Length);
                activity?.SetTag("duration_ms", overallStopwatch.ElapsedMilliseconds);

                return successCount > 0;
            }
            catch (OperationCanceledException)
            {
                overallStopwatch.Stop();
                camera.AddProtocolLog(camera.Protocol.ToString(), "Info Load",
                    $"Information loading was cancelled after {overallStopwatch.ElapsedMilliseconds}ms", ProtocolLogLevel.Warning);
                activity?.SetStatus(ActivityStatusCode.Error, "Cancelled");
                throw;
            }
            catch (NotSupportedException ex)
            {
                overallStopwatch.Stop();
                camera.AddProtocolLog(camera.Protocol.ToString(), "Info Load Error",
                    $"Protocol not supported after {overallStopwatch.ElapsedMilliseconds}ms: {ex.Message}", ProtocolLogLevel.Error);
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                overallStopwatch.Stop();
                camera.AddProtocolLog(camera.Protocol.ToString(), "Info Load Error",
                    $"Failed to load camera information after {overallStopwatch.ElapsedMilliseconds}ms: {ex.GetType().Name} - {ex.Message}", ProtocolLogLevel.Error);

                // Add stack trace for debugging
                if (ex.StackTrace != null)
                {
                    camera.AddProtocolLog(camera.Protocol.ToString(), "Stack Trace",
                        ex.StackTrace, ProtocolLogLevel.Error);
                }

                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
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
            catch (NotSupportedException ex)
            {
                camera.AddProtocolLog(camera.Protocol.ToString(), "Network Config Error",
                    $"Protocol not supported: {ex.Message}", ProtocolLogLevel.Error);
                return false;
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
            catch (NotSupportedException)
            {
                return false;
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
        /// Loads device information using the generic protocol configuration with enhanced logging
        /// </summary>
        private static async Task<bool> LoadDeviceInfoAsync(
            Camera camera,
            IProtocolConfiguration configuration,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                camera.AddProtocolLog(camera.Protocol.ToString(), "Device Info",
                    "Starting device information retrieval...");

                var (success, deviceData, errorMessage) = await configuration.GetDeviceInfoAsync(cancellationToken).ConfigureAwait(false);

                stopwatch.Stop();

                if (success && deviceData != null)
                {
                    camera.AddProtocolLog(camera.Protocol.ToString(), "Device Info",
                        $"Retrieved {deviceData.Count} device parameters in {stopwatch.ElapsedMilliseconds}ms", ProtocolLogLevel.Success);

                    // Log individual data items for debugging
                    foreach (var item in deviceData.Take(10)) // Limit to first 10 items
                    {
                        camera.AddProtocolLog(camera.Protocol.ToString(), "Device Data",
                            $"{item.Key}: {item.Value}", ProtocolLogLevel.Info);
                    }

                    if (deviceData.Count > 10)
                    {
                        camera.AddProtocolLog(camera.Protocol.ToString(), "Device Data",
                            $"... and {deviceData.Count - 10} more items", ProtocolLogLevel.Info);
                    }

                    CameraDataProcessor.UpdateDeviceInfo(camera, deviceData, camera.Protocol.ToString());
                    return true;
                }
                else
                {
                    camera.AddProtocolLog(camera.Protocol.ToString(), "Device Info",
                        $"Failed to load device information in {stopwatch.ElapsedMilliseconds}ms: {errorMessage}", ProtocolLogLevel.Warning);
                    return false;
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                camera.AddProtocolLog(camera.Protocol.ToString(), "Device Info Error",
                    $"Error loading device information after {stopwatch.ElapsedMilliseconds}ms: {ex.GetType().Name} - {ex.Message}", ProtocolLogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// Loads network configuration information using the generic protocol configuration with enhanced logging
        /// </summary>
        private static async Task<bool> LoadNetworkInfoAsync(
            Camera camera,
            IProtocolConfiguration configuration,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                camera.AddProtocolLog(camera.Protocol.ToString(), "Network Info",
                    "Starting network information retrieval...");

                var (success, networkData, errorMessage) = await configuration.GetNetworkInfoAsync(cancellationToken).ConfigureAwait(false);

                stopwatch.Stop();

                if (success && networkData != null)
                {
                    camera.AddProtocolLog(camera.Protocol.ToString(), "Network Info",
                        $"Retrieved {networkData.Count} network parameters in {stopwatch.ElapsedMilliseconds}ms", ProtocolLogLevel.Success);

                    // Log network data for debugging
                    foreach (var item in networkData)
                    {
                        camera.AddProtocolLog(camera.Protocol.ToString(), "Network Data",
                            $"{item.Key}: {item.Value}", ProtocolLogLevel.Info);
                    }

                    CameraDataProcessor.UpdateNetworkInfo(camera, networkData);
                    return true;
                }
                else
                {
                    camera.AddProtocolLog(camera.Protocol.ToString(), "Network Info",
                        $"Failed to load network information in {stopwatch.ElapsedMilliseconds}ms: {errorMessage}", ProtocolLogLevel.Warning);
                    return false;
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                camera.AddProtocolLog(camera.Protocol.ToString(), "Network Info Error",
                    $"Error loading network information after {stopwatch.ElapsedMilliseconds}ms: {ex.GetType().Name} - {ex.Message}", ProtocolLogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// Loads video stream information using the generic protocol configuration and operation with enhanced logging
        /// </summary>
        private static async Task<bool> LoadVideoStreamInfoAsync(
            Camera camera,
            IProtocolConfiguration configuration,
            IProtocolOperation operation,
            CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                camera.AddProtocolLog(camera.Protocol.ToString(), "Video Info",
                    "Starting video stream information retrieval...");

                // Set stream URLs first
                var mainStreamUrl = operation.GetMainStreamUrl(1);
                var subStreamUrl = operation.GetSubStreamUrl(1);

                camera.VideoStream.MainStreamUrl = mainStreamUrl;
                camera.VideoStream.SubStreamUrl = subStreamUrl;

                camera.AddProtocolLog(camera.Protocol.ToString(), "Video URLs",
                    $"Main: {mainStreamUrl}, Sub: {subStreamUrl}", ProtocolLogLevel.Info);

                // Get detailed video information
                var (success, videoData, errorMessage) = await configuration.GetVideoInfoAsync(cancellationToken).ConfigureAwait(false);

                stopwatch.Stop();

                if (success && videoData != null)
                {
                    camera.AddProtocolLog(camera.Protocol.ToString(), "Video Info",
                        $"Retrieved {videoData.Count} video parameters in {stopwatch.ElapsedMilliseconds}ms", ProtocolLogLevel.Success);

                    // Log video data for debugging
                    foreach (var item in videoData)
                    {
                        camera.AddProtocolLog(camera.Protocol.ToString(), "Video Data",
                            $"{item.Key}: {item.Value}", ProtocolLogLevel.Info);
                    }

                    CameraDataProcessor.UpdateVideoInfo(camera, videoData);
                }
                else if (!success)
                {
                    camera.AddProtocolLog(camera.Protocol.ToString(), "Video Info",
                        $"Failed to get detailed video info in {stopwatch.ElapsedMilliseconds}ms: {errorMessage}", ProtocolLogLevel.Warning);
                }

                return true; // Always return true since we at least set the stream URLs
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                camera.AddProtocolLog(camera.Protocol.ToString(), "Video Info Error",
                    $"Error loading video information after {stopwatch.ElapsedMilliseconds}ms: {ex.GetType().Name} - {ex.Message}", ProtocolLogLevel.Error);

                // Set basic fallback URLs
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