using wpfhikip.Models;
using wpfhikip.Protocols.Common;

namespace wpfhikip.Protocols.Onvif
{
    /// <summary>
    /// Adapter that wraps OnvifOperation to implement IProtocolOperation
    /// </summary>
    public sealed class OnvifOperationAdapter : IProtocolOperation
    {
        private readonly OnvifConnection _connection;
        private bool _disposed;

        public OnvifOperationAdapter(OnvifConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        public string GetMainStreamUrl(int channel = 1)
        {
            // ONVIF stream URLs are typically discovered through GetProfiles and GetStreamUri calls
            // For now, return a basic RTSP URL format that can be refined with proper ONVIF media service calls
            var port = _connection.Port == 80 ? 554 : _connection.Port; // Default to RTSP port if using HTTP port
            return $"rtsp://{_connection.Username}:{_connection.Password}@{_connection.IpAddress}:{port}/onvif1";
        }

        public string GetSubStreamUrl(int channel = 1)
        {
            // Similar to main stream but typically a lower quality profile
            var port = _connection.Port == 80 ? 554 : _connection.Port;
            return $"rtsp://{_connection.Username}:{_connection.Password}@{_connection.IpAddress}:{port}/onvif2";
        }

        public async Task<ProtocolOperationResult<Dictionary<string, object>>> GetCameraStatusAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var operation = new OnvifOperation(_connection);

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

        public async Task<ProtocolOperationResult<Dictionary<string, object>>> GetVideoConfigurationAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var operation = new OnvifOperation(_connection);

                var (success, config, error) = await operation.GetVideoConfigurationAsync().ConfigureAwait(false);

                return success
                    ? ProtocolOperationResult<Dictionary<string, object>>.CreateSuccess(config)
                    : ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(error);
            }
            catch (Exception ex)
            {
                return ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(ex.Message);
            }
        }

        public async Task<ProtocolOperationResult<bool>> RebootCameraAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                using var operation = new OnvifOperation(_connection);

                var (success, error) = await operation.RebootCameraAsync().ConfigureAwait(false);

                return success
                    ? ProtocolOperationResult<bool>.CreateSuccess(true)
                    : ProtocolOperationResult<bool>.CreateFailure(error);
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
                // OnvifOperation is disposed via using statements in methods
                _disposed = true;
            }
        }
    }
}