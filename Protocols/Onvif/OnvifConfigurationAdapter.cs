using wpfhikip.Models;
using wpfhikip.Protocols.Common;

namespace wpfhikip.Protocols.Onvif
{
    /// <summary>
    /// Adapter that wraps OnvifConnection and OnvifConfiguration to implement IProtocolConfiguration
    /// </summary>
    public sealed class OnvifConfigurationAdapter : IProtocolConfiguration
    {
        private readonly OnvifConnection _connection;
        private readonly OnvifConfiguration _configuration;
        private bool _disposed;

        public OnvifConfigurationAdapter(OnvifConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _configuration = new OnvifConfiguration(connection);
        }

        public async Task<ProtocolOperationResult<Dictionary<string, object>>> GetDeviceInfoAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var objectData = new Dictionary<string, object>();

                // Get device information
                var (devSuccess, deviceInfo, devError) = await _configuration.GetDeviceInfoAsync().ConfigureAwait(false);
                if (devSuccess)
                {
                    foreach (var info in deviceInfo)
                    {
                        objectData[info.Key] = info.Value;
                    }
                }

                // Get capabilities
                var (capSuccess, capabilities, capError) = await _configuration.GetCapabilitiesAsync().ConfigureAwait(false);
                if (capSuccess)
                {
                    foreach (var capability in capabilities)
                    {
                        objectData.TryAdd(capability.Key, capability.Value);
                    }
                }

                return devSuccess || capSuccess
                    ? ProtocolOperationResult<Dictionary<string, object>>.CreateSuccess(objectData)
                    : ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(devError ?? capError);
            }
            catch (Exception ex)
            {
                return ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(ex.Message);
            }
        }

        public async Task<ProtocolOperationResult<Dictionary<string, object>>> GetNetworkInfoAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var objectData = new Dictionary<string, object>();

                // Get network configuration
                var (netSuccess, networkConfig, netError) = await _configuration.GetNetworkConfigurationAsync().ConfigureAwait(false);
                if (netSuccess)
                {
                    foreach (var config in networkConfig)
                    {
                        objectData[config.Key] = config.Value;
                    }
                }

                // Get NTP configuration
                var (ntpSuccess, ntpConfig, ntpError) = await _configuration.GetNtpConfigurationAsync().ConfigureAwait(false);
                if (ntpSuccess)
                {
                    foreach (var config in ntpConfig)
                    {
                        objectData.TryAdd(config.Key, config.Value);
                    }
                }

                return netSuccess || ntpSuccess
                    ? ProtocolOperationResult<Dictionary<string, object>>.CreateSuccess(objectData)
                    : ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(netError ?? ntpError);
            }
            catch (Exception ex)
            {
                return ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(ex.Message);
            }
        }

        public async Task<ProtocolOperationResult<Dictionary<string, object>>> GetVideoInfoAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var operation = new OnvifOperation(_connection);

                var (success, videoInfo, error) = await operation.GetVideoConfigurationAsync().ConfigureAwait(false);

                return success
                    ? ProtocolOperationResult<Dictionary<string, object>>.CreateSuccess(videoInfo)
                    : ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(error);
            }
            catch (Exception ex)
            {
                return ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(ex.Message);
            }
        }

        public async Task<ProtocolOperationResult<bool>> SetNetworkConfigurationAsync(Camera camera, CancellationToken cancellationToken = default)
        {
            try
            {
                var result = await _configuration.UpdateNetworkConfigurationAsync(camera).ConfigureAwait(false);

                return result.Success
                    ? ProtocolOperationResult<bool>.CreateSuccess(true)
                    : ProtocolOperationResult<bool>.CreateFailure(result.Message);
            }
            catch (Exception ex)
            {
                return ProtocolOperationResult<bool>.CreateFailure(ex.Message);
            }
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