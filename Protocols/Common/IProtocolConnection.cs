using wpfhikip.Models;

namespace wpfhikip.Protocols.Common
{
    /// <summary>
    /// Common interface for all camera protocol connections
    /// </summary>
    public interface IProtocolConnection : IDisposable
    {
        string IpAddress { get; set; }
        int Port { get; set; }
        string Username { get; set; }
        string Password { get; set; }
        CameraProtocol ProtocolType { get; }

        /// <summary>
        /// Checks if the device is compatible with this protocol
        /// </summary>
        Task<ProtocolCompatibilityResult> CheckCompatibilityAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Tests authentication with the provided credentials
        /// </summary>
        Task<AuthenticationResult> TestAuthenticationAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends network configuration to the device
        /// </summary>
        Task<bool> SendNetworkConfigAsync(NetworkConfiguration config, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends NTP configuration to the device
        /// </summary>
        Task<bool> SendNTPConfigAsync(NTPConfiguration config, CancellationToken cancellationToken = default);
    }
}