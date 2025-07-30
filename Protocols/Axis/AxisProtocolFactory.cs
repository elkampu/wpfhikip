using wpfhikip.Models;
using wpfhikip.Protocols.Common;

namespace wpfhikip.Protocols.Axis
{
    /// <summary>
    /// Factory for creating Axis protocol instances
    /// </summary>
    public sealed class AxisProtocolFactory : IProtocolFactory
    {
        public CameraProtocol SupportedProtocol => CameraProtocol.Axis;

        public IProtocolConfiguration CreateConfiguration(IProtocolConnection connection)
        {
            ArgumentNullException.ThrowIfNull(connection);
            
            if (connection is not AxisConnection axisConnection)
                throw new ArgumentException($"Expected {nameof(AxisConnection)}, got {connection.GetType().Name}", nameof(connection));

            return new AxisConfigurationAdapter(axisConnection);
        }

        public IProtocolOperation CreateOperation(IProtocolConnection connection)
        {
            ArgumentNullException.ThrowIfNull(connection);
            
            if (connection is not AxisConnection axisConnection)
                throw new ArgumentException($"Expected {nameof(AxisConnection)}, got {connection.GetType().Name}", nameof(connection));

            return new AxisOperationAdapter(axisConnection);
        }
    }
}