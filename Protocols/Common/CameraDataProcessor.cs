using wpfhikip.Models;

namespace wpfhikip.Protocols.Common
{
    /// <summary>
    /// Unified processor for updating camera data from protocol responses
    /// </summary>
    public static class CameraDataProcessor
    {
        /// <summary>
        /// Updates camera device information from protocol data
        /// </summary>
        public static void UpdateDeviceInfo(Camera camera, Dictionary<string, object> deviceData, string protocolName)
        {
            ArgumentNullException.ThrowIfNull(camera);
            ArgumentNullException.ThrowIfNull(deviceData);

            // Try multiple possible key variations for each field
            camera.Manufacturer = GetValueFromMultipleKeys(deviceData,
                new[] { "manufacturer", "brand", "Brand.Brand", "Properties.System.Brand" }, protocolName);

            camera.Model = GetValueFromMultipleKeys(deviceData,
                new[] { "model", "deviceName", "deviceType", "hardwareID", "Properties.System.HardwareID", "Properties.System.ProductName" }, null);

            camera.Firmware = GetValueFromMultipleKeys(deviceData,
                new[] { "firmware", "firmwareVersion", "version", "Properties.Firmware.Version", "Properties.System.Version" }, null);

            camera.SerialNumber = GetValueFromMultipleKeys(deviceData,
                new[] { "serialNumber", "serial", "Properties.System.SerialNumber", "Properties.System.Serial" }, null);

            camera.MacAddress = GetValueFromMultipleKeys(deviceData,
                new[] { "macAddress", "MACAddress", "Network.eth0.MACAddress", "Network.Ethernet.MACAddress" }, null);
        }

