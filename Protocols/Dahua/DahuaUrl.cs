namespace wpfhikip.Protocols.Dahua
{
    public static class DahuaUrl
    {
        // Base endpoints
        public const string ConfigManagerBase = "/cgi-bin/configManager.cgi";
        public const string SnapshotBase = "/cgi-bin/snapshot.cgi";

        // Network configuration endpoints
        public const string NetworkEth0Config = "/cgi-bin/configManager.cgi?action=getConfig&name=Network.eth0";
        public const string NetworkEth0IpAddress = "/cgi-bin/configManager.cgi?action=getConfig&name=Network.eth0.IPAddress";
        public const string NetworkEth0SubnetMask = "/cgi-bin/configManager.cgi?action=getConfig&name=Network.eth0.SubnetMask";
        public const string NetworkEth0Gateway = "/cgi-bin/configManager.cgi?action=getConfig&name=Network.eth0.DefaultGateway";

        // NTP configuration endpoints
        public const string NtpConfig = "/cgi-bin/configManager.cgi?action=getConfig&name=NTP";
        public const string NtpEnable = "/cgi-bin/configManager.cgi?action=getConfig&name=NTP.Enable";
        public const string NtpAddress = "/cgi-bin/configManager.cgi?action=getConfig&name=NTP.Address";
        public const string NtpTimeZone = "/cgi-bin/configManager.cgi?action=getConfig&name=NTP.TimeZone";

        // DST (Daylight Saving Time) configuration endpoints
        public const string LocalesConfig = "/cgi-bin/configManager.cgi?action=getConfig&name=Locales";
        public const string DstConfig = "/cgi-bin/configManager.cgi?action=getConfig&name=Locales.DSTEnable";

        // System information endpoints
        public const string DeviceInfo = "/cgi-bin/configManager.cgi?action=getConfig&name=General";
        public const string SystemInfo = "/cgi-bin/configManager.cgi?action=getConfig&name=General.MachineName";

        // Video streaming endpoints
        public const string VideoConfig = "/cgi-bin/configManager.cgi?action=getConfig&name=Encode";
        public const string VideoMainStream = "/cgi-bin/configManager.cgi?action=getConfig&name=Encode.0";
        public const string VideoSubStream = "/cgi-bin/configManager.cgi?action=getConfig&name=Encode.1";

        // Media streaming endpoints
        public const string SnapshotChannel1 = "/cgi-bin/snapshot.cgi?channel=1";
        public const string SnapshotChannel2 = "/cgi-bin/snapshot.cgi?channel=2";

        // URL Builders
        public static class UrlBuilders
        {
            public static string BuildGetUrl(string ipAddress, string endpoint, bool useHttps = false)
            {
                var protocol = useHttps ? "https" : "http";
                return $"{protocol}://{ipAddress}{endpoint}";
            }

            public static string BuildGetUrl(string ipAddress, string endpoint, bool useHttps, int port)
            {
                var protocol = useHttps ? "https" : "http";
                var portSuffix = port is not (80 or 443) ? $":{port}" : "";
                return $"{protocol}://{ipAddress}{portSuffix}{endpoint}";
            }

            public static string BuildSetConfigUrl(string ipAddress, Dictionary<string, string> parameters, bool useHttps = false)
            {
                var protocol = useHttps ? "https" : "http";
                var baseUrl = $"{protocol}://{ipAddress}{ConfigManagerBase}?action=setConfig";

                if (parameters?.Any() == true)
                {
                    var paramString = string.Join("&", parameters.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
                    return $"{baseUrl}&{paramString}";
                }

                return baseUrl;
            }

            public static string BuildNetworkConfigUrl(string ipAddress, string newIP = null, string newMask = null, string newGateway = null, bool useHttps = false)
            {
                var parameters = new Dictionary<string, string>();

                if (!string.IsNullOrEmpty(newIP))
                    parameters["Network.eth0.IPAddress"] = newIP;

                if (!string.IsNullOrEmpty(newMask))
                    parameters["Network.eth0.SubnetMask"] = newMask;

                if (!string.IsNullOrEmpty(newGateway))
                    parameters["Network.eth0.DefaultGateway"] = newGateway;

                return BuildSetConfigUrl(ipAddress, parameters, useHttps);
            }

            public static string BuildNtpConfigUrl(string ipAddress, string ntpServer = null, bool enableNtp = true, int timeZone = 1, bool useHttps = false)
            {
                var parameters = new Dictionary<string, string>();

                if (enableNtp)
                    parameters["NTP.Enable"] = "true";

                parameters["NTP.TimeZone"] = timeZone.ToString();

                if (!string.IsNullOrEmpty(ntpServer))
                    parameters["NTP.Address"] = ntpServer;

                return BuildSetConfigUrl(ipAddress, parameters, useHttps);
            }

            public static string BuildDstConfigUrl(string ipAddress, bool enableDst = true, bool useHttps = false)
            {
                var parameters = new Dictionary<string, string>
                {
                    ["Locales.DSTEnable"] = enableDst.ToString().ToLower(),
                    ["Locales.DSTStart.Month"] = "3",
                    ["Locales.DSTStart.Week"] = "-1",
                    ["Locales.DSTStart.Day"] = "0",
                    ["Locales.DSTStart.Hour"] = "2",
                    ["Locales.DSTStart.Minute"] = "0",
                    ["Locales.DSTEnd.Month"] = "10",
                    ["Locales.DSTEnd.Week"] = "-1",
                    ["Locales.DSTEnd.Day"] = "0",
                    ["Locales.DSTEnd.Hour"] = "2",
                    ["Locales.DSTEnd.Minute"] = "0"
                };

                return BuildSetConfigUrl(ipAddress, parameters, useHttps);
            }

            public static string BuildSnapshotUrl(string ipAddress, int channel = 1, bool useHttps = false)
            {
                var protocol = useHttps ? "https" : "http";
                return $"{protocol}://{ipAddress}{SnapshotBase}?channel={channel}";
            }

            public static string BuildRtspUrl(string ipAddress, string username, string password, int channel = 1, int subtype = 0)
            {
                return $"rtsp://{username}:{password}@{ipAddress}:554/cam/realmonitor?channel={channel}&subtype={subtype}";
            }

            public static string BuildRtspUrlAlternative(string ipAddress, string username, string password, int channel = 1, string streamType = "main")
            {
                return $"rtsp://{username}:{password}@{ipAddress}:554/live/ch{channel:D2}/{streamType}";
            }
        }

        // Endpoint to HTTP Method mapping
        public static class EndpointMethods
        {
            public static readonly Dictionary<string, (string GetMethod, string SetMethod)> Methods = new()
            {
                { NetworkEth0Config, ("GET", "GET") }, // Dahua uses GET for both read and write
                { NetworkEth0IpAddress, ("GET", "GET") },
                { NtpConfig, ("GET", "GET") },
                { DeviceInfo, ("GET", "N/A") },
                { SystemInfo, ("GET", "N/A") },
                { VideoConfig, ("GET", "GET") },
                { SnapshotChannel1, ("GET", "N/A") }
            };

            public static (string GetMethod, string SetMethod) GetMethodsForEndpoint(string endpoint)
            {
                return Methods.TryGetValue(endpoint, out var methods) ? methods : ("GET", "GET");
            }
        }
    }

    // Content Types
    public static class DahuaContentTypes
    {
        public const string FormUrlEncoded = "application/x-www-form-urlencoded";
        public const string Text = "text/plain";
        public const string Json = "application/json";
    }

    // Response Status Messages
    public static class DahuaStatusMessages
    {
        public const string ConnectionOk = "Connection OK";
        public const string NetworkSettingsSent = "Network settings sent successfully";
        public const string NetworkSettingsError = "Error sending network settings";
        public const string NtpServerSent = "NTP server sent successfully";
        public const string NtpServerError = "Error sending NTP server";
        public const string DstSettingsSent = "DST settings sent successfully";
        public const string DstSettingsError = "Error sending DST settings";
        public const string LoginFailed = "Login failed";
        public const string UnknownConnectionError = "Unknown connection error";
        public const string SendingRequest = "Sending request...";
        public const string RetrievingCurrentConfig = "Retrieving current configuration...";
        public const string ConfigRetrieved = "Configuration retrieved successfully";
        public const string ConfigRetrievalError = "Error retrieving configuration";
        public const string DeviceNotCompatible = "Device is not a Dahua camera";
        public const string AuthenticationRequired = "Authentication required";
        public const string AuthenticationFailed = "Authentication failed";
    }
}