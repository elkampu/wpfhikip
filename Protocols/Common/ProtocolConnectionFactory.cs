using System.Collections.Frozen;
using wpfhikip.Models;
using wpfhikip.Protocols.Hikvision;
using wpfhikip.Protocols.Dahua;
using wpfhikip.Protocols.Axis;
using wpfhikip.Protocols.Onvif;

namespace wpfhikip.Protocols.Common
{
    /// <summary>
    /// Factory for creating protocol connection instances
    /// </summary>
    public static class ProtocolConnectionFactory
    {
        private static readonly FrozenDictionary<CameraProtocol, Func<string, int, string, string, IProtocolConnection>> ConnectionFactories =
            new Dictionary<CameraProtocol, Func<string, int, string, string, IProtocolConnection>>
            {
                { CameraProtocol.Hikvision, (ip, port, user, pass) => new HikvisionConnection(ip, port, user, pass) },
                { CameraProtocol.Dahua, (ip, port, user, pass) => new DahuaConnection(ip, port, user, pass) },
                { CameraProtocol.Axis, (ip, port, user, pass) => new AxisConnection(ip, port, user, pass) },
                { CameraProtocol.Onvif, (ip, port, user, pass) => new OnvifConnection(ip, port, user, pass) }
            }.ToFrozenDictionary();

        /// <summary>
        /// Creates a protocol connection instance for the specified protocol
        /// </summary>
        public static IProtocolConnection CreateConnection(CameraProtocol protocol, string ipAddress, int port, string username, string password)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(ipAddress);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(port);

            if (ConnectionFactories.TryGetValue(protocol, out var factory))
            {
                return factory(ipAddress, port, username ?? "", password ?? "");
            }

            throw new NotSupportedException($"Protocol {protocol} is not supported");
        }

        /// <summary>
        /// Gets all supported protocols
        /// </summary>
        public static IEnumerable<CameraProtocol> GetSupportedProtocols()
        {
            return ConnectionFactories.Keys;
        }

        /// <summary>
        /// Checks if a protocol is supported
        /// </summary>
        public static bool IsProtocolSupported(CameraProtocol protocol)
        {
            return ConnectionFactories.ContainsKey(protocol);
        }
    }
}