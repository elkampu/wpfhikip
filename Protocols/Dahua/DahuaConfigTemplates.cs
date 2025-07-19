using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace wpfhikip.Protocols.Dahua
{
    public static class DahuaConfigTemplates
    {
        /// <summary>
        /// Parses Dahua configuration response into a dictionary
        /// Dahua responses are in key=value format, one per line
        /// </summary>
        public static Dictionary<string, string> ParseConfigResponse(string configResponse)
        {
            var result = new Dictionary<string, string>();

            if (string.IsNullOrWhiteSpace(configResponse))
                return result;

            try
            {
                var lines = configResponse.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines)
                {
                    if (line.Contains('='))
                    {
                        var parts = line.Split(new[] { '=' }, 2);
                        if (parts.Length == 2)
                        {
                            var key = parts[0].Trim();
                            var value = parts[1].Trim();
                            result[key] = value;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Log error or handle as needed
            }

            return result;
        }

        /// <summary>
        /// Validates if the response contains expected Dahua configuration format
        /// </summary>
        public static bool ValidateConfigResponse(string configResponse)
        {
            if (string.IsNullOrWhiteSpace(configResponse))
                return false;

            // Check for typical Dahua response patterns
            var dahuaIndicators = new[]
            {
                "table.Network.eth0",
                "table.NTP",
                "table.General",
                "table.Locales"
            };

            return dahuaIndicators.Any(indicator =>
                configResponse.Contains(indicator, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Creates URL parameters for network configuration
        /// </summary>
        public static Dictionary<string, string> CreateNetworkConfigParameters(NetworkConfiguration config)
        {
            var parameters = new Dictionary<string, string>();

            if (!string.IsNullOrEmpty(config.NewIP))
                parameters["Network.eth0.IPAddress"] = config.NewIP;

            if (!string.IsNullOrEmpty(config.NewMask))
                parameters["Network.eth0.SubnetMask"] = config.NewMask;

            if (!string.IsNullOrEmpty(config.NewGateway))
                parameters["Network.eth0.DefaultGateway"] = config.NewGateway;

            return parameters;
        }

        /// <summary>
        /// Creates URL parameters for NTP configuration
        /// </summary>
        public static Dictionary<string, string> CreateNtpConfigParameters(NetworkConfiguration config, bool enableNtp = true, int timeZone = 1)
        {
            var parameters = new Dictionary<string, string>();

            if (enableNtp)
                parameters["NTP.Enable"] = "true";

            parameters["NTP.TimeZone"] = timeZone.ToString();

            if (!string.IsNullOrEmpty(config.NewNTPServer))
                parameters["NTP.Address"] = config.NewNTPServer;

            return parameters;
        }

        /// <summary>
        /// Creates URL parameters for DST configuration
        /// </summary>
        public static Dictionary<string, string> CreateDstConfigParameters(bool enableDst = true)
        {
            return new Dictionary<string, string>
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
        }

        /// <summary>
        /// Compares current and new configurations to determine what needs updating
        /// </summary>
        public static bool HasConfigurationChanged(Dictionary<string, string> currentConfig, NetworkConfiguration newConfig, string configType)
        {
            return configType.ToLower() switch
            {
                "network" => HasNetworkConfigChanged(currentConfig, newConfig),
                "ntp" => HasNtpConfigChanged(currentConfig, newConfig),
                _ => true // Default to updating if we can't determine
            };
        }

        private static bool HasNetworkConfigChanged(Dictionary<string, string> currentConfig, NetworkConfiguration newConfig)
        {
            var currentIP = currentConfig.GetValueOrDefault("table.Network.eth0.IPAddress", "");
            var currentMask = currentConfig.GetValueOrDefault("table.Network.eth0.SubnetMask", "");
            var currentGateway = currentConfig.GetValueOrDefault("table.Network.eth0.DefaultGateway", "");

            return (!string.IsNullOrEmpty(newConfig.NewIP) && currentIP != newConfig.NewIP) ||
                   (!string.IsNullOrEmpty(newConfig.NewMask) && currentMask != newConfig.NewMask) ||
                   (!string.IsNullOrEmpty(newConfig.NewGateway) && currentGateway != newConfig.NewGateway);
        }

        private static bool HasNtpConfigChanged(Dictionary<string, string> currentConfig, NetworkConfiguration newConfig)
        {
            var currentNtpServer = currentConfig.GetValueOrDefault("table.NTP.Address", "");
            return !string.IsNullOrEmpty(newConfig.NewNTPServer) && currentNtpServer != newConfig.NewNTPServer;
        }

        /// <summary>
        /// Extracts specific configuration values for easier access
        /// </summary>
        public static class ConfigExtractor
        {
            public static string GetCurrentIP(Dictionary<string, string> config)
            {
                return config.GetValueOrDefault("table.Network.eth0.IPAddress", "");
            }

            public static string GetCurrentSubnetMask(Dictionary<string, string> config)
            {
                return config.GetValueOrDefault("table.Network.eth0.SubnetMask", "");
            }

            public static string GetCurrentGateway(Dictionary<string, string> config)
            {
                return config.GetValueOrDefault("table.Network.eth0.DefaultGateway", "");
            }

            public static string GetCurrentNtpServer(Dictionary<string, string> config)
            {
                return config.GetValueOrDefault("table.NTP.Address", "");
            }

            public static bool GetNtpEnabled(Dictionary<string, string> config)
            {
                var enabledValue = config.GetValueOrDefault("table.NTP.Enable", "false");
                return enabledValue.Equals("true", StringComparison.OrdinalIgnoreCase);
            }

            public static string GetDeviceModel(Dictionary<string, string> config)
            {
                return config.GetValueOrDefault("table.General.MachineName", "Unknown Dahua Device");
            }
        }
    }
}
