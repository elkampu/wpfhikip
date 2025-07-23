namespace wpfhikip.Discovery.Protocols.WsDiscovery
{
    /// <summary>
    /// Constants for WS-Discovery protocol
    /// </summary>
    public static class WsDiscoveryConstants
    {
        // WS-Discovery multicast
        public const string MulticastAddress = "239.255.255.250";
        public const int MulticastPort = 3702;

        // ONVIF device types
        public const string NetworkVideoTransmitter = "dn:NetworkVideoTransmitter";
        public const string Device = "tds:Device";
        public const string NetworkVideoRecorder = "dn:NetworkVideoRecorder";

        // Generic WS-Discovery types
        public const string GenericDevice = "wsdp:Device";

        // Axis-specific types
        public const string AxisNetworkCamera = "axis:NetworkCamera";
        public const string AxisNetworkVideoProduct = "axis:NetworkVideoProduct";

        // Action URIs
        public const string ProbeAction = "http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe";
        public const string ProbeMatchesAction = "http://schemas.xmlsoap.org/ws/2005/04/discovery/ProbeMatches";
        public const string HelloAction = "http://schemas.xmlsoap.org/ws/2005/04/discovery/Hello";
        public const string ByeAction = "http://schemas.xmlsoap.org/ws/2005/04/discovery/Bye";

        // Namespaces
        public const string DiscoveryNamespace = "http://schemas.xmlsoap.org/ws/2005/04/discovery";
        public const string AddressingNamespace = "http://www.w3.org/2005/08/addressing";
        public const string OnvifDeviceNamespace = "http://www.onvif.org/ver10/device/wsdl";
        public const string OnvifNetworkNamespace = "http://www.onvif.org/ver10/network/wsdl";

        /// <summary>
        /// Gets common device types for discovery probes
        /// </summary>
        public static string[] GetCommonDeviceTypes()
        {
            return new[]
            {
                NetworkVideoTransmitter,
                Device,
                NetworkVideoRecorder,
                GenericDevice,
                AxisNetworkCamera,
                AxisNetworkVideoProduct
            };
        }

        /// <summary>
        /// Gets ONVIF-specific device types
        /// </summary>
        public static string[] GetOnvifDeviceTypes()
        {
            return new[]
            {
                NetworkVideoTransmitter,
                Device,
                NetworkVideoRecorder
            };
        }

        /// <summary>
        /// Gets vendor-specific device types
        /// </summary>
        public static string[] GetVendorSpecificDeviceTypes()
        {
            return new[]
            {
                AxisNetworkCamera,
                AxisNetworkVideoProduct
            };
        }
    }
}