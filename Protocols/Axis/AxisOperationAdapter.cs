using wpfhikip.Protocols.Common;

namespace wpfhikip.Protocols.Axis
{
    /// <summary>
    /// Adapter that wraps AxisOperation to implement IProtocolOperation
    /// </summary>
    public sealed class AxisOperationAdapter : IProtocolOperation
    {
        private readonly AxisOperation _operation;
        private bool _disposed;

        public AxisOperationAdapter(AxisConnection connection)
        {
            _operation = new AxisOperation(connection ?? throw new ArgumentNullException(nameof(connection)));
        }

        public string GetMainStreamUrl(int channel = 1)
        {
            return _operation.GetMjpegStreamUrl(channel, 1920);
        }

        public string GetSubStreamUrl(int channel = 1)
        {
            return _operation.GetMjpegStreamUrl(channel, 704);
        }

        public async Task<ProtocolOperationResult<Dictionary<string, object>>> GetCameraStatusAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var (success, status, error) = await _operation.GetCameraStatusAsync().ConfigureAwait(false);
                
                return success 
                    ? ProtocolOperationResult<Dictionary<string, object>>.CreateSuccess(status)
                    : ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(error);
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