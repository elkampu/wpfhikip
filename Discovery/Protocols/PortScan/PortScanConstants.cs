namespace wpfhikip.Discovery.Protocols.PortScan
{
    /// <summary>
    /// Constants for port scanning
    /// </summary>
    public static class PortScanConstants
    {
        /// <summary>
        /// Common ports to scan for device discovery
        /// </summary>
        public static readonly Dictionary<int, string> CommonPorts = new()
        {
            // Web services
            { 80, "HTTP" },
            { 443, "HTTPS" },
            { 8080, "HTTP-Alt" },
            { 8000, "HTTP-Alt2" },
            { 8443, "HTTPS-Alt" },
            { 8008, "HTTP" },
            { 8888, "HTTP" },
            
            // Remote access
            { 22, "SSH" },
            { 23, "Telnet" },
            { 3389, "RDP" },
            { 5900, "VNC" },
            { 5901, "VNC" },
            { 5902, "VNC" },
            
            // File sharing
            { 21, "FTP" },
            { 139, "NetBIOS" },
            { 445, "SMB/CIFS" },
            { 2049, "NFS" },
            
            // Email
            { 25, "SMTP" },
            { 110, "POP3" },
            { 143, "IMAP" },
            { 993, "IMAPS" },
            { 995, "POP3S" },
            
            // Printing
            { 631, "IPP/CUPS" },
            { 9100, "JetDirect" },
            { 515, "LPD" },
            
            // Media streaming
            { 554, "RTSP" },
            { 8554, "RTSP-Alt" },
            { 1935, "RTMP" },
            
            // Network management
            { 161, "SNMP" },
            { 162, "SNMP-Trap" },
            
            // Databases
            { 3306, "MySQL" },
            { 5432, "PostgreSQL" },
            { 1433, "MSSQL" },
            { 1521, "Oracle" },
            { 27017, "MongoDB" },
            
            // Camera-specific ports
            { 37777, "Dahua" },
            { 34567, "Hikvision" },
            { 8000, "Hikvision-HTTP" },
            { 65001, "Hikvision" },
            
            // Other common services
            { 53, "DNS" },
            { 67, "DHCP" },
            { 123, "NTP" },
            { 135, "RPC" },
            { 1900, "SSDP" },
            { 5353, "mDNS" },
            { 3702, "WS-Discovery" }
        };

        /// <summary>
        /// Gets common ports for device discovery
        /// </summary>
        public static int[] GetCommonPorts()
        {
            return CommonPorts.Keys.ToArray();
        }

        /// <summary>
        /// Gets high-priority ports (most likely to indicate device type)
        /// </summary>
        public static int[] GetHighPriorityPorts()
        {
            return new[] { 80, 443, 22, 23, 8080, 554, 631, 9100, 37777, 34567, 8000 };
        }

        /// <summary>
        /// Gets camera-specific ports
        /// </summary>
        public static int[] GetCameraPorts()
        {
            return new[] { 80, 554, 8080, 8000, 37777, 34567, 65001, 8554 };
        }

        /// <summary>
        /// Gets printer-specific ports
        /// </summary>
        public static int[] GetPrinterPorts()
        {
            return new[] { 631, 9100, 515 };
        }

        /// <summary>
        /// Gets network infrastructure ports
        /// </summary>
        public static int[] GetNetworkInfrastructurePorts()
        {
            return new[] { 80, 443, 23, 22, 161, 162, 53, 67, 123 };
        }

        /// <summary>
        /// Gets service name for a port number
        /// </summary>
        public static string? GetServiceName(int port)
        {
            return CommonPorts.GetValueOrDefault(port);
        }

        /// <summary>
        /// Gets priority for a port (lower number = higher priority)
        /// </summary>
        public static int GetPortPriority(int port)
        {
            return port switch
            {
                80 => 1,
                443 => 2,
                8080 => 3,
                22 => 4,
                23 => 5,
                554 => 6,
                8000 => 7,
                631 => 8,
                9100 => 9,
                37777 => 10,
                34567 => 11,
                _ => 100
            };
        }
    }
}