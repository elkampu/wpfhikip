using System.Text;

namespace wpfhikip.Protocols.Onvif
{
    public static class OnvifUrl
    {
        // Standard ONVIF service endpoints
        public const string DeviceService = "/onvif/device_service";
        public const string MediaService = "/onvif/media_service";
        public const string ImagingService = "/onvif/imaging_service";
        public const string PTZService = "/onvif/ptz_service";
        public const string EventService = "/onvif/event_service";
        public const string AnalyticsService = "/onvif/analytics_service";

        // Alternative common ONVIF paths
        public const string DeviceServiceAlt = "/onvif/services";
        public const string DeviceServiceAlt2 = "/onvif/device";
        public const string MediaServiceAlt = "/onvif/media";
        public const string MediaServiceAlt2 = "/onvif/Media2";

        // WS-Discovery multicast address for device discovery
        public const string DiscoveryMulticastAddress = "239.255.255.250";
        public const int DiscoveryPort = 3702;

        // Common ONVIF ports
        public const int StandardPort = 80;
        public const int SecurePort = 443;
        public const int OnvifPort = 8080;
        public const int OnvifSecurePort = 8443;

        // URL Builders
        public static class UrlBuilders
        {
            private static readonly StringBuilder s_stringBuilder = new(256);
            private static readonly object s_lock = new();

            // Cache common ports array to avoid allocation
            private static readonly int[] s_commonPorts = { 80, 8080, 8000, 554, 8554, 443, 8443 };

            public static string BuildServiceUrl(string ipAddress, string service, int port = 80, bool useHttps = false)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(ipAddress);
                ArgumentException.ThrowIfNullOrWhiteSpace(service);

                if (port is <= 0 or > 65535)
                    throw new ArgumentOutOfRangeException(nameof(port), "Port must be between 1 and 65535");

                lock (s_lock)
                {
                    s_stringBuilder.Clear();
                    s_stringBuilder.Append(useHttps ? "https://" : "http://");
                    s_stringBuilder.Append(ipAddress);

                    // Only add port if it's not the default for the protocol
                    if (port != (useHttps ? 443 : 80))
                    {
                        s_stringBuilder.Append(':');
                        s_stringBuilder.Append(port);
                    }

                    s_stringBuilder.Append(service);
                    return s_stringBuilder.ToString();
                }
            }

            public static string BuildDeviceServiceUrl(string ipAddress, int port = 80, bool useHttps = false)
            {
                return BuildServiceUrl(ipAddress, DeviceService, port, useHttps);
            }

            public static string BuildMediaServiceUrl(string ipAddress, int port = 80, bool useHttps = false)
            {
                return BuildServiceUrl(ipAddress, MediaService, port, useHttps);
            }

            public static string[] GetPossibleDeviceServiceUrls(string ipAddress, int port = 80, bool useHttps = false)
            {
                return new[]
                {
                    BuildServiceUrl(ipAddress, DeviceService, port, useHttps),
                    BuildServiceUrl(ipAddress, DeviceServiceAlt, port, useHttps),
                    BuildServiceUrl(ipAddress, DeviceServiceAlt2, port, useHttps)
                };
            }

            public static string[] GetPossibleMediaServiceUrls(string ipAddress, int port = 80, bool useHttps = false)
            {
                return new[]
                {
                    BuildServiceUrl(ipAddress, MediaService, port, useHttps),
                    BuildServiceUrl(ipAddress, MediaServiceAlt, port, useHttps),
                    BuildServiceUrl(ipAddress, MediaServiceAlt2, port, useHttps)
                };
            }

            public static int[] GetCommonOnvifPorts() => s_commonPorts;
        }

