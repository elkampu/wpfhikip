using wpfhikip.Models;
using wpfhikip.Protocols.Common;

namespace wpfhikip.Protocols.Axis
{
    /// <summary>
    /// Adapter that wraps AxisConfiguration to implement IProtocolConfiguration
    /// </summary>
    public sealed class AxisConfigurationAdapter : IProtocolConfiguration
    {
        private readonly AxisConfiguration _configuration;
        private readonly AxisConnection _connection;
        private bool _disposed;

        public AxisConfigurationAdapter(AxisConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _configuration = new AxisConfiguration(connection);
        }

        public async Task<ProtocolOperationResult<Dictionary<string, object>>> GetDeviceInfoAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var objectData = new Dictionary<string, object>();

                // Get system parameters
                var (sysSuccess, sysParams, sysError) = await _configuration.GetSystemParametersAsync().ConfigureAwait(false);
                if (sysSuccess)
                {
                    foreach (var param in sysParams)
                    {
                        objectData[param.Key] = param.Value;
                    }
                }

                // Get device info
                var (devSuccess, deviceInfo, devError) = await _configuration.GetDeviceInfoAsync().ConfigureAwait(false);
                if (devSuccess)
                {
                    foreach (var info in deviceInfo)
                    {
                        objectData.TryAdd(info.Key, info.Value);
                    }
                }

                return sysSuccess || devSuccess
                    ? ProtocolOperationResult<Dictionary<string, object>>.CreateSuccess(objectData)
                    : ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(sysError ?? devError);
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

                // Get network parameters
                var (paramSuccess, netParams, paramError) = await _configuration.GetNetworkParametersAsync().ConfigureAwait(false);
                if (paramSuccess)
                {
                    foreach (var param in netParams)
                    {
                        objectData.TryAdd(param.Key, param.Value);
                    }
                }

                return netSuccess || paramSuccess
                    ? ProtocolOperationResult<Dictionary<string, object>>.CreateSuccess(objectData)
                    : ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(netError ?? paramError);
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
                using var operation = new AxisOperation(_connection);

                // Note: AxisOperation.GetCameraStatusAsync() doesn't take CancellationToken parameter
                var (success, status, error) = await operation.GetCameraStatusAsync().ConfigureAwait(false);

                return success
                    ? ProtocolOperationResult<Dictionary<string, object>>.CreateSuccess(status)
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