using wpfhikip.Models;
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
        /// Sends Hikvision network configuration with detailed logging to the camera
        /// </summary>
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
    }
}