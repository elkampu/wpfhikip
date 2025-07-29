namespace wpfhikip.Discovery.Protocols.OnvifProbe
{
    /// <summary>
    /// Constants and definitions for ONVIF probe discovery
    /// </summary>
    public static class OnvifProbeConstants
    {
        // WS-Discovery multicast for ONVIF
        public const string MulticastAddress = "239.255.255.250";
        public const int MulticastPort = 3702;

        // ONVIF device types
        public const string NetworkVideoTransmitter = "tds:Device";
        public const string NetworkVideoRecorder = "dn:NetworkVideoRecorder";
        public const string NetworkVideoDisplay = "dn:NetworkVideoDisplay";

        // Namespaces
        public const string OnvifDeviceNamespace = "http://www.onvif.org/ver10/device/wsdl";
        public const string OnvifNetworkNamespace = "http://www.onvif.org/ver10/network/wsdl";
        public const string DiscoveryNamespace = "http://schemas.xmlsoap.org/ws/2005/04/discovery";
        public const string AddressingNamespace = "http://www.w3.org/2005/08/addressing";

        // Action URIs
        public const string ProbeAction = "http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe";
        public const string ProbeMatchesAction = "http://schemas.xmlsoap.org/ws/2005/04/discovery/ProbeMatches";

        // ONVIF-specific scopes
        public const string OnvifHardwareScope = "onvif://www.onvif.org/hardware/";
        public const string OnvifNameScope = "onvif://www.onvif.org/name/";
        public const string OnvifLocationScope = "onvif://www.onvif.org/location/";
        public const string OnvifTypeScope = "onvif://www.onvif.org/type/";

        /// <summary>
        /// Gets ONVIF-specific device types for probing
        /// </summary>
        public static string[] GetOnvifDeviceTypes()
        {
            return new[]
            {
                NetworkVideoTransmitter,
                NetworkVideoRecorder,
                NetworkVideoDisplay
            };
        }

        /// <summary>
        /// Creates ONVIF WS-Discovery probe message
        /// </summary>
        public static string CreateOnvifProbeMessage()
        {
            var messageId = Guid.NewGuid().ToString();
            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<soap:Envelope 
    xmlns:soap=""http://www.w3.org/2003/05/soap-envelope""
    xmlns:wsa=""http://www.w3.org/2005/08/addressing""
    xmlns:wsd=""http://schemas.xmlsoap.org/ws/2005/04/discovery""
    xmlns:tds=""http://www.onvif.org/ver10/device/wsdl"">
    <soap:Header>
        <wsa:MessageID>urn:uuid:{messageId}</wsa:MessageID>
        <wsa:To soap:mustUnderstand=""true"">urn:schemas-xmlsoap-org:ws:2005:04:discovery</wsa:To>
        <wsa:Action soap:mustUnderstand=""true"">{ProbeAction}</wsa:Action>
    </soap:Header>
    <soap:Body>
        <wsd:Probe>
            <wsd:Types>tds:Device</wsd:Types>
        </wsd:Probe>
    </soap:Body>
</soap:Envelope>";
        }

        /// <summary>
        /// Extracts device information from ONVIF scopes
        /// </summary>
        public static (string? Name, string? Hardware, string? Location, string? Type) ParseOnvifScopes(string scopes)
        {
            if (string.IsNullOrEmpty(scopes))
                return (null, null, null, null);

            string? name = null, hardware = null, location = null, type = null;

            var scopeList = scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (var scope in scopeList)
            {
                if (scope.StartsWith(OnvifNameScope))
                {
                    name = Uri.UnescapeDataString(scope.Substring(OnvifNameScope.Length));
                }
                else if (scope.StartsWith(OnvifHardwareScope))
                {
                    hardware = Uri.UnescapeDataString(scope.Substring(OnvifHardwareScope.Length));
                }
                else if (scope.StartsWith(OnvifLocationScope))
                {
                    location = Uri.UnescapeDataString(scope.Substring(OnvifLocationScope.Length));
                }
                else if (scope.StartsWith(OnvifTypeScope))
                {
                    type = Uri.UnescapeDataString(scope.Substring(OnvifTypeScope.Length));
                }
            }

            return (name, hardware, location, type);
        }
    }
}