        /// <summary>
        /// Updates camera network settings from protocol data with enhanced ONVIF support
        /// </summary>
        public static void UpdateNetworkInfo(Camera camera, Dictionary<string, object> networkData, bool preserveUserTargetValues = false)
        {
            ArgumentNullException.ThrowIfNull(camera);
            ArgumentNullException.ThrowIfNull(networkData);

            // Ensure Settings is initialized
            camera.Settings ??= new CameraSettings();

            // Store user-entered target values if we need to preserve them
            string? userNewIP = null;
            string? userNewMask = null;
            string? userNewGateway = null;
            string? userNewDNS1 = null;
            string? userNewDNS2 = null;
            string? userNewNTPServer = null;

            if (preserveUserTargetValues)
            {
                userNewIP = camera.NewIP;
                userNewMask = camera.NewMask;
                userNewGateway = camera.NewGateway;
                userNewDNS1 = camera.NewDNS1;
                userNewDNS2 = camera.NewDNS2;
                userNewNTPServer = camera.NewNTPServer;
            }

            // Update current network information from camera (these are for display in info dialog)
            var subnetMask = GetValueFromMultipleKeys(networkData,
                new[] {
                    "subnetMask",           // ONVIF extracted subnet mask from prefix length conversion
                    "mask",
                    "SubnetMask",
                    "Network.eth0.SubnetMask"
                }, null);

            if (!string.IsNullOrEmpty(subnetMask))
            {
                camera.CurrentSubnetMask = subnetMask;

                // Only update target values if not preserving user input or user hasn't entered anything
                if (!preserveUserTargetValues || string.IsNullOrEmpty(userNewMask))
                {
                    camera.Settings.SubnetMask = subnetMask;
                }
            }

            var defaultGateway = GetValueFromMultipleKeys(networkData,
                new[] {
                    "defaultGateway",       // ONVIF extracted gateway from GetNetworkDefaultGateway
                    "gateway",
                    "DefaultGateway",
                    "Network.DefaultRouter"
                }, null);

            if (!string.IsNullOrEmpty(defaultGateway))
            {
                camera.CurrentGateway = defaultGateway;

                if (!preserveUserTargetValues || string.IsNullOrEmpty(userNewGateway))
                {
                    camera.Settings.DefaultGateway = defaultGateway;
                }
            }

            var dns1 = GetValueFromMultipleKeys(networkData,
                new[] {
                    "dns1",                 // ONVIF extracted DNS1 from GetDNS
                    "primaryDNS",
                    "nameServer1",
                    "Network.NameServer1.Address"
                }, null);

            if (!string.IsNullOrEmpty(dns1))
            {
                camera.CurrentDNS1 = dns1;

                if (!preserveUserTargetValues || string.IsNullOrEmpty(userNewDNS1))
                {
                    camera.Settings.DNS1 = dns1;
                }
            }

            var dns2 = GetValueFromMultipleKeys(networkData,
                new[] {
                    "dns2",                 // ONVIF extracted DNS2 from GetDNS
                    "secondaryDNS",
                    "nameServer2",
                    "Network.NameServer2.Address"
                }, null);

            if (!string.IsNullOrEmpty(dns2))
            {
                camera.CurrentDNS2 = dns2;

                if (!preserveUserTargetValues || string.IsNullOrEmpty(userNewDNS2))
                {
                    camera.Settings.DNS2 = dns2;
                }
            }

            // Current IP address - this should update CurrentIP, not target IP
            var currentIp = GetValueFromMultipleKeys(networkData,
                new[] {
                    "currentIp",            // ONVIF extracted current IP from GetNetworkInterfaces
                    "ipAddress",
                    "address",
                    "Address",
                    "Network.eth0.Address"
                }, null);

            // Update current IP if we found one and camera doesn't have it set
            if (!string.IsNullOrEmpty(currentIp) && string.IsNullOrEmpty(camera.CurrentIP))
            {
                camera.CurrentIP = currentIp;
            }

            // Only update target IP if not preserving user values or user hasn't set one
            if (!string.IsNullOrEmpty(currentIp))
            {
                if (!preserveUserTargetValues || string.IsNullOrEmpty(userNewIP))
                {
                    camera.Settings.IPAddress = currentIp;
                }
            }

            // Update MAC address if available and not already set
            if (string.IsNullOrEmpty(camera.MacAddress))
            {
                camera.MacAddress = GetValueFromMultipleKeys(networkData,
                    new[] {
                        "macAddress",           // ONVIF extracted MAC from GetNetworkInterfaces
                        "MACAddress",
                        "hwAddress",            // ONVIF hardware address field
                        "Network.eth0.MACAddress",
                        "Network.Ethernet.MACAddress"
                    }, null);
            }

            // Restore user-entered target values if we were preserving them
            if (preserveUserTargetValues)
            {
                if (!string.IsNullOrEmpty(userNewIP)) camera.Settings.IPAddress = userNewIP;
                if (!string.IsNullOrEmpty(userNewMask)) camera.Settings.SubnetMask = userNewMask;
                if (!string.IsNullOrEmpty(userNewGateway)) camera.Settings.DefaultGateway = userNewGateway;
                if (!string.IsNullOrEmpty(userNewDNS1)) camera.Settings.DNS1 = userNewDNS1;
                if (!string.IsNullOrEmpty(userNewDNS2)) camera.Settings.DNS2 = userNewDNS2;
                if (!string.IsNullOrEmpty(userNewNTPServer)) camera.Settings.NTPServer = userNewNTPServer;
            }

            // Handle ONVIF-specific additional network information
            var interfaceName = GetValueFromMultipleKeys(networkData,
                new[] { "interfaceName" }, null);

            var interfaceToken = GetValueFromMultipleKeys(networkData,
                new[] { "interfaceToken" }, null);

            var dhcpEnabled = GetValueFromMultipleKeys(networkData,
                new[] { "dhcpEnabled" }, null);

            var ipv4Enabled = GetValueFromMultipleKeys(networkData,
                new[] { "ipv4Enabled" }, null);

            var dnsFromDhcp = GetValueFromMultipleKeys(networkData,
                new[] { "dnsFromDhcp" }, null);

            var prefixLength = GetValueFromMultipleKeys(networkData,
                new[] { "prefixLength" }, null);

            // Log network configuration details for debugging
            if (!string.IsNullOrEmpty(interfaceName) || !string.IsNullOrEmpty(dhcpEnabled))
            {
                var details = new List<string>();
                if (!string.IsNullOrEmpty(interfaceName)) details.Add($"Interface: {interfaceName}");
                if (!string.IsNullOrEmpty(dhcpEnabled)) details.Add($"DHCP: {dhcpEnabled}");
                if (!string.IsNullOrEmpty(ipv4Enabled)) details.Add($"IPv4: {ipv4Enabled}");
                if (!string.IsNullOrEmpty(dnsFromDhcp)) details.Add($"DNS from DHCP: {dnsFromDhcp}");
                if (!string.IsNullOrEmpty(prefixLength)) details.Add($"Prefix Length: {prefixLength}");

                camera.AddProtocolLog("Network Info", "ONVIF Details",
                    string.Join(", ", details), ProtocolLogLevel.Info);
            }
        }

