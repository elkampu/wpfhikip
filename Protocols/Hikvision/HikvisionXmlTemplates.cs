using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace wpfhikip.Protocols.Hikvision
{
    public static class HikvisionXmlTemplates
    {
        /// <summary>
        /// Validates XML content before sending
        /// </summary>
        public static bool ValidateXml(string xmlContent)
        {
            try
            {
                XDocument.Parse(xmlContent);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Extracts values from response XML into a flat dictionary
        /// </summary>
        public static Dictionary<string, string> ParseResponseXml(string xmlResponse)
        {
            var result = new Dictionary<string, string>();
            try
            {
                var doc = XDocument.Parse(xmlResponse);
                foreach (var element in doc.Descendants())
                {
                    if (!element.HasElements && !string.IsNullOrWhiteSpace(element.Value))
                    {
                        result[element.Name.LocalName] = element.Value;
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
        /// Modifies an XML template with new values while preserving structure
        /// </summary>
        public static string ModifyXmlTemplate(string originalXml, Dictionary<string, string> newValues)
        {
            try
            {
                var doc = XDocument.Parse(originalXml);

                foreach (var kvp in newValues)
                {
                    var elements = doc.Descendants().Where(e => e.Name.LocalName == kvp.Key);
                    foreach (var element in elements)
                    {
                        if (!element.HasElements)
                        {
                            element.Value = kvp.Value;
                        }
                    }
                }

                return doc.ToString();
            }
            catch (Exception)
            {
                throw new InvalidOperationException("Failed to modify XML template");
            }
        }

        /// <summary>
        /// Creates a modified XML for PUT request based on GET response
        /// </summary>
        public static string CreatePutXmlFromGetResponse(string getResponseXml, NetworkConfiguration config, string endpoint)
        {
            return endpoint switch
            {
                HikvisionUrl.NetworkInterfaceIpAddress => ModifyNetworkXml(getResponseXml, config),
                HikvisionUrl.SystemTime => ModifyTimeXml(getResponseXml, config),
                HikvisionUrl.NtpServers => ModifyNtpXml(getResponseXml, config),
                _ => throw new ArgumentException($"Unsupported endpoint for XML modification: {endpoint}")
            };
        }

        private static string ModifyNetworkXml(string originalXml, NetworkConfiguration config)
        {
            var newValues = new Dictionary<string, string>();

            if (!string.IsNullOrEmpty(config.NewIP))
                newValues["ipAddress"] = config.NewIP;

            if (!string.IsNullOrEmpty(config.NewMask))
                newValues["subnetMask"] = config.NewMask;

            if (!string.IsNullOrEmpty(config.NewGateway))
            {
                // Handle nested gateway structure
                var doc = XDocument.Parse(originalXml);
                var gatewayElement = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "DefaultGateway");
                if (gatewayElement != null)
                {
                    var ipElement = gatewayElement.Descendants().FirstOrDefault(e => e.Name.LocalName == "ipAddress");
                    if (ipElement != null)
                    {
                        ipElement.Value = config.NewGateway;
                    }
                }
                return doc.ToString();
            }

            return ModifyXmlTemplate(originalXml, newValues);
        }

        private static string ModifyTimeXml(string originalXml, NetworkConfiguration config)
        {
            var newValues = new Dictionary<string, string>
            {
                ["timeMode"] = "NTP"
            };

            return ModifyXmlTemplate(originalXml, newValues);
        }

        private static string ModifyNtpXml(string originalXml, NetworkConfiguration config)
        {
            if (string.IsNullOrEmpty(config.NewNTPServer))
                return originalXml;

            try
            {
                var doc = XDocument.Parse(originalXml);
                var ntpServerElement = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "NTPServer");

                if (ntpServerElement != null)
                {
                    var ipElement = ntpServerElement.Descendants().FirstOrDefault(e => e.Name.LocalName == "ipAddress");
                    if (ipElement != null)
                    {
                        ipElement.Value = config.NewNTPServer;
                    }
                }

                return doc.ToString();
            }
            catch
            {
                // Fallback to template-based approach
                var newValues = new Dictionary<string, string>
                {
                    ["ipAddress"] = config.NewNTPServer
                };
                return ModifyXmlTemplate(originalXml, newValues);
            }
        }

        /// <summary>
        /// Compares current and new configurations to determine what needs updating
        /// </summary>
        public static bool HasConfigurationChanged(string currentXml, NetworkConfiguration config, string endpoint)
        {
            var currentValues = ParseResponseXml(currentXml);

            return endpoint switch
            {
                HikvisionUrl.NetworkInterfaceIpAddress => HasNetworkConfigChanged(currentValues, config),
                HikvisionUrl.NtpServers => HasNtpConfigChanged(currentValues, config),
                _ => true // Default to updating if we can't determine
            };
        }

        private static bool HasNetworkConfigChanged(Dictionary<string, string> currentValues, NetworkConfiguration config)
        {
            return (currentValues.GetValueOrDefault("ipAddress") != config.NewIP) ||
                   (currentValues.GetValueOrDefault("subnetMask") != config.NewMask) ||
                   (!string.IsNullOrEmpty(config.NewGateway) &&
                    currentValues.GetValueOrDefault("ipAddress") != config.NewGateway); // This may need adjustment for nested gateway
        }

        private static bool HasNtpConfigChanged(Dictionary<string, string> currentValues, NetworkConfiguration config)
        {
            return currentValues.GetValueOrDefault("ipAddress") != config.NewNTPServer;
        }

        /// <summary>
        /// Gets template with parameter substitution (fallback method)
        /// </summary>
        public static string GetTemplate(string templateName, Dictionary<string, string> parameters)
        {
            return templateName switch
            {
                "NetworkConfig" => CreateNetworkConfigXml(parameters),
                "TimeConfig" => CreateTimeConfigXml(parameters),
                "NtpConfig" => CreateNtpConfigXml(parameters),
                _ => throw new ArgumentException($"Unknown template: {templateName}")
            };
        }

        private static string CreateNetworkConfigXml(Dictionary<string, string> parameters)
        {
            return $@"<?xml version='1.0' encoding='UTF-8'?>
<IPAddress version='2.0' xmlns='http://www.hikvision.com/ver20/XMLSchema'>
    <ipVersion>dual</ipVersion>
    <addressingType>static</addressingType>
    <ipAddress>{parameters.GetValueOrDefault("ipAddress", "")}</ipAddress>
    <subnetMask>{parameters.GetValueOrDefault("subnetMask", "")}</subnetMask>
    <ipv6Address>::</ipv6Address>
    <bitMask>0</bitMask>
    <DefaultGateway>
        <ipAddress>{parameters.GetValueOrDefault("gateway", "")}</ipAddress>
        <ipv6Address>::</ipv6Address>
    </DefaultGateway>
    <PrimaryDNS>
        <ipAddress>{parameters.GetValueOrDefault("primaryDns", "8.8.8.8")}</ipAddress>
    </PrimaryDNS>
    <SecondaryDNS>
        <ipAddress>{parameters.GetValueOrDefault("secondaryDns", "8.8.4.4")}</ipAddress>
    </SecondaryDNS>
    <Ipv6Mode>
        <ipV6AddressingType>ra</ipV6AddressingType>
        <ipv6AddressList>
            <v6Address>
                <id>1</id>
                <type>manual</type>
                <address>::</address>
                <bitMask>0</bitMask>
            </v6Address>
        </ipv6AddressList>
    </Ipv6Mode>
</IPAddress>";
        }

        private static string CreateTimeConfigXml(Dictionary<string, string> parameters)
        {
            return $@"<?xml version='1.0' encoding='UTF-8'?>
<Time xmlns='http://www.hikvision.com/ver20/XMLSchema' version='2.0'>
    <timeMode>NTP</timeMode>
    <timeZone>{parameters.GetValueOrDefault("timeZone", "CST-1:00:00DST01:00:00,M3.5.0/02:00:00,M10.5.0/02:00:00")}</timeZone>
</Time>";
        }

        private static string CreateNtpConfigXml(Dictionary<string, string> parameters)
        {
            return $@"<?xml version='1.0' encoding='UTF-8'?>
<NTPServerList xmlns='http://www.hikvision.com/ver20/XMLSchema' version='2.0'>
    <NTPServer xmlns='http://www.hikvision.com/ver20/XMLSchema' version='2.0'>
        <id>{parameters.GetValueOrDefault("serverId", "1")}</id>
        <addressingFormatType>ipaddress</addressingFormatType>
        <ipAddress>{parameters.GetValueOrDefault("ntpServer", "")}</ipAddress>
    </NTPServer>
</NTPServerList>";
        }
    }
}
