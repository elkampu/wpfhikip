namespace wpfhikip.Discovery.Protocols.NetBios
{
    /// <summary>
    /// Constants and definitions for NetBIOS protocol
    /// </summary>
    public static class NetBiosConstants
    {
        // NetBIOS Name Service
        public const int NameServicePort = 137;
        public const int SessionServicePort = 139;
        public const int DatagramServicePort = 138;

        // NetBIOS node types
        public const byte BNodeType = 0x01;      // Broadcast node
        public const byte PNodeType = 0x02;      // Point-to-point node
        public const byte MNodeType = 0x04;      // Mixed node
        public const byte HNodeType = 0x08;      // Hybrid node

        // NetBIOS name types
        public const byte WorkstationService = 0x00;
        public const byte MessengerService = 0x03;
        public const byte RasServerService = 0x06;
        public const byte DomainMasterBrowser = 0x1B;
        public const byte MasterBrowser = 0x1D;
        public const byte BrowserService = 0x1E;
        public const byte NetDDEService = 0x1F;
        public const byte ServerService = 0x20;
        public const byte RasClientService = 0x21;

        // Broadcast address
        public const string BroadcastAddress = "255.255.255.255";

        // Query types
        public const ushort NBNameQuery = 0x0020;
        public const ushort NBStatQuery = 0x0021;

        // Response flags
        public const ushort ResponseFlag = 0x8000;
        public const ushort AuthoritativeAnswer = 0x0400;
        public const ushort Recursion = 0x0100;

        /// <summary>
        /// Gets human-readable name for NetBIOS service type
        /// </summary>
        public static string GetServiceTypeName(byte serviceType)
        {
            return serviceType switch
            {
                WorkstationService => "Workstation",
                MessengerService => "Messenger",
                RasServerService => "RAS Server",
                DomainMasterBrowser => "Domain Master Browser",
                MasterBrowser => "Master Browser",
                BrowserService => "Browser",
                NetDDEService => "NetDDE",
                ServerService => "Server",
                RasClientService => "RAS Client",
                _ => $"Unknown ({serviceType:X2})"
            };
        }

        /// <summary>
        /// Determines if service type indicates a server/shared resource
        /// </summary>
        public static bool IsServerService(byte serviceType)
        {
            return serviceType switch
            {
                ServerService or
                DomainMasterBrowser or
                MasterBrowser or
                BrowserService => true,
                _ => false
            };
        }

        /// <summary>
        /// Common NetBIOS names to query
        /// </summary>
        public static string[] GetCommonNetBiosNames()
        {
            return new[]
            {
                "*\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00", // Wildcard
                "__MSBROWSE__\x01",  // Master browser
                "WORKGROUP\x00",     // Default workgroup
                "DOMAIN\x00",        // Domain
                "LOCALHOST\x00"      // Local host
            };
        }
    }
}