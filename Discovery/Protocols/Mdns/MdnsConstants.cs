namespace wpfhikip.Discovery.Protocols.Mdns
{
    /// <summary>
    /// Constants for mDNS/Bonjour protocol
    /// </summary>
    public static class MdnsConstants
    {
        // mDNS multicast
        public const string MulticastAddress = "224.0.0.251";
        public const int MulticastPort = 5353;

        // Common service types
        public const string HttpService = "_http._tcp.local.";
        public const string HttpsService = "_https._tcp.local.";
        public const string FtpService = "_ftp._tcp.local.";
        public const string SshService = "_ssh._tcp.local.";
        public const string TelnetService = "_telnet._tcp.local.";
        public const string SmtpService = "_smtp._tcp.local.";

        // Printer services
        public const string IppService = "_ipp._tcp.local.";
        public const string PrinterService = "_printer._tcp.local.";
        public const string PdlDatastreamService = "_pdl-datastream._tcp.local.";

        // Apple services
        public const string AirPlayService = "_airplay._tcp.local.";
        public const string RaopService = "_raop._tcp.local.";
        public const string AfpService = "_afpovertcp._tcp.local.";
        public const string HomeKitService = "_hap._tcp.local.";

        // Media services
        public const string UpnpService = "_upnp._tcp.local.";
        public const string DlnaService = "_dlna._tcp.local.";

        // Network services
        public const string SmbService = "_smb._tcp.local.";
        public const string NfsService = "_nfs._tcp.local.";

        // Scan services
        public const string ScannerService = "_scanner._tcp.local.";
        public const string EsclService = "_escl._tcp.local.";

        // Device info services
        public const string DeviceInfoService = "_device-info._tcp.local.";

        /// <summary>
        /// Gets common service types for discovery
        /// </summary>
        public static string[] GetCommonServiceTypes()
        {
            return new[]
            {
                HttpService,
                HttpsService,
                IppService,
                PrinterService,
                PdlDatastreamService,
                AirPlayService,
                RaopService,
                AfpService,
                HomeKitService,
                UpnpService,
                SmbService,
                ScannerService,
                EsclService,
                DeviceInfoService,
                SshService,
                FtpService,
                TelnetService
            };
        }

        /// <summary>
        /// Gets service types specific to printers
        /// </summary>
        public static string[] GetPrinterServiceTypes()
        {
            return new[]
            {
                IppService,
                PrinterService,
                PdlDatastreamService
            };
        }

        /// <summary>
        /// Gets service types specific to Apple devices
        /// </summary>
        public static string[] GetAppleServiceTypes()
        {
            return new[]
            {
                AirPlayService,
                RaopService,
                AfpService,
                HomeKitService
            };
        }

        /// <summary>
        /// Gets service types specific to media devices
        /// </summary>
        public static string[] GetMediaServiceTypes()
        {
            return new[]
            {
                UpnpService,
                DlnaService,
                AirPlayService,
                RaopService
            };
        }
    }
}