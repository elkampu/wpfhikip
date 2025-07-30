using wpfhikip.Models;

namespace wpfhikip.Protocols.Common
{
    /// <summary>
    /// Factory interface for creating protocol-specific instances
    /// </summary>
    public interface IProtocolFactory
    {
        /// <summary>
        /// Creates a configuration instance for the protocol
        /// </summary>
        IProtocolConfiguration CreateConfiguration(IProtocolConnection connection);

        /// <summary>
        /// Creates an operation instance for the protocol
        /// </summary>
        IProtocolOperation CreateOperation(IProtocolConnection connection);

        /// <summary>
        /// Gets the protocol type this factory supports
        /// </summary>
        CameraProtocol SupportedProtocol { get; }
    }
}