using wpfhikip.Protocols.Common;

namespace wpfhikip.Protocols.Hikvision
{
    /// <summary>
    /// Adapter that wraps HikvisionOperation to implement IProtocolOperation
    /// </summary>
    public sealed class HikvisionOperationAdapter : IProtocolOperation
    {
        private readonly HikvisionOperation _operation;
        private bool _disposed;

        public HikvisionOperationAdapter(HikvisionConnection connection)
        {
            _operation = new HikvisionOperation(connection ?? throw new ArgumentNullException(nameof(connection)));
        }

        public string GetMainStreamUrl(int channel = 1)
        {
            return _operation.GetRtspStreamUrl(channel, 1);
        }

        public string GetSubStreamUrl(int channel = 1)
        {
            return _operation.GetRtspStreamUrl(channel, 2);
        }

        public async Task<ProtocolOperationResult<Dictionary<string, object>>> GetCameraStatusAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var (success, status, error) = await _operation.GetCameraStatusAsync().ConfigureAwait(false);
                
                if (!success)
                    return ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(error);

                var objectData = status.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);
                return ProtocolOperationResult<Dictionary<string, object>>.CreateSuccess(objectData);
            }
            catch (Exception ex)
            {
                return ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(ex.Message);
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _operation?.Dispose();
                _disposed = true;
            }
        }
    }
}