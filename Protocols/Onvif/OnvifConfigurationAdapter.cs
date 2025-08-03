using wpfhikip.Models;
using wpfhikip.Protocols.Common;

namespace wpfhikip.Protocols.Onvif
{
    /// <summary>
    /// Adapter that wraps OnvifConfiguration to implement IProtocolConfiguration
    /// This adapter is now redundant since OnvifConfiguration implements IProtocolConfiguration directly
    /// </summary>
    public sealed class OnvifConfigurationAdapter : IProtocolConfiguration
    {
        private readonly OnvifConfiguration _configuration;
        private bool _disposed;

        public OnvifConfigurationAdapter(OnvifConnection connection)
        {
            _configuration = new OnvifConfiguration(connection ?? throw new ArgumentNullException(nameof(connection)));
        }

        public async Task<ProtocolOperationResult<Dictionary<string, object>>> GetDeviceInfoAsync(CancellationToken cancellationToken = default)
        {
            return await _configuration.GetDeviceInfoAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<ProtocolOperationResult<Dictionary<string, object>>> GetNetworkInfoAsync(CancellationToken cancellationToken = default)
        {
            return await _configuration.GetNetworkInfoAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<ProtocolOperationResult<Dictionary<string, object>>> GetVideoInfoAsync(CancellationToken cancellationToken = default)
        {
            return await _configuration.GetVideoInfoAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<ProtocolOperationResult<bool>> SetNetworkConfigurationAsync(Camera camera, CancellationToken cancellationToken = default)
        {
            return await _configuration.SetNetworkConfigurationAsync(camera, cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _configuration?.Dispose();
                _disposed = true;
            }
        }
    }
}