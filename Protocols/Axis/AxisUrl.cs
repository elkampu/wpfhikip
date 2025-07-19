using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace wpfhikip.Protocols.Axis
{
    public static class AxisUrl
    {
        // Base endpoints
        public const string NetworkSettingsBase = "/axis-cgi/network_settings.cgi";
        public const string ParamBase = "/axis-cgi/param.cgi";

        // Network configuration endpoints
        public const string NetworkSettings = "/axis-cgi/network_settings.cgi";
        public const string NetworkParams = "/axis-cgi/param.cgi?action=list&group=Network";
        public const string NetworkIPParams = "/axis-cgi/param.cgi?action=list&group=Network.IPAddress";

        // System information endpoints
        public const string SystemParams = "/axis-cgi/param.cgi?action=list&group=Properties.System";
        public const string DeviceInfo = "/axis-cgi/param.cgi?action=list&group=Properties";

        // NTP configuration endpoints (for future implementation)
        public const string NtpParams = "/axis-cgi/param.cgi?action=list&group=Network.NTP";

        // URL Builders
        public static class UrlBuilders
        {
            public static string BuildGetUrl(string ipAddress, string endpoint, bool useHttps = false)
            {
                var protocol = useHttps ? "https" : "http";
                return $"{protocol}://{ipAddress}{endpoint}";
            }

            public static string BuildPostUrl(string ipAddress, string endpoint, bool useHttps = false)
            {
                var protocol = useHttps ? "https" : "http";
                return $"{protocol}://{ipAddress}{endpoint}";
            }

            public static string BuildNetworkInfoUrl(string ipAddress, bool useHttps = false)
            {
                return BuildPostUrl(ipAddress, NetworkSettings, useHttps);
            }

            public static string BuildSystemInfoUrl(string ipAddress, bool useHttps = false)
            {
                return BuildGetUrl(ipAddress, SystemParams, useHttps);
            }

            public static string BuildDeviceInfoUrl(string ipAddress, bool useHttps = false)
            {
                return BuildGetUrl(ipAddress, DeviceInfo, useHttps);
            }

            public static string BuildNetworkParamsUrl(string ipAddress, bool useHttps = false)
            {
                return BuildGetUrl(ipAddress, NetworkParams, useHttps);
            }
        }

        // JSON Methods for Axis API
        public static class JsonMethods
        {
            public const string GetNetworkInfo = "getNetworkInfo";
            public const string SetIPv4AddressConfiguration = "setIPv4AddressConfiguration";
            public const string GetIPv4AddressConfiguration = "getIPv4AddressConfiguration";
        }

        // Endpoint to HTTP Method mapping
        public static class EndpointMethods
        {
            public static readonly Dictionary<string, (string GetMethod, string SetMethod)> Methods = new()
            {
                { NetworkSettings, ("POST", "POST") }, // Axis uses POST for both read and write
                { NetworkParams, ("GET", "POST") },
                { SystemParams, ("GET", "N/A") },
                { DeviceInfo, ("GET", "N/A") }
            };

            public static (string GetMethod, string SetMethod) GetMethodsForEndpoint(string endpoint)
            {
                return Methods.TryGetValue(endpoint, out var methods) ? methods : ("GET", "POST");
            }
        }
    }

    // Content Types
    public static class AxisContentTypes
    {
        public const string Json = "application/json";
        public const string FormUrlEncoded = "application/x-www-form-urlencoded";
        public const string Text = "text/plain";
    }

    // Response Status Messages
    public static class AxisStatusMessages
    {
        public const string ConnectionOk = "Connection OK";
        public const string NetworkSettingsSent = "Network settings sent successfully";
        public const string NetworkSettingsError = "Error sending network settings";
        public const string NtpServerSent = "NTP server sent successfully";
        public const string NtpServerError = "Error sending NTP server";
        public const string LoginFailed = "Login failed";
        public const string UnknownConnectionError = "Unknown connection error";
        public const string SendingRequest = "Sending request...";
        public const string RetrievingCurrentConfig = "Retrieving current configuration...";
        public const string ConfigRetrieved = "Configuration retrieved successfully";
        public const string ConfigRetrievalError = "Error retrieving configuration";
    }
}
