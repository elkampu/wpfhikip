using wpfhikip.Models;
using wpfhikip.Protocols.Common;

namespace wpfhikip.Protocols.Dahua
{
    /// <summary>
    /// Factory for creating Dahua protocol components
    /// </summary>
    public sealed class DahuaProtocolFactory : IProtocolFactory
    {
        public CameraProtocol SupportedProtocol => CameraProtocol.Dahua;

        public IProtocolConnection CreateConnection(string ipAddress, int port, string username, string password)
        {
            return new DahuaConnection(ipAddress, port, username, password);
        }

        public IProtocolConfiguration CreateConfiguration(IProtocolConnection connection)
        {
            if (connection is not DahuaConnection dahuaConnection)
            {
                throw new ArgumentException($"Expected DahuaConnection, got {connection.GetType().Name}", nameof(connection));
            }

            return new DahuaConfiguration(dahuaConnection);
        }

        public IProtocolOperation CreateOperation(IProtocolConnection connection)
        {
            if (connection is not DahuaConnection dahuaConnection)
            {
                throw new ArgumentException($"Expected DahuaConnection, got {connection.GetType().Name}", nameof(connection));
            }

            return new DahuaOperation(dahuaConnection);
        }
    }
}