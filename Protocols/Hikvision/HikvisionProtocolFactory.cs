using wpfhikip.Models;
using wpfhikip.Protocols.Common;

namespace wpfhikip.Protocols.Hikvision
{
    /// <summary>
    /// Factory for creating Hikvision protocol instances
    /// </summary>
    public sealed class HikvisionProtocolFactory : IProtocolFactory
    {
        public CameraProtocol SupportedProtocol => CameraProtocol.Hikvision;

        public IProtocolConfiguration CreateConfiguration(IProtocolConnection connection)
        {
            ArgumentNullException.ThrowIfNull(connection);

            if (connection is not HikvisionConnection hikvisionConnection)
                throw new ArgumentException($"Expected {nameof(HikvisionConnection)}, got {connection.GetType().Name}", nameof(connection));

            return new HikvisionConfigurationAdapter(hikvisionConnection);
        }

        public IProtocolOperation CreateOperation(IProtocolConnection connection)
        {
            ArgumentNullException.ThrowIfNull(connection);

            if (connection is not HikvisionConnection hikvisionConnection)
                throw new ArgumentException($"Expected {nameof(HikvisionConnection)}, got {connection.GetType().Name}", nameof(connection));

            return new HikvisionOperationAdapter(hikvisionConnection);
        }
    }
}