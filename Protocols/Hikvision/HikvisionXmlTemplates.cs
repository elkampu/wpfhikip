using System.Xml.Linq;

using wpfhikip.Models;

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
        /// Extracts values from response XML into a flat dictionary with proper handling of nested structures
        /// </summary>
        public static Dictionary<string, string> ParseResponseXml(string xmlResponse)
        {
            var result = new Dictionary<string, string>();
            try
            {
                var doc = XDocument.Parse(xmlResponse);
                var root = doc.Root;

                if (root == null) return result;

                // Handle network IP address XML structure
                if (root.Name.LocalName == "IPAddress")
                {
                    ParseNetworkIpAddressXml(root, result);
                }
                // Handle device info XML structure
                else if (root.Name.LocalName == "DeviceInfo")
                {
                    ParseDeviceInfoXml(root, result);
                }
                // Handle capabilities XML structure
                else if (root.Name.LocalName == "DeviceCap")
                {
                    ParseCapabilitiesXml(root, result);
                }
                // Fallback: generic parsing for other XML structures
                else
                {
                    ParseGenericXml(root, result);
                }
            }
            catch (Exception ex)
            {
                // Add error to result for debugging
                result["ParseError"] = $"XML parsing failed: {ex.Message}";
            }
            return result;
        }

        /// <summary>
        /// Parses network IP address configuration XML from Hikvision camera
        /// </summary>
        private static void ParseNetworkIpAddressXml(XElement root, Dictionary<string, string> result)
        {
            // Get the namespace from the root element
            var ns = root.GetDefaultNamespace();

            // Basic network settings - use LocalName to ignore namespace
            var ipVersion = root.Element(ns + "ipVersion")?.Value ?? root.Elements().FirstOrDefault(e => e.Name.LocalName == "ipVersion")?.Value;
            if (!string.IsNullOrEmpty(ipVersion))
                result["ipVersion"] = ipVersion;

            var addressingType = root.Element(ns + "addressingType")?.Value ?? root.Elements().FirstOrDefault(e => e.Name.LocalName == "addressingType")?.Value;
            if (!string.IsNullOrEmpty(addressingType))
                result["addressingType"] = addressingType;

            var ipAddress = root.Element(ns + "ipAddress")?.Value ?? root.Elements().FirstOrDefault(e => e.Name.LocalName == "ipAddress")?.Value;
            if (!string.IsNullOrEmpty(ipAddress))
                result["ipAddress"] = ipAddress;

            var subnetMask = root.Element(ns + "subnetMask")?.Value ?? root.Elements().FirstOrDefault(e => e.Name.LocalName == "subnetMask")?.Value;
            if (!string.IsNullOrEmpty(subnetMask))
                result["subnetMask"] = subnetMask;

            var ipv6Address = root.Element(ns + "ipv6Address")?.Value ?? root.Elements().FirstOrDefault(e => e.Name.LocalName == "ipv6Address")?.Value;
            if (!string.IsNullOrEmpty(ipv6Address))
                result["ipv6Address"] = ipv6Address;

            var bitMask = root.Element(ns + "bitMask")?.Value ?? root.Elements().FirstOrDefault(e => e.Name.LocalName == "bitMask")?.Value;
            if (!string.IsNullOrEmpty(bitMask))
                result["bitMask"] = bitMask;

            // Parse Default Gateway (nested structure) - handle both with and without namespace
            var defaultGateway = root.Element(ns + "DefaultGateway") ?? root.Elements().FirstOrDefault(e => e.Name.LocalName == "DefaultGateway");
            if (defaultGateway != null)
            {
                var gatewayIp = defaultGateway.Element(ns + "ipAddress")?.Value ?? defaultGateway.Elements().FirstOrDefault(e => e.Name.LocalName == "ipAddress")?.Value;
                if (!string.IsNullOrEmpty(gatewayIp))
                    result["defaultGateway"] = gatewayIp;

                var gatewayIpv6 = defaultGateway.Element(ns + "ipv6Address")?.Value ?? defaultGateway.Elements().FirstOrDefault(e => e.Name.LocalName == "ipv6Address")?.Value;
                if (!string.IsNullOrEmpty(gatewayIpv6))
                    result["defaultGatewayIpv6"] = gatewayIpv6;
            }

            // Parse Primary DNS (nested structure) - handle both with and without namespace
            var primaryDns = root.Element(ns + "PrimaryDNS") ?? root.Elements().FirstOrDefault(e => e.Name.LocalName == "PrimaryDNS");
            if (primaryDns != null)
            {
                var dnsIp = primaryDns.Element(ns + "ipAddress")?.Value ?? primaryDns.Elements().FirstOrDefault(e => e.Name.LocalName == "ipAddress")?.Value;
                if (!string.IsNullOrEmpty(dnsIp))
                    result["primaryDNS"] = dnsIp;
            }

            // Parse Secondary DNS (nested structure) - handle both with and without namespace
            var secondaryDns = root.Element(ns + "SecondaryDNS") ?? root.Elements().FirstOrDefault(e => e.Name.LocalName == "SecondaryDNS");
            if (secondaryDns != null)
            {
                var dnsIp = secondaryDns.Element(ns + "ipAddress")?.Value ?? secondaryDns.Elements().FirstOrDefault(e => e.Name.LocalName == "ipAddress")?.Value;
                if (!string.IsNullOrEmpty(dnsIp))
                    result["secondaryDNS"] = dnsIp;
            }

            // Parse IPv6 Mode information - handle both with and without namespace
            var ipv6Mode = root.Element(ns + "Ipv6Mode") ?? root.Elements().FirstOrDefault(e => e.Name.LocalName == "Ipv6Mode");
            if (ipv6Mode != null)
            {
                var ipv6AddressingType = ipv6Mode.Element(ns + "ipV6AddressingType")?.Value ?? ipv6Mode.Elements().FirstOrDefault(e => e.Name.LocalName == "ipV6AddressingType")?.Value;
                if (!string.IsNullOrEmpty(ipv6AddressingType))
                    result["ipv6AddressingType"] = ipv6AddressingType;

                // Parse IPv6 address list
                var ipv6AddressList = ipv6Mode.Element(ns + "ipv6AddressList") ?? ipv6Mode.Elements().FirstOrDefault(e => e.Name.LocalName == "ipv6AddressList");
                if (ipv6AddressList != null)
                {
                    var v6Addresses = ipv6AddressList.Elements().Where(e => e.Name.LocalName == "v6Address").ToList();
                    for (int i = 0; i < v6Addresses.Count; i++)
                    {
                        var v6Address = v6Addresses[i];
                        var id = v6Address.Elements().FirstOrDefault(e => e.Name.LocalName == "id")?.Value;
                        var type = v6Address.Elements().FirstOrDefault(e => e.Name.LocalName == "type")?.Value;
                        var address = v6Address.Elements().FirstOrDefault(e => e.Name.LocalName == "address")?.Value;
                        var mask = v6Address.Elements().FirstOrDefault(e => e.Name.LocalName == "bitMask")?.Value;

                        if (!string.IsNullOrEmpty(id))
                            result[$"ipv6Address_{i}_id"] = id;
                        if (!string.IsNullOrEmpty(type))
                            result[$"ipv6Address_{i}_type"] = type;
                        if (!string.IsNullOrEmpty(address))
                            result[$"ipv6Address_{i}_address"] = address;
                        if (!string.IsNullOrEmpty(mask))
                            result[$"ipv6Address_{i}_bitMask"] = mask;
                    }
                }
            }
        }

        /// <summary>
        /// Parses device information XML from Hikvision camera
        /// </summary>
        private static void ParseDeviceInfoXml(XElement root, Dictionary<string, string> result)
        {
            // Parse all child elements for device info
            foreach (var element in root.Elements())
            {
                if (!element.HasElements && !string.IsNullOrWhiteSpace(element.Value))
                {
                    result[element.Name.LocalName] = element.Value;
                }
            }
        }

        /// <summary>
        /// Parses capabilities XML from Hikvision camera
        /// </summary>
        private static void ParseCapabilitiesXml(XElement root, Dictionary<string, string> result)
        {
            // Parse all descendant elements for capabilities
            foreach (var element in root.Descendants())
            {
                if (!element.HasElements && !string.IsNullOrWhiteSpace(element.Value))
                {
                    // Use full path for nested elements to avoid conflicts
                    var elementPath = GetElementPath(element);
                    result[elementPath] = element.Value;
                }
            }
        }

        /// <summary>
        /// Generic XML parsing for unknown structures (fallback)
        /// </summary>
        private static void ParseGenericXml(XElement root, Dictionary<string, string> result)
        {
            foreach (var element in root.Descendants())
            {
                if (!element.HasElements && !string.IsNullOrWhiteSpace(element.Value))
                {
                    result[element.Name.LocalName] = element.Value;
                }
            }
        }

        /// <summary>
        /// Gets the full path of an XML element for unique identification
        /// </summary>
        private static string GetElementPath(XElement element)
        {
            var path = element.Name.LocalName;
            var parent = element.Parent;

            while (parent != null && parent.Name.LocalName != "root")
            {
                path = parent.Name.LocalName + "." + path;
                parent = parent.Parent;
            }

            return path;
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
        /// Creates a modified XML for PUT request based on GET response using Camera object
        /// </summary>
        public static string CreatePutXmlFromGetResponse(string getResponseXml, Camera camera, string endpoint)
        {
            return endpoint switch
            {
                HikvisionUrl.NetworkInterfaceIpAddress => ModifyNetworkXml(getResponseXml, camera),
                HikvisionUrl.SystemTime => ModifyTimeXml(getResponseXml, camera),
                HikvisionUrl.NtpServers => ModifyNtpXml(getResponseXml, camera),
                _ => throw new ArgumentException($"Unsupported endpoint for XML modification: {endpoint}")
            };
        }

        private static string ModifyNetworkXml(string originalXml, Camera camera)
        {
            try
            {
                var doc = XDocument.Parse(originalXml);
                bool modified = false;

                // Handle IP address (avoid changing gateway IP)
                if (!string.IsNullOrEmpty(camera.NewIP))
                {
                    var ipElements = doc.Descendants()
                        .Where(e => e.Name.LocalName == "ipAddress" &&
                               e.Parent?.Name.LocalName != "DefaultGateway" &&
                               e.Parent?.Name.LocalName != "PrimaryDNS" &&
                               e.Parent?.Name.LocalName != "SecondaryDNS");

                    foreach (var ipElement in ipElements)
                    {
                        if (!ipElement.HasElements)
                        {
                            ipElement.Value = camera.NewIP;
                            modified = true;
                        }
                    }
                }

                // Handle subnet mask
                if (!string.IsNullOrEmpty(camera.NewMask))
                {
                    var maskElements = doc.Descendants().Where(e => e.Name.LocalName == "subnetMask");
                    foreach (var maskElement in maskElements)
                    {
                        if (!maskElement.HasElements)
                        {
                            maskElement.Value = camera.NewMask;
                            modified = true;
                        }
                    }
                }

                // Handle gateway (nested structure)
                if (!string.IsNullOrEmpty(camera.NewGateway))
                {
                    var gatewayElement = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "DefaultGateway");
                    if (gatewayElement != null)
                    {
                        var gatewayIpElement = gatewayElement.Descendants().FirstOrDefault(e => e.Name.LocalName == "ipAddress");
                        if (gatewayIpElement != null)
                        {
                            gatewayIpElement.Value = camera.NewGateway;
                            modified = true;
                        }
                    }
                }

                return modified ? doc.ToString() : originalXml;
            }
            catch (Exception)
            {
                // Fallback to template-based approach
                var newValues = new Dictionary<string, string>();

                if (!string.IsNullOrEmpty(camera.NewIP))
                    newValues["ipAddress"] = camera.NewIP;

                if (!string.IsNullOrEmpty(camera.NewMask))
                    newValues["subnetMask"] = camera.NewMask;

                return ModifyXmlTemplate(originalXml, newValues);
            }
        }

        private static string ModifyTimeXml(string originalXml, Camera camera)
        {
            var newValues = new Dictionary<string, string>
            {
                ["timeMode"] = "NTP"
            };

            return ModifyXmlTemplate(originalXml, newValues);
        }

        private static string ModifyNtpXml(string originalXml, Camera camera)
        {
            if (string.IsNullOrEmpty(camera.NewNTPServer))
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
                        ipElement.Value = camera.NewNTPServer;
                    }
                }

                return doc.ToString();
            }
            catch
            {
                // Fallback to template-based approach
                var newValues = new Dictionary<string, string>
                {
                    ["ipAddress"] = camera.NewNTPServer
                };
                return ModifyXmlTemplate(originalXml, newValues);
            }
        }

        /// <summary>
        /// Compares current and new configurations to determine what needs updating using Camera object
        /// </summary>
        public static bool HasConfigurationChanged(string currentXml, Camera camera, string endpoint)
        {
            var currentValues = ParseResponseXml(currentXml);

            return endpoint switch
            {
                HikvisionUrl.NetworkInterfaceIpAddress => HasNetworkConfigChanged(currentValues, camera),
                HikvisionUrl.NtpServers => HasNtpConfigChanged(currentValues, camera),
                _ => true // Default to updating if we can't determine
            };
        }

        private static bool HasNetworkConfigChanged(Dictionary<string, string> currentValues, Camera camera)
        {
            // Check if IP address changed
            if (!string.IsNullOrEmpty(camera.NewIP) &&
                currentValues.GetValueOrDefault("ipAddress") != camera.NewIP)
                return true;

            // Check if subnet mask changed
            if (!string.IsNullOrEmpty(camera.NewMask) &&
                currentValues.GetValueOrDefault("subnetMask") != camera.NewMask)
                return true;

            // Check if gateway changed
            if (!string.IsNullOrEmpty(camera.NewGateway) &&
                currentValues.GetValueOrDefault("defaultGateway") != camera.NewGateway)
                return true;

            return false;
        }

        private static bool HasNtpConfigChanged(Dictionary<string, string> currentValues, Camera camera)
        {
            return currentValues.GetValueOrDefault("ipAddress") != camera.NewNTPServer;
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