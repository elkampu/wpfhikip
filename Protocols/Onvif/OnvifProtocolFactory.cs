using wpfhikip.Models;
using wpfhikip.Protocols.Common;

namespace wpfhikip.Protocols.Onvif
{
    /// <summary>
    /// Factory for creating ONVIF protocol components
    /// </summary>
    public sealed class OnvifProtocolFactory : IProtocolFactory
    {
        public CameraProtocol SupportedProtocol => CameraProtocol.Onvif;

        public IProtocolConnection CreateConnection(string ipAddress, int port, string username, string password)
        {
            return new OnvifConnection(ipAddress, port, username, password);
        }

        public IProtocolConfiguration CreateConfiguration(IProtocolConnection connection)
        {
            if (connection is not OnvifConnection onvifConnection)
            {
                throw new ArgumentException($"Expected OnvifConnection, got {connection.GetType().Name}", nameof(connection));
            }

            return new OnvifConfigurationAdapter(onvifConnection);
        }

        public IProtocolOperation CreateOperation(IProtocolConnection connection)
        {
            if (connection is not OnvifConnection onvifConnection)
            {
                throw new ArgumentException($"Expected OnvifConnection, got {connection.GetType().Name}", nameof(connection));
            }

            return new OnvifOperationAdapter(onvifConnection);
        }
    }
}