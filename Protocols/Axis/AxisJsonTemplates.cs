using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using wpfhikip.Models;

namespace wpfhikip.Protocols.Axis
{
    public static class AxisJsonTemplates
    {
        /// <summary>
        /// Validates JSON content before sending
        /// </summary>
        public static bool ValidateJson(string jsonContent)
        {
            try
            {
                JsonDocument.Parse(jsonContent);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Parses Axis JSON response into a dictionary
        /// </summary>
        public static Dictionary<string, object> ParseJsonResponse(string jsonResponse)
        {
            var result = new Dictionary<string, object>();

            if (string.IsNullOrWhiteSpace(jsonResponse))
                return result;

            try
            {
                using var document = JsonDocument.Parse(jsonResponse);
                ParseJsonElement(document.RootElement, result, "");
            }
            catch (Exception)
            {
                // Log error or handle as needed
            }

            return result;
        }

        private static void ParseJsonElement(JsonElement element, Dictionary<string, object> result, string prefix)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        var key = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";
                        ParseJsonElement(property.Value, result, key);
                    }
                    break;
                case JsonValueKind.Array:
                    var index = 0;
                    foreach (var item in element.EnumerateArray())
                    {
                        var key = $"{prefix}[{index}]";
                        ParseJsonElement(item, result, key);
                        index++;
                    }
                    break;
                default:
                    result[prefix] = element.ToString();
                    break;
            }
        }

        /// <summary>
        /// Validates if the response contains expected Axis format
        /// </summary>
        public static bool ValidateAxisResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return false;

            // Check for typical Axis response patterns
            var axisIndicators = new[]
            {
                "apiVersion",
                "method",
                "params",
                "axis",
                "Network.IPAddress",
                "Properties.System"
            };

            return axisIndicators.Any(indicator =>
                response.Contains(indicator, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Creates JSON for getting network information
        /// </summary>
        public static string CreateGetNetworkInfoJson()
        {
            var request = new
            {
                apiVersion = "1.0",
                context = "NetworkConfig",
                method = AxisUrl.JsonMethods.GetNetworkInfo
            };

            return JsonSerializer.Serialize(request);
        }

        /// <summary>
        /// Creates JSON for getting IPv4 address configuration
        /// </summary>
        public static string CreateGetIPv4ConfigJson()
        {
            var request = new
            {
                apiVersion = "1.0",
                context = "NetworkConfig",
                method = AxisUrl.JsonMethods.GetIPv4AddressConfiguration,
                @params = new
                {
                    deviceName = "eth0"
                }
            };

            return JsonSerializer.Serialize(request);
        }

        /// <summary>
        /// Creates JSON for setting IPv4 address configuration using Camera object
        /// </summary>
        public static string CreateSetIPv4ConfigJson(Camera camera)
        {
            var prefixLength = CalculatePrefixLength(camera.NewMask ?? "255.255.255.0");

            var request = new
            {
                apiVersion = "1.0",
                context = "SetIPv4Config",
                method = AxisUrl.JsonMethods.SetIPv4AddressConfiguration,
                @params = new
                {
                    deviceName = "eth0",
                    configurationMode = "static",
                    staticDefaultRouter = camera.NewGateway ?? "",
                    staticAddressConfigurations = new[]
                    {
                        new
                        {
                            address = camera.NewIP ?? "",
                            prefixLength = prefixLength
                        }
                    }
                }
            };

            return JsonSerializer.Serialize(request);
        }

        /// <summary>
        /// Compares current and new configurations using Camera object
        /// </summary>
        public static bool HasConfigurationChanged(Dictionary<string, object> currentConfig, Camera camera)
        {
            return HasNetworkConfigChanged(currentConfig, camera);
        }

        private static bool HasNetworkConfigChanged(Dictionary<string, object> currentConfig, Camera camera)
        {
            // This would need to be implemented based on the actual response structure from Axis
            // For now, we'll return true to always attempt the update
            return true;
        }

        /// <summary>
        /// Converts subnet mask to CIDR prefix length
        /// </summary>
        public static int CalculatePrefixLength(string subnetMask)
        {
            try
            {
                var octets = subnetMask.Split('.').Select(int.Parse).ToArray();
                var binaryStr = string.Join("", octets.Select(o => Convert.ToString(o, 2).PadLeft(8, '0')));
                return binaryStr.Count(c => c == '1');
            }
            catch
            {
                return 24; // Default to /24 if conversion fails
            }
        }

        /// <summary>
        /// Extracts specific configuration values for easier access
        /// </summary>
        public static class ConfigExtractor
        {
            public static string GetCurrentIP(Dictionary<string, object> config)
            {
                // This would need to be implemented based on actual Axis response structure
                return config.GetValueOrDefault("data.staticAddressConfigurations[0].address", "").ToString();
            }

            public static string GetCurrentSubnetMask(Dictionary<string, object> config)
            {
                // Convert prefix length back to subnet mask if needed
                var prefixLength = config.GetValueOrDefault("data.staticAddressConfigurations[0].prefixLength", "24").ToString();
                if (int.TryParse(prefixLength, out var length))
                {
                    return ConvertPrefixLengthToSubnetMask(length);
                }
                return "255.255.255.0";
            }

            public static string GetCurrentGateway(Dictionary<string, object> config)
            {
                return config.GetValueOrDefault("data.staticDefaultRouter", "").ToString();
            }

            public static string GetDeviceModel(Dictionary<string, object> config)
            {
                return config.GetValueOrDefault("Properties.System.ProductName", "Unknown Axis Device").ToString();
            }

            private static string ConvertPrefixLengthToSubnetMask(int prefixLength)
            {
                var mask = ~(0xffffffff >> prefixLength);
                return $"{(mask >> 24) & 0xff}.{(mask >> 16) & 0xff}.{(mask >> 8) & 0xff}.{mask & 0xff}";
            }
        }
    }
}