        /// <summary>
        /// Updates camera video stream information from protocol data with enhanced ONVIF support
        /// </summary>
        public static void UpdateVideoInfo(Camera camera, Dictionary<string, object> videoData)
        {
            ArgumentNullException.ThrowIfNull(camera);
            ArgumentNullException.ThrowIfNull(videoData);

            // Ensure VideoStream is initialized
            camera.VideoStream ??= new CameraVideoStream();

            // Update stream URLs if available
            var streamUri1 = GetValueFromMultipleKeys(videoData,
                new[] { "streamUri1", "mainStreamUri" }, null);
            if (!string.IsNullOrEmpty(streamUri1))
            {
                camera.VideoStream.MainStreamUrl = streamUri1;
            }

            var streamUri2 = GetValueFromMultipleKeys(videoData,
                new[] { "streamUri2", "subStreamUri" }, null);
            if (!string.IsNullOrEmpty(streamUri2))
            {
                camera.VideoStream.SubStreamUrl = streamUri2;
            }

            // Codec information - check ONVIF-specific keys first
            var codecType = GetValueFromMultipleKeys(videoData,
                new[] {
                    "codecType",            // ONVIF extracted codec
                    "encoding",             // ONVIF encoding type from profiles
                    "videoCodecType",
                    "codec",
                    "Properties.Image.Compression"
                }, null);
            if (!string.IsNullOrWhiteSpace(codecType))
                camera.VideoStream.CodecType = codecType;

            // Resolution - check ONVIF-specific keys first
            var resolution = GetValueFromMultipleKeys(videoData,
                new[] {
                    "resolution",           // ONVIF extracted resolution (WxH format)
                    "videoResolution",
                    "Properties.Image.Resolution"
                }, null);

            if (string.IsNullOrWhiteSpace(resolution))
            {
                var width = GetValueFromMultipleKeys(videoData, new[] { "width", "videoResolutionWidth", "resolutionWidth" }, null);
                var height = GetValueFromMultipleKeys(videoData, new[] { "height", "videoResolutionHeight", "resolutionHeight" }, null);

                if (!string.IsNullOrWhiteSpace(width) && !string.IsNullOrWhiteSpace(height))
                    resolution = $"{width}x{height}";
            }

            if (!string.IsNullOrWhiteSpace(resolution))
                camera.VideoStream.Resolution = resolution;

            // Frame rate - check ONVIF-specific keys first
            var frameRate = GetValueFromMultipleKeys(videoData,
                new[] {
                    "frameRate",            // ONVIF extracted frame rate
                    "frameRateLimit",       // ONVIF frame rate limit from encoder config
                    "videoFrameRate",
                    "maxFrameRate",
                    "Properties.Image.FrameRate"
                }, null);

            if (!string.IsNullOrWhiteSpace(frameRate))
            {
                // Handle different frame rate formats
                if (int.TryParse(frameRate, out var frameRateValue))
                {
                    // Handle Hikvision format (hundredths to fps)
                    if (frameRateValue > 100)
                    {
                        var fps = frameRateValue / 100.0;
                        camera.VideoStream.FrameRate = $"{fps:F1} fps";
                    }
                    else
                    {
                        camera.VideoStream.FrameRate = $"{frameRateValue} fps";
                    }
                }
                else
                {
                    // Handle already formatted frame rate
                    camera.VideoStream.FrameRate = frameRate.Contains("fps", StringComparison.OrdinalIgnoreCase) ? frameRate : $"{frameRate} fps";
                }
            }

            // Bit rate and quality control - check ONVIF-specific keys first
            var qualityControlType = GetValueFromMultipleKeys(videoData,
                new[] {
                    "qualityControlType",   // Generated from ONVIF analysis (CBR/VBR)
                    "videoQualityControlType",
                    "bitrateControl"
                }, null);

            if (!string.IsNullOrWhiteSpace(qualityControlType))
                camera.VideoStream.QualityControlType = qualityControlType;

            var bitRate = GetValueFromMultipleKeys(videoData,
                new[] {
                    "bitRate",              // ONVIF extracted bitrate
                    "bitrateLimit",         // ONVIF bitrate limit from encoder config
                    "videoBitRate",
                    "constantBitRate",
                    "vbrUpperCap"
                }, null);

            if (!string.IsNullOrWhiteSpace(bitRate))
            {
                var displayBitRate = bitRate;

                // Format bitrate with units if not already present
                if (int.TryParse(bitRate, out var bitrateValue))
                {
                    // Convert to appropriate units (Kbps/Mbps)
                    if (bitrateValue >= 1000000)
                    {
                        displayBitRate = $"{bitrateValue / 1000000.0:F1} Mbps";
                    }
                    else if (bitrateValue >= 1000)
                    {
                        displayBitRate = $"{bitrateValue / 1000.0:F0} Kbps";
                    }
                    else
                    {
                        displayBitRate = $"{bitrateValue} bps";
                    }
                }

                // Add quality control type if available
                if (!string.IsNullOrWhiteSpace(qualityControlType))
                    displayBitRate += $" ({qualityControlType})";

                camera.VideoStream.BitRate = displayBitRate;
            }

            // Handle additional ONVIF video parameters
            var quality = GetValueFromMultipleKeys(videoData,
                new[] { "quality" }, null);

            var govLength = GetValueFromMultipleKeys(videoData,
                new[] { "govLength" }, null);

            var profileName = GetValueFromMultipleKeys(videoData,
                new[] { "profileName" }, null);

            var profileToken = GetValueFromMultipleKeys(videoData,
                new[] { "profileToken" }, null);

            // Log additional video details for debugging
            if (!string.IsNullOrEmpty(quality) || !string.IsNullOrEmpty(govLength) || !string.IsNullOrEmpty(profileName))
            {
                var details = new List<string>();
                if (!string.IsNullOrEmpty(profileName)) details.Add($"Profile: {profileName}");
                if (!string.IsNullOrEmpty(quality)) details.Add($"Quality: {quality}");
                if (!string.IsNullOrEmpty(govLength)) details.Add($"GOP Length: {govLength}");

                camera.AddProtocolLog("Video Info", "ONVIF Details",
                    string.Join(", ", details), ProtocolLogLevel.Info);
            }
        }

        /// <summary>
        /// Gets a value from multiple possible keys in the data dictionary
        /// </summary>
        private static string? GetValueFromMultipleKeys(Dictionary<string, object> data, string[] possibleKeys, string? defaultValue)
        {
            foreach (var key in possibleKeys)
            {
                // Try exact match first
                if (data.TryGetValue(key, out var exactValue) && exactValue != null)
                {
                    var stringValue = exactValue.ToString();
                    if (!string.IsNullOrWhiteSpace(stringValue))
                        return stringValue;
                }

                // Try case-insensitive match
                var caseInsensitiveMatch = data.FirstOrDefault(kvp =>
                    string.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase) && kvp.Value != null);

                if (caseInsensitiveMatch.Key != null)
                {
                    var stringValue = caseInsensitiveMatch.Value.ToString();
                    if (!string.IsNullOrWhiteSpace(stringValue))
                        return stringValue;
                }
            }

            return defaultValue;
        }
    }
}