        // ONVIF SOAP Actions
        public static class SoapActions
        {
            // Device Service Actions
            public const string GetDeviceInformation = "http://www.onvif.org/ver10/device/wsdl/GetDeviceInformation";
            public const string GetCapabilities = "http://www.onvif.org/ver10/device/wsdl/GetCapabilities";
            public const string GetNetworkInterfaces = "http://www.onvif.org/ver10/device/wsdl/GetNetworkInterfaces";
            public const string SetNetworkInterfaces = "http://www.onvif.org/ver10/device/wsdl/SetNetworkInterfaces";
            public const string GetDNS = "http://www.onvif.org/ver10/device/wsdl/GetDNS";
            public const string SetDNS = "http://www.onvif.org/ver10/device/wsdl/SetDNS";
            public const string GetNetworkDefaultGateway = "http://www.onvif.org/ver10/device/wsdl/GetNetworkDefaultGateway";
            public const string SetNetworkDefaultGateway = "http://www.onvif.org/ver10/device/wsdl/SetNetworkDefaultGateway";
            public const string GetNTP = "http://www.onvif.org/ver10/device/wsdl/GetNTP";
            public const string SetNTP = "http://www.onvif.org/ver10/device/wsdl/SetNTP";
            public const string GetSystemDateAndTime = "http://www.onvif.org/ver10/device/wsdl/GetSystemDateAndTime";
            public const string SetSystemDateAndTime = "http://www.onvif.org/ver10/device/wsdl/SetSystemDateAndTime";
            public const string SystemReboot = "http://www.onvif.org/ver10/device/wsdl/SystemReboot";

            // Media Service Actions
            public const string GetProfiles = "http://www.onvif.org/ver10/media/wsdl/GetProfiles";
            public const string GetVideoEncoderConfiguration = "http://www.onvif.org/ver10/media/wsdl/GetVideoEncoderConfiguration";
            public const string GetStreamUri = "http://www.onvif.org/ver10/media/wsdl/GetStreamUri";
            public const string GetVideoSources = "http://www.onvif.org/ver10/media/wsdl/GetVideoSources";
            public const string GetVideoEncoderConfigurations = "http://www.onvif.org/ver10/media/wsdl/GetVideoEncoderConfigurations";

            // Discovery Actions
            public const string Probe = "http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe";
            public const string ProbeMatches = "http://schemas.xmlsoap.org/ws/2005/04/discovery/ProbeMatches";
        }

        // ONVIF Namespaces
        public static class Namespaces
        {
            public const string Device = "http://www.onvif.org/ver10/device/wsdl";
            public const string Schema = "http://www.onvif.org/ver10/schema";
            public const string Media = "http://www.onvif.org/ver10/media/wsdl";
            public const string Soap = "http://www.w3.org/2003/05/soap-envelope";
            public const string WSAddressing = "http://www.w3.org/2005/08/addressing";
            public const string WSDiscovery = "http://schemas.xmlsoap.org/ws/2005/04/discovery";
            public const string WSSecurityExt = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
            public const string WSSecurityUtility = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";
        }

        // Device Types for WS-Discovery
        public static class DeviceTypes
        {
            public const string NetworkVideoTransmitter = "dn:NetworkVideoTransmitter";
            public const string Device = "tds:Device";
        }
    }

    // Content Types
    public static class OnvifContentTypes
    {
        public const string Soap = "application/soap+xml";
        public const string Xml = "application/xml";
        public const string Text = "text/xml";
    }

    // Response Status Messages
    public static class OnvifStatusMessages
    {
        public const string ConnectionOk = "Connection OK";
        public const string DeviceFound = "ONVIF device found";
        public const string DeviceNotFound = "ONVIF device not found";
        public const string NetworkSettingsSent = "Network settings sent successfully";
        public const string NetworkSettingsError = "Error sending network settings";
        public const string NtpServerSent = "NTP server sent successfully";
        public const string NtpServerError = "Error sending NTP server";
        public const string LoginFailed = "Authentication failed";
        public const string UnknownConnectionError = "Unknown connection error";
        public const string SendingRequest = "Sending ONVIF request...";
        public const string RetrievingDeviceInfo = "Retrieving device information...";
        public const string DeviceInfoRetrieved = "Device information retrieved successfully";
        public const string DeviceInfoError = "Error retrieving device information";
        public const string SoapFault = "SOAP fault received";
        public const string InvalidResponse = "Invalid ONVIF response";
    }
}