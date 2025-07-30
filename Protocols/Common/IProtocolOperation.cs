namespace wpfhikip.Protocols.Common
{
    /// <summary>
    /// Interface for protocol-specific operational methods
    /// </summary>
    public interface IProtocolOperation : IDisposable
    {
        /// <summary>
        /// Gets the main stream URL for the camera
        /// </summary>
        string GetMainStreamUrl(int channel = 1);

        /// <summary>
        /// Gets the sub stream URL for the camera
        /// </summary>
        string GetSubStreamUrl(int channel = 1);

        /// <summary>
        /// Gets camera status information
        /// </summary>
        Task<ProtocolOperationResult<Dictionary<string, object>>> GetCameraStatusAsync(CancellationToken cancellationToken = default);
    }
}