namespace wpfhikip.Discovery.Protocols.Snmp
{
    /// <summary>
    /// Constants and definitions for SNMP protocol
    /// </summary>
    public static class SnmpConstants
    {
        // Default SNMP settings
        public const int DefaultPort = 161;
        public const int DefaultTrapPort = 162;
        public const string DefaultCommunity = "public";
        public const int DefaultTimeout = 5000; // milliseconds
        public const int DefaultRetries = 1;

        // SNMP Versions
        public const int Version1 = 0;
        public const int Version2c = 1;
        public const int Version3 = 3;

        // Common OIDs for device identification
        public static class OIDs
        {
            public const string SysDescr = "1.3.6.1.2.1.1.1.0";        // System description
            public const string SysObjectId = "1.3.6.1.2.1.1.2.0";    // System object identifier
            public const string SysUpTime = "1.3.6.1.2.1.1.3.0";      // System uptime
            public const string SysContact = "1.3.6.1.2.1.1.4.0";     // System contact
            public const string SysName = "1.3.6.1.2.1.1.5.0";       // System name
            public const string SysLocation = "1.3.6.1.2.1.1.6.0";   // System location
            public const string SysServices = "1.3.6.1.2.1.1.7.0";   // System services

            // Interface information
            public const string IfNumber = "1.3.6.1.2.1.2.1.0";      // Number of interfaces
            public const string IfDescr = "1.3.6.1.2.1.2.2.1.2";    // Interface descriptions
            public const string IfType = "1.3.6.1.2.1.2.2.1.3";     // Interface types
            public const string IfPhysAddress = "1.3.6.1.2.1.2.2.1.6"; // Physical addresses

            // Enterprise-specific OIDs for cameras
            public const string HikvisionRoot = "1.3.6.1.4.1.39165";
            public const string AxisRoot = "1.3.6.1.4.1.368";
            public const string DahuaRoot = "1.3.6.1.4.1.15587";
            public const string HanwhaRoot = "1.3.6.1.4.1.36849";

            // Network infrastructure
            public const string CiscoRoot = "1.3.6.1.4.1.9";
            public const string HPRoot = "1.3.6.1.4.1.11";
            public const string JuniperRoot = "1.3.6.1.4.1.2636";

            // Printer OIDs
            public const string PrinterRoot = "1.3.6.1.2.1.43";
            public const string PrinterModel = "1.3.6.1.2.1.43.5.1.1.16.1";
            public const string PrinterSerialNumber = "1.3.6.1.2.1.43.5.1.1.17.1";
        }

        /// <summary>
        /// Common SNMP communities to try
        /// </summary>
        public static string[] GetCommonCommunities()
        {
            return new[]
            {
                "public",
                "private",
                "admin",
                "manager",
                "read",
                "write",
                "community",
                "default",
                "guest"
            };
        }

        /// <summary>
        /// Essential OIDs for basic device identification
        /// </summary>
        public static string[] GetEssentialOIDs()
        {
            return new[]
            {
                OIDs.SysDescr,
                OIDs.SysObjectId,
                OIDs.SysName,
                OIDs.SysContact,
                OIDs.SysLocation
            };
        }

        /// <summary>
        /// Determines device type based on system object ID
        /// </summary>
        public static string GetDeviceTypeFromOID(string sysObjectId)
        {
            if (string.IsNullOrEmpty(sysObjectId))
                return "Unknown";

            return sysObjectId switch
            {
                var oid when oid.StartsWith(OIDs.HikvisionRoot) => "Hikvision Camera",
                var oid when oid.StartsWith(OIDs.AxisRoot) => "Axis Camera",
                var oid when oid.StartsWith(OIDs.DahuaRoot) => "Dahua Camera",
                var oid when oid.StartsWith(OIDs.HanwhaRoot) => "Hanwha Camera",
                var oid when oid.StartsWith(OIDs.CiscoRoot) => "Cisco Device",
                var oid when oid.StartsWith(OIDs.HPRoot) => "HP Device",
                var oid when oid.StartsWith(OIDs.JuniperRoot) => "Juniper Device",
                var oid when oid.StartsWith(OIDs.PrinterRoot) => "Network Printer",
                _ => "SNMP Device"
            };
        }
    }
}