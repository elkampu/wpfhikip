using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace wpfhikip.Protocols.Hikvision
{
    public static class HikvisionUrl
    {
        // Endpoints
        public const string SystemCapabilities = "/ISAPI/System/capabilities";
        public const string SystemReboot = "/ISAPI/System/reboot";
        public const string DeviceInfo = "/ISAPI/System/deviceInfo";
        public const string NetworkInterface = "/ISAPI/System/Network/interfaces/1";
        public const string NetworkInterfaceIpAddress = "/ISAPI/System/Network/interfaces/1/ipAddress";
        public const string SystemTime = "/ISAPI/System/time";
        public const string NtpServers = "/ISAPI/System/time/ntpServers";

        // URL Builders
        public static class UrlBuilders
        {
            public static string BuildGetUrl(string ipAddress, string endpoint, bool useHttps = false)
            {
                var protocol = useHttps ? "https" : "http";
                return $"{protocol}://{ipAddress}{endpoint}";
            }

            public static string BuildPutUrl(string ipAddress, string endpoint, bool useHttps = false)
            {
                var protocol = useHttps ? "https" : "http";
                return $"{protocol}://{ipAddress}{endpoint}";
            }
        }

        // Endpoint to HTTP Method mapping for template validation workflow
        public static class EndpointMethods
        {
            public static readonly Dictionary<string, (string GetMethod, string SetMethod)> Methods = new()
            {
                { NetworkInterfaceIpAddress, ("GET", "PUT") },
                { SystemTime, ("GET", "PUT") },
                { NtpServers, ("GET", "PUT") },
                { DeviceInfo, ("GET", "N/A") },
                { SystemCapabilities, ("GET", "N/A") },
                { SystemReboot, ("N/A", "PUT") }
            };

            public static (string GetMethod, string SetMethod) GetMethodsForEndpoint(string endpoint)
            {
                return Methods.TryGetValue(endpoint, out var methods) ? methods : ("GET", "PUT");
            }
        }
    }

    // Content Types
    public static class ContentTypes
    {
        public const string Xml = "application/xml";
        public const string Json = "application/json";
        public const string FormUrlEncoded = "application/x-www-form-urlencoded";
    }

    // Response Status Messages
    public static class StatusMessages
    {
        public const string ConnectionOk = "Connection OK";
        public const string NetworkSettingsSent = "Network settings sent successfully";
        public const string NetworkSettingsError = "Error sending network settings";
        public const string NtpServerSent = "NTP server sent successfully";
        public const string NtpServerError = "Error sending NTP server";
        public const string LoginFailed = "Login failed";
        public const string UnknownConnectionError = "Unknown connection error";
        public const string Rebooting = "Rebooting...";
        public const string RebootError = "Error rebooting";
        public const string SendingRequest = "Sending request...";
        public const string RetrievingCurrentConfig = "Retrieving current configuration...";
        public const string ConfigRetrieved = "Configuration retrieved successfully";
        public const string ConfigRetrievalError = "Error retrieving configuration";
    }
}
