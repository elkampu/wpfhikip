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
        /// Updates camera network settings from protocol data
        /// </summary>
        public static void UpdateNetworkInfo(Camera camera, Dictionary<string, object> networkData)
        {
            ArgumentNullException.ThrowIfNull(camera);
            ArgumentNullException.ThrowIfNull(networkData);

            // Ensure Settings is initialized
            camera.Settings ??= new CameraSettings();

            camera.Settings.SubnetMask = GetValueFromMultipleKeys(networkData,
                new[] { "subnetMask", "mask", "SubnetMask", "Network.eth0.SubnetMask" }, null);

            camera.Settings.DefaultGateway = GetValueFromMultipleKeys(networkData,
                new[] { "defaultGateway", "gateway", "DefaultGateway", "Network.DefaultRouter" }, null);

            camera.Settings.DNS1 = GetValueFromMultipleKeys(networkData,
                new[] { "dns1", "primaryDNS", "nameServer1", "Network.NameServer1.Address" }, null);

            camera.Settings.DNS2 = GetValueFromMultipleKeys(networkData,
                new[] { "dns2", "secondaryDNS", "nameServer2", "Network.NameServer2.Address" }, null);

            // Update MAC address if available and not already set
            if (string.IsNullOrEmpty(camera.MacAddress))
            {
                camera.MacAddress = GetValueFromMultipleKeys(networkData,
                    new[] { "macAddress", "MACAddress", "Network.eth0.MACAddress", "Network.Ethernet.MACAddress" }, null);
            }
        }

        /// <summary>
        /// Updates camera video stream information from protocol data
        /// </summary>
        public static void UpdateVideoInfo(Camera camera, Dictionary<string, object> videoData)
        {
            ArgumentNullException.ThrowIfNull(camera);
            ArgumentNullException.ThrowIfNull(videoData);

            // Ensure VideoStream is initialized
            camera.VideoStream ??= new CameraVideoStream();

            // Codec information
            var codecType = GetValueFromMultipleKeys(videoData,
                new[] { "codecType", "videoCodecType", "codec", "Properties.Image.Compression" }, null);
            if (!string.IsNullOrWhiteSpace(codecType))
                camera.VideoStream.CodecType = codecType;

            // Resolution (try combined and separate width/height)
            var resolution = GetValueFromMultipleKeys(videoData,
                new[] { "resolution", "videoResolution", "Properties.Image.Resolution" }, null);

            if (string.IsNullOrWhiteSpace(resolution))
            {
                var width = GetValueFromMultipleKeys(videoData, new[] { "width", "videoResolutionWidth", "resolutionWidth" }, null);
                var height = GetValueFromMultipleKeys(videoData, new[] { "height", "videoResolutionHeight", "resolutionHeight" }, null);

                if (!string.IsNullOrWhiteSpace(width) && !string.IsNullOrWhiteSpace(height))
                    resolution = $"{width}x{height}";
            }

            if (!string.IsNullOrWhiteSpace(resolution))
                camera.VideoStream.Resolution = resolution;

            // Frame rate
            var frameRate = GetValueFromMultipleKeys(videoData,
                new[] { "frameRate", "videoFrameRate", "maxFrameRate", "Properties.Image.FrameRate" }, null);

            if (!string.IsNullOrWhiteSpace(frameRate))
            {
                // Handle Hikvision format (hundredths to fps)
                if (int.TryParse(frameRate, out var frameRateValue) && frameRateValue > 100)
                {
                    var fps = frameRateValue / 100.0;
                    camera.VideoStream.FrameRate = $"{fps:F1} fps";
                }
                else
                {
                    camera.VideoStream.FrameRate = frameRate.Contains("fps", StringComparison.OrdinalIgnoreCase) ? frameRate : $"{frameRate} fps";
                }
            }

            // Bit rate and quality control
            var qualityControlType = GetValueFromMultipleKeys(videoData,
                new[] { "qualityControlType", "videoQualityControlType", "bitrateControl" }, null);

            if (!string.IsNullOrWhiteSpace(qualityControlType))
                camera.VideoStream.QualityControlType = qualityControlType;

            var bitRate = GetValueFromMultipleKeys(videoData,
                new[] { "bitRate", "videoBitRate", "constantBitRate", "vbrUpperCap" }, null);

            if (!string.IsNullOrWhiteSpace(bitRate))
            {
                var displayBitRate = bitRate;
                if (!string.IsNullOrWhiteSpace(qualityControlType))
                    displayBitRate += $" ({qualityControlType})";

                camera.VideoStream.BitRate = displayBitRate;
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