using wpfhikip.Models;

namespace wpfhikip.Protocols.Common
{
    /// <summary>
    /// Interface for protocol-specific configuration operations
    /// </summary>
    public interface IProtocolConfiguration : IDisposable
    {
        /// <summary>
        /// Gets device information from the camera
        /// </summary>
        Task<ProtocolOperationResult<Dictionary<string, object>>> GetDeviceInfoAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets network configuration information from the camera
        /// </summary>
        Task<ProtocolOperationResult<Dictionary<string, object>>> GetNetworkInfoAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets video stream information from the camera
        /// </summary>
        Task<ProtocolOperationResult<Dictionary<string, object>>> GetVideoInfoAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends network configuration to the camera
        /// </summary>
        Task<ProtocolOperationResult<bool>> SetNetworkConfigurationAsync(Camera camera, CancellationToken cancellationToken = default);
    }
}