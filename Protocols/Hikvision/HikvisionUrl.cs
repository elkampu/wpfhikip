namespace wpfhikip.Protocols.Hikvision
{
    public static class HikvisionUrl
    {
        // System Endpoints
        public const string SystemCapabilities = "/ISAPI/System/capabilities";
        public const string SystemReboot = "/ISAPI/System/reboot";
        public const string SystemStatus = "/ISAPI/System/status";
        public const string DeviceInfo = "/ISAPI/System/deviceInfo";

        // Network Endpoints
        public const string NetworkInterface = "/ISAPI/System/Network/interfaces/1";
        public const string NetworkInterfaceIpAddress = "/ISAPI/System/Network/interfaces/1/ipAddress";

        // Time Endpoints
        public const string SystemTime = "/ISAPI/System/time";
        public const string NtpServers = "/ISAPI/System/time/ntpServers";

        // Streaming Endpoints
        public const string StreamingChannels = "/ISAPI/Streaming/channels";
        public const string StreamingChannelBase = "/ISAPI/Streaming/channels/{0}01"; // {0} = channel number
        public const string StreamingChannelPicture = "/ISAPI/Streaming/channels/{0}01/picture"; // {0} = channel number

        // Video Input Endpoints
        public const string VideoInputChannels = "/ISAPI/System/Video/inputs/channels";
        public const string VideoInputChannelBase = "/ISAPI/System/Video/inputs/channels/{0}"; // {0} = channel number

        // Recording Endpoints
        public const string RecordingStatus = "/ISAPI/ContentMgmt/record/status/channels/{0}"; // {0} = channel number
        public const string RecordingTracks = "/ISAPI/ContentMgmt/record/tracks/{0}01"; // {0} = channel number

        // PTZ Endpoints
        public const string PtzControl = "/ISAPI/PTZCtrl/channels/{0}/continuous"; // {0} = channel number

        // URL Builders
        public static class UrlBuilders
        {
            public static string BuildGetUrl(string ipAddress, string endpoint, bool useHttps = false, int port = 0)
            {
                var protocol = useHttps ? "https" : "http";
                var defaultPort = useHttps ? 443 : 80;

                // Only include port if it's not the default port for the protocol
                var portSuffix = (port > 0 && port != defaultPort) ? $":{port}" : "";

                return $"{protocol}://{ipAddress}{portSuffix}{endpoint}";
            }

            public static string BuildPutUrl(string ipAddress, string endpoint, bool useHttps = false, int port = 0)
            {
                var protocol = useHttps ? "https" : "http";
                var defaultPort = useHttps ? 443 : 80;

                // Only include port if it's not the default port for the protocol
                var portSuffix = (port > 0 && port != defaultPort) ? $":{port}" : "";

                return $"{protocol}://{ipAddress}{portSuffix}{endpoint}";
            }

            public static string BuildStreamingChannelUrl(string ipAddress, int channel, bool useHttps = false, int port = 0)
            {
                return BuildGetUrl(ipAddress, string.Format(StreamingChannelBase, channel), useHttps, port);
            }

            public static string BuildVideoInputChannelUrl(string ipAddress, int channel, bool useHttps = false, int port = 0)
            {
                return BuildGetUrl(ipAddress, string.Format(VideoInputChannelBase, channel), useHttps, port);
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
                { SystemStatus, ("GET", "N/A") },
                { SystemReboot, ("N/A", "PUT") },
                { StreamingChannels, ("GET", "N/A") },
                { VideoInputChannels, ("GET", "N/A") }
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
        public const string RebootError = "Error during reboot";
    }
}