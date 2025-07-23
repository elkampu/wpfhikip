namespace wpfhikip.Discovery.Protocols.Ssdp
{
    /// <summary>
    /// Constants and definitions for SSDP protocol
    /// </summary>
    public static class SsdpConstants
    {
        // SSDP Multicast
        public const string MulticastAddress = "239.255.255.250";
        public const int MulticastPort = 1900;

        // Common Search Targets
        public const string SearchAll = "ssdp:all";
        public const string SearchRootDevice = "upnp:rootdevice";

        // Device Types
        public const string InternetGatewayDevice = "urn:schemas-upnp-org:device:InternetGatewayDevice:1";
        public const string MediaServer = "urn:schemas-upnp-org:device:MediaServer:1";
        public const string MediaRenderer = "urn:schemas-upnp-org:device:MediaRenderer:1";
        public const string WANConnectionDevice = "urn:schemas-upnp-org:device:WANConnectionDevice:1";
        public const string WANDevice = "urn:schemas-upnp-org:device:WANDevice:1";
        public const string LANDevice = "urn:schemas-upnp-org:device:LANDevice:1";

        // Service Types
        public const string ConnectionManager = "urn:schemas-upnp-org:service:ConnectionManager:1";
        public const string ContentDirectory = "urn:schemas-upnp-org:service:ContentDirectory:1";
        public const string AVTransport = "urn:schemas-upnp-org:service:AVTransport:1";

        // Vendor-specific
        public const string AxisNetworkVideoProduct = "urn:axis-com:device:Network_Video_Product:1";
        public const string SamsungTv = "urn:samsung.com:device:RemoteControlReceiver:1";
        public const string RokuDevice = "roku:ecp";
        public const string CastDevice = "urn:dial-multiscreen-org:service:dial:1";
        public const string SonnosDevice = "urn:schemas-upnp-org:device:ZonePlayer:1";

        // Printer devices
        public const string PrinterBasic = "urn:schemas-upnp-org:device:Printer:1";
        public const string PrinterAdvanced = "urn:schemas-upnp-org:device:PrinterAdvanced:1";

        /// <summary>
        /// Gets common search targets for device discovery
        /// </summary>
        public static string[] GetCommonSearchTargets()
        {
            return new[]
            {
                SearchAll,
                SearchRootDevice,
                InternetGatewayDevice,
                MediaServer,
                MediaRenderer,
                WANConnectionDevice,
                AxisNetworkVideoProduct,
                SamsungTv,
                RokuDevice,
                CastDevice,
                SonnosDevice,
                PrinterBasic,
                PrinterAdvanced
            };
        }

        /// <summary>
        /// Gets search targets specific to network infrastructure
        /// </summary>
        public static string[] GetNetworkInfrastructureTargets()
        {
            return new[]
            {
                InternetGatewayDevice,
                WANConnectionDevice,
                WANDevice,
                LANDevice
            };
        }

        /// <summary>
        /// Gets search targets specific to media devices
        /// </summary>
        public static string[] GetMediaDeviceTargets()
        {
            return new[]
            {
                MediaServer,
                MediaRenderer,
                SamsungTv,
                RokuDevice,
                CastDevice,
                SonnosDevice
            };
        }

        /// <summary>
        /// Gets search targets specific to security cameras
        /// </summary>
        public static string[] GetSecurityDeviceTargets()
        {
            return new[]
            {
                AxisNetworkVideoProduct,
                // Add other camera manufacturer specific targets here
            };
        }

        /// <summary>
        /// Gets search targets specific to printers
        /// </summary>
        public static string[] GetPrinterTargets()
        {
            return new[]
            {
                PrinterBasic,
                PrinterAdvanced
            };
        }
    }
}