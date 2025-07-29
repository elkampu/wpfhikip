namespace wpfhikip.Discovery.Models
{
    /// <summary>
    /// Different methods/protocols used for network device discovery
    /// </summary>
    public enum DiscoveryMethod
    {
        Unknown = 0,

        // Multicast/Broadcast Discovery
        SSDP = 1,           // Simple Service Discovery Protocol (UPnP)
        WSDiscovery = 2,    // WS-Discovery (ONVIF)
        mDNS = 3,           // Multicast DNS (Bonjour/Zeroconf)
        DHCP = 4,           // DHCP lease table analysis

        // Network Layer Discovery
        ARP = 10,           // ARP table scanning
        ICMP = 11,          // ICMP ping sweep

        // Transport Layer Discovery
        PortScan = 20,      // TCP/UDP port scanning

        // Application Layer Discovery
        SNMP = 30,          // Simple Network Management Protocol
        HTTP = 31,          // HTTP banner grabbing
        SSH = 32,           // SSH banner grabbing
        Telnet = 33,        // Telnet probing

        // Platform-Specific Discovery  
        NetBIOS = 40,       // NetBIOS name resolution
        SMB = 41,           // SMB/CIFS shares discovery
        WMI = 42,           // Windows Management Instrumentation

        // Security-Specific Discovery  
        ONVIFProbe = 50,    // ONVIF-specific WS-Discovery
        RTSPProbe = 51,     // RTSP stream discovery

        // Hybrid Methods
        ActiveScan = 60,    // Combined active scanning techniques
        PassiveMonitor = 61, // Passive network monitoring

        // Manual/External
        Manual = 70,        // Manually added devices
        Import = 71,        // Imported from external sources
        Database = 72       // Retrieved from database
    }

    /// <summary>
    /// Extension methods for DiscoveryMethod enum
    /// </summary>
    public static class DiscoveryMethodExtensions
    {
        // Pre-computed descriptions to avoid repeated string concatenation
        private static readonly IReadOnlyDictionary<DiscoveryMethod, string> s_descriptions = 
            new Dictionary<DiscoveryMethod, string>
            {
                [DiscoveryMethod.SSDP] = "SSDP/UPnP Discovery",
                [DiscoveryMethod.WSDiscovery] = "WS-Discovery",
                [DiscoveryMethod.mDNS] = "mDNS/Bonjour",
                [DiscoveryMethod.DHCP] = "DHCP Analysis",
                [DiscoveryMethod.ARP] = "ARP Table Scan",
                [DiscoveryMethod.ICMP] = "ICMP Ping Sweep",
                [DiscoveryMethod.PortScan] = "Port Scanning",
                [DiscoveryMethod.SNMP] = "SNMP Discovery",
                [DiscoveryMethod.HTTP] = "HTTP Probing",
                [DiscoveryMethod.SSH] = "SSH Banner Grab",
                [DiscoveryMethod.Telnet] = "Telnet Probing",
                [DiscoveryMethod.NetBIOS] = "NetBIOS Discovery",
                [DiscoveryMethod.SMB] = "SMB/CIFS Discovery",
                [DiscoveryMethod.WMI] = "WMI Query",
                [DiscoveryMethod.ONVIFProbe] = "ONVIF Probe",
                [DiscoveryMethod.RTSPProbe] = "RTSP Discovery",
                [DiscoveryMethod.ActiveScan] = "Active Network Scan",
                [DiscoveryMethod.PassiveMonitor] = "Passive Monitoring",
                [DiscoveryMethod.Manual] = "Manual Entry",
                [DiscoveryMethod.Import] = "External Import",
                [DiscoveryMethod.Database] = "Database Query"
            };

        private static readonly IReadOnlyDictionary<DiscoveryMethod, string> s_categories = 
            new Dictionary<DiscoveryMethod, string>
            {
                [DiscoveryMethod.SSDP] = "Multicast/Broadcast",
                [DiscoveryMethod.WSDiscovery] = "Multicast/Broadcast",
                [DiscoveryMethod.mDNS] = "Multicast/Broadcast",
                [DiscoveryMethod.DHCP] = "Multicast/Broadcast",
                [DiscoveryMethod.ARP] = "Network Layer",
                [DiscoveryMethod.ICMP] = "Network Layer",
                [DiscoveryMethod.PortScan] = "Transport Layer",
                [DiscoveryMethod.SNMP] = "Application Layer",
                [DiscoveryMethod.HTTP] = "Application Layer",
                [DiscoveryMethod.SSH] = "Application Layer",
                [DiscoveryMethod.Telnet] = "Application Layer",
                [DiscoveryMethod.NetBIOS] = "Platform-Specific",
                [DiscoveryMethod.SMB] = "Platform-Specific",
                [DiscoveryMethod.WMI] = "Platform-Specific",
                [DiscoveryMethod.ONVIFProbe] = "Security-Specific",
                [DiscoveryMethod.RTSPProbe] = "Security-Specific",
                [DiscoveryMethod.ActiveScan] = "Hybrid",
                [DiscoveryMethod.PassiveMonitor] = "Hybrid",
                [DiscoveryMethod.Manual] = "Manual/External",
                [DiscoveryMethod.Import] = "Manual/External",
                [DiscoveryMethod.Database] = "Manual/External"
            };

        private static readonly IReadOnlyDictionary<DiscoveryMethod, TimeSpan> s_defaultTimeouts = 
            new Dictionary<DiscoveryMethod, TimeSpan>
            {
                [DiscoveryMethod.SSDP] = TimeSpan.FromSeconds(30),
                [DiscoveryMethod.WSDiscovery] = TimeSpan.FromSeconds(15),
                [DiscoveryMethod.mDNS] = TimeSpan.FromSeconds(10),
                [DiscoveryMethod.ICMP] = TimeSpan.FromSeconds(5),
                [DiscoveryMethod.PortScan] = TimeSpan.FromMinutes(2),
                [DiscoveryMethod.SNMP] = TimeSpan.FromSeconds(10),
                [DiscoveryMethod.ARP] = TimeSpan.FromSeconds(5),
                [DiscoveryMethod.ActiveScan] = TimeSpan.FromMinutes(5)
            };

        /// <summary>
        /// Gets a human-readable description of the discovery method
        /// </summary>
        public static string GetDescription(this DiscoveryMethod method)
        {
            return s_descriptions.TryGetValue(method, out var description) ? description : "Unknown Method";
        }

        /// <summary>
        /// Gets the category of the discovery method
        /// </summary>
        public static string GetCategory(this DiscoveryMethod method)
        {
            return s_categories.TryGetValue(method, out var category) ? category : "Unknown";
        }

        /// <summary>
        /// Determines if this discovery method requires network access
        /// </summary>
        public static bool RequiresNetworkAccess(this DiscoveryMethod method)
        {
            return method switch
            {
                DiscoveryMethod.Manual or DiscoveryMethod.Import or DiscoveryMethod.Database => false,
                _ => true
            };
        }

        /// <summary>
        /// Determines if this discovery method is active (sends packets)
        /// </summary>
        public static bool IsActiveMethod(this DiscoveryMethod method)
        {
            return method switch
            {
                DiscoveryMethod.PassiveMonitor or
                DiscoveryMethod.ARP or      // Reading ARP table is passive
                DiscoveryMethod.DHCP or     // Reading DHCP leases is passive
                DiscoveryMethod.Manual or
                DiscoveryMethod.Import or
                DiscoveryMethod.Database => false,
                _ => true
            };
        }

        /// <summary>
        /// Gets the default timeout for this discovery method
        /// </summary>
        public static TimeSpan GetDefaultTimeout(this DiscoveryMethod method)
        {
            return s_defaultTimeouts.TryGetValue(method, out var timeout) ? timeout : TimeSpan.FromSeconds(30);
        }
    }
}