using System.Text;

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
            private static readonly StringBuilder s_stringBuilder = new(256);
            private static readonly object s_lock = new();

            public static string BuildGetUrl(string ipAddress, string endpoint, bool useHttps = false)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(ipAddress);
                ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

                lock (s_lock)
                {
                    s_stringBuilder.Clear();
                    s_stringBuilder.Append(useHttps ? "https://" : "http://");
                    s_stringBuilder.Append(ipAddress);
                    s_stringBuilder.Append(endpoint);
                    return s_stringBuilder.ToString();
                }
            }

            public static string BuildPostUrl(string ipAddress, string endpoint, bool useHttps = false)
            {
                return BuildGetUrl(ipAddress, endpoint, useHttps);
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

            public static string BuildUrlWithPort(string ipAddress, int port, string endpoint, bool useHttps = false)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(ipAddress);
                ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

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
                    
                    s_stringBuilder.Append(endpoint);
                    return s_stringBuilder.ToString();
                }
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
            public static readonly IReadOnlyDictionary<string, (string GetMethod, string SetMethod)> Methods = 
                new Dictionary<string, (string GetMethod, string SetMethod)>
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
