using wpfhikip.Models;
using wpfhikip.Protocols.Common;

namespace wpfhikip.Protocols.Hikvision
{
    /// <summary>
    /// Adapter that wraps HikvisionConfiguration to implement IProtocolConfiguration
    /// </summary>
    public sealed class HikvisionConfigurationAdapter : IProtocolConfiguration
    {
        private readonly HikvisionConfiguration _configuration;
        private readonly HikvisionConnection _connection;
        private bool _disposed;

        public HikvisionConfigurationAdapter(HikvisionConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _configuration = new HikvisionConfiguration(connection);
        }

        public async Task<ProtocolOperationResult<Dictionary<string, object>>> GetDeviceInfoAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var (success, deviceInfo, error) = await _configuration.GetDeviceInfoAsync().ConfigureAwait(false);
                
                if (!success)
                    return ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(error);

                // Convert string dictionary to object dictionary
                var objectData = deviceInfo.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);
                
                // Also get capabilities for additional device info
                var (capSuccess, capabilities, _) = await _configuration.GetSystemCapabilitiesAsync().ConfigureAwait(false);
                if (capSuccess)
                {
                    foreach (var cap in capabilities)
                    {
                        objectData.TryAdd(cap.Key, cap.Value);
                    }
                }

                return ProtocolOperationResult<Dictionary<string, object>>.CreateSuccess(objectData);
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
                var (success, networkXml, error) = await _configuration.GetConfigurationAsync(HikvisionUrl.NetworkInterfaceIpAddress).ConfigureAwait(false);
                
                if (!success)
                    return ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(error);

                var networkInfo = HikvisionXmlTemplates.ParseResponseXml(networkXml);
                var objectData = networkInfo.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);

                return ProtocolOperationResult<Dictionary<string, object>>.CreateSuccess(objectData);
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
                using var operation = new HikvisionOperation(_connection);
                
                // Get streaming channel information
                var (streamSuccess, streamingInfo, streamError) = await operation.GetStreamingChannelInfoAsync(1).ConfigureAwait(false);
                var objectData = new Dictionary<string, object>();

                if (streamSuccess)
                {
                    foreach (var item in streamingInfo)
                    {
                        objectData[item.Key] = item.Value;
                    }
                }

                // Also try to get camera status for additional video information
                var (statusSuccess, status, _) = await operation.GetCameraStatusAsync().ConfigureAwait(false);
                if (statusSuccess)
                {
                    foreach (var item in status)
                    {
                        objectData.TryAdd(item.Key, item.Value);
                    }
                }

                return streamSuccess || statusSuccess 
                    ? ProtocolOperationResult<Dictionary<string, object>>.CreateSuccess(objectData)
                    : ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(streamError);
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
                var (success, errorMessage) = await _configuration.UpdateNetworkSettingsAsync(camera).ConfigureAwait(false);
                
                return success 
                    ? ProtocolOperationResult<bool>.CreateSuccess(true)
                    : ProtocolOperationResult<bool>.CreateFailure(errorMessage);
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