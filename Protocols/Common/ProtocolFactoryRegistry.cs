using System.Collections.Frozen;
using wpfhikip.Models;
using wpfhikip.Protocols.Hikvision;
using wpfhikip.Protocols.Dahua;
using wpfhikip.Protocols.Axis;
using wpfhikip.Protocols.Onvif;

namespace wpfhikip.Protocols.Common
{
    /// <summary>
    /// Registry for protocol factories
    /// </summary>
    public static class ProtocolFactoryRegistry
    {
        private static readonly FrozenDictionary<CameraProtocol, IProtocolFactory> Factories =
            new Dictionary<CameraProtocol, IProtocolFactory>
            {
                { CameraProtocol.Hikvision, new HikvisionProtocolFactory() },
                //{ CameraProtocol.Dahua, new DahuaProtocolFactory() },
                { CameraProtocol.Axis, new AxisProtocolFactory() },
                { CameraProtocol.Onvif, new OnvifProtocolFactory() }
            }.ToFrozenDictionary();

        /// <summary>
        /// Gets a factory for the specified protocol
        /// </summary>
        public static IProtocolFactory? GetFactory(CameraProtocol protocol)
        {
            return Factories.TryGetValue(protocol, out var factory) ? factory : null;
        }

        /// <summary>
        /// Checks if a protocol is supported
        /// </summary>
        public static bool IsProtocolSupported(CameraProtocol protocol)
        {
            return Factories.ContainsKey(protocol);
        }

        /// <summary>
        /// Gets all supported protocols
        /// </summary>
        public static IEnumerable<CameraProtocol> GetSupportedProtocols()
        {
            return Factories.Keys;
        }
    }
}