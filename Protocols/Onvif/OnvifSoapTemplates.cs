using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

using wpfhikip.Models;

namespace wpfhikip.Protocols.Onvif
{
    public static class OnvifSoapTemplates
    {
        /// <summary>
        /// Validates SOAP XML content before sending
        /// </summary>
        public static bool ValidateSoap(string soapContent)
        {
            try
            {
                XDocument.Parse(soapContent);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Parses ONVIF SOAP response into a flat dictionary
        /// </summary>
        public static Dictionary<string, string> ParseSoapResponse(string soapResponse)
        {
            var result = new Dictionary<string, string>();
            try
            {
                var doc = XDocument.Parse(soapResponse);

                // Remove namespace prefixes for easier parsing
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
        /// Checks if response contains SOAP fault
        /// </summary>
        public static bool IsSoapFault(string soapResponse)
        {
            if (string.IsNullOrWhiteSpace(soapResponse))
                return false;

            var faultIndicators = new[]
            {
                "soap:Fault",
                "env:Fault",
                "s:Fault",
                "<Fault",
                "faultcode",
                "faultstring",
                "ter:InvalidCredentials",
                "ter:NotAuthorized"
            };

            return faultIndicators.Any(indicator =>
                soapResponse.Contains(indicator, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Extracts SOAP fault string from response
        /// </summary>
        public static string ExtractSoapFaultString(string soapResponse)
        {
            try
            {
                var doc = XDocument.Parse(soapResponse);
                var faultString = doc.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName.Equals("faultstring", StringComparison.OrdinalIgnoreCase))?.Value;

                if (string.IsNullOrEmpty(faultString))
                {
                    // Try to find fault text
                    faultString = doc.Descendants()
                        .FirstOrDefault(e => e.Name.LocalName.Equals("text", StringComparison.OrdinalIgnoreCase))?.Value;
                }

                return faultString ?? "Unknown SOAP fault";
            }
            catch
            {
                return "Unable to parse SOAP fault";
            }
        }

        /// <summary>
        /// Validates if the response is a valid ONVIF response
        /// </summary>
        public static bool ValidateOnvifResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return false;

            var onvifIndicators = new[]
            {
                "onvif.org",
                "tds:",
                "tt:",
                "trt:",
                "GetDeviceInformationResponse",
                "GetCapabilitiesResponse",
                "GetSystemDateAndTimeResponse",
                "GetNetworkInterfacesResponse",
                "GetProfilesResponse",
                "GetVideoEncoderConfigurationResponse",
                "soap:Envelope",
                "s:Envelope",
                "env:Envelope",
                "xmlns:tds",
                "xmlns:tt",
                "xmlns:trt",
                "xmlns:ter"
            };

            return onvifIndicators.Any(indicator =>
                response.Contains(indicator, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Creates SOAP envelope with WS-Security authentication
        /// </summary>
        public static string CreateAuthenticatedSoapEnvelope(string body, string username, string password, string action = "")
        {
            var timestamp = DateTime.UtcNow;
            var nonce = GenerateNonce();
            var passwordDigest = GeneratePasswordDigest(nonce, timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), password);

            return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/"" 
               xmlns:tds=""http://www.onvif.org/ver10/device/wsdl""
               xmlns:trt=""http://www.onvif.org/ver10/media/wsdl""
               xmlns:tt=""http://www.onvif.org/ver10/schema"">
    <soap:Header>
        <wsse:Security xmlns:wsse=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd""
                       xmlns:wsu=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd"">
            <wsse:UsernameToken>
                <wsse:Username>{username}</wsse:Username>
                <wsse:Password Type=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-username-token-profile-1.0#PasswordDigest"">{passwordDigest}</wsse:Password>
                <wsse:Nonce EncodingType=""http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-soap-message-security-1.0#Base64Binary"">{nonce}</wsse:Nonce>
                <wsu:Created>{timestamp:yyyy-MM-ddTHH:mm:ss.fffZ}</wsu:Created>
            </wsse:UsernameToken>
        </wsse:Security>
    </soap:Header>
    <soap:Body>
        {body}
    </soap:Body>
</soap:Envelope>";
        }

        /// <summary>
        /// Creates simple SOAP envelope without authentication
        /// </summary>
        public static string CreateSimpleSoapEnvelope(string body, string action = "")
        {
            return $@"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/"" 
               xmlns:tds=""http://www.onvif.org/ver10/device/wsdl""
               xmlns:trt=""http://www.onvif.org/ver10/media/wsdl""
               xmlns:tt=""http://www.onvif.org/ver10/schema"">
    <soap:Body>
        {body}
    </soap:Body>
</soap:Envelope>";
        }

        /// <summary>
        /// Creates GetDeviceInformation SOAP request
        /// </summary>
        public static string CreateGetDeviceInformationRequest(string username = null, string password = null)
        {
            var body = "<tds:GetDeviceInformation/>";

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                return CreateAuthenticatedSoapEnvelope(body, username, password);
            }
            else
            {
                return CreateSimpleSoapEnvelope(body);
            }
        }

        /// <summary>
        /// Creates GetSystemDateAndTime SOAP request (usually available without authentication)
        /// </summary>
        public static string CreateGetSystemDateAndTimeRequest(string username = null, string password = null)
        {
            var body = "<tds:GetSystemDateAndTime/>";

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                return CreateAuthenticatedSoapEnvelope(body, username, password);
            }
            else
            {
                return CreateSimpleSoapEnvelope(body);
            }
        }

        /// <summary>
        /// Creates GetCapabilities SOAP request
        /// </summary>
        public static string CreateGetCapabilitiesRequest(string username = null, string password = null)
        {
            var body = @"<tds:GetCapabilities>
                <tds:Category>All</tds:Category>
            </tds:GetCapabilities>";

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                return CreateAuthenticatedSoapEnvelope(body, username, password);
            }
            else
            {
                return CreateSimpleSoapEnvelope(body);
            }
        }

        /// <summary>
        /// Creates GetProfiles SOAP request for Media Service
        /// </summary>
        public static string CreateGetProfilesRequest(string username, string password)
        {
            var body = "<trt:GetProfiles/>";
            return CreateAuthenticatedSoapEnvelope(body, username, password);
        }

        /// <summary>
        /// Creates GetVideoEncoderConfiguration SOAP request for Media Service
        /// </summary>
        public static string CreateGetVideoEncoderConfigurationRequest(string configurationToken, string username, string password)
        {
            var body = $@"<trt:GetVideoEncoderConfiguration>
                <trt:ConfigurationToken>{configurationToken}</trt:ConfigurationToken>
            </trt:GetVideoEncoderConfiguration>";
            return CreateAuthenticatedSoapEnvelope(body, username, password);
        }

        /// <summary>
        /// Creates GetStreamUri SOAP request for Media Service
        /// </summary>
        public static string CreateGetStreamUriRequest(string profileToken, string username, string password)
        {
            var body = $@"<trt:GetStreamUri>
                <trt:StreamSetup>
                    <tt:Stream>RTP-Unicast</tt:Stream>
                    <tt:Transport>
                        <tt:Protocol>RTSP</tt:Protocol>
                    </tt:Transport>
                </trt:StreamSetup>
                <trt:ProfileToken>{profileToken}</trt:ProfileToken>
            </trt:GetStreamUri>";
            return CreateAuthenticatedSoapEnvelope(body, username, password);
        }

        /// <summary>
        /// Creates GetNetworkInterfaces SOAP request
        /// </summary>
        public static string CreateGetNetworkInterfacesRequest(string username, string password)
        {
            var body = "<tds:GetNetworkInterfaces/>";
            return CreateAuthenticatedSoapEnvelope(body, username, password);
        }

        /// <summary>
        /// Creates GetDNS SOAP request
        /// </summary>
        public static string CreateGetDNSRequest(string username, string password)
        {
            var body = "<tds:GetDNS/>";
            return CreateAuthenticatedSoapEnvelope(body, username, password);
        }

        /// <summary>
        /// Creates GetNetworkDefaultGateway SOAP request
        /// </summary>
        public static string CreateGetNetworkDefaultGatewayRequest(string username, string password)
        {
            var body = "<tds:GetNetworkDefaultGateway/>";
            return CreateAuthenticatedSoapEnvelope(body, username, password);
        }

        /// <summary>
        /// Creates SetNetworkInterfaces SOAP request using Camera object
        /// </summary>
        public static string CreateSetNetworkInterfacesRequest(Camera camera, string interfaceToken, string username, string password)
        {
            // Validate inputs
            if (string.IsNullOrEmpty(camera.NewIP))
            {
                throw new ArgumentException("Target IP address is required for network configuration");
            }

            var prefixLength = CalculatePrefixLength(camera.NewMask ?? "255.255.255.0");

            // Create the SOAP body with both DHCP disabled AND manual configuration in one request
            // This is the correct ONVIF approach - provide the complete configuration atomically
            var body = $@"<tds:SetNetworkInterfaces>
        <tds:InterfaceToken>{interfaceToken}</tds:InterfaceToken>
        <tds:NetworkInterface>
            <tt:Enabled>true</tt:Enabled>
            <tt:IPv4>
                <tt:Enabled>true</tt:Enabled>
                <tt:Config>
                    <tt:DHCP>false</tt:DHCP>
                    <tt:Manual>
                        <tt:Address>{camera.NewIP}</tt:Address>
                        <tt:PrefixLength>{prefixLength}</tt:PrefixLength>
                    </tt:Manual>
                </tt:Config>
            </tt:IPv4>
        </tds:NetworkInterface>
    </tds:SetNetworkInterfaces>";

            return CreateAuthenticatedSoapEnvelope(body, username, password);
        }

        /// <summary>
        /// Creates a separate SetNetworkInterfaces request for manual IP configuration
        /// This is called after DHCP is disabled to set the manual configuration
        /// </summary>
        public static string CreateSetNetworkInterfacesManualRequest(Camera camera, string interfaceToken, string username, string password)
        {
            if (string.IsNullOrEmpty(camera.NewIP))
            {
                throw new ArgumentException("Target IP address is required for network configuration");
            }

            var prefixLength = CalculatePrefixLength(camera.NewMask ?? "255.255.255.0");

            var body = $@"<tds:SetNetworkInterfaces>
        <tds:InterfaceToken>{interfaceToken}</tds:InterfaceToken>
        <tds:NetworkInterface>
            <tt:Enabled>true</tt:Enabled>
            <tt:IPv4>
                <tt:Enabled>true</tt:Enabled>
                <tt:Config>
                    <tt:Manual>
                        <tt:Address>{camera.NewIP}</tt:Address>
                        <tt:PrefixLength>{prefixLength}</tt:PrefixLength>
                    </tt:Manual>
                </tt:Config>
            </tt:IPv4>
        </tds:NetworkInterface>
    </tds:SetNetworkInterfaces>";

            return CreateAuthenticatedSoapEnvelope(body, username, password);
        }
        /// <summary>
        /// Creates GetNTP SOAP request
        /// </summary>
        public static string CreateGetNtpRequest(string username, string password)
        {
            var body = "<tds:GetNTP/>";
            return CreateAuthenticatedSoapEnvelope(body, username, password);
        }

        /// <summary>
        /// Creates SetNTP SOAP request using Camera object
        /// </summary>
        public static string CreateSetNtpRequest(Camera camera, string username, string password)
        {
            var body = $@"<tds:SetNTP>
        <tds:FromDHCP>false</tds:FromDHCP>
        <tds:NTPManual>
            <tt:Type>IPv4</tt:Type>
            <tt:IPv4Address>{camera.NewNTPServer}</tt:IPv4Address>
        </tds:NTPManual>
    </tds:SetNTP>";

            return CreateAuthenticatedSoapEnvelope(body, username, password);
        }
        /// <summary>
        /// Creates SetNetworkDefaultGateway SOAP request
        /// </summary>
        public static string CreateSetNetworkDefaultGatewayRequest(string gatewayIP, string username, string password)
        {
            var body = $@"<tds:SetNetworkDefaultGateway>
        <tds:IPv4Address>{gatewayIP}</tds:IPv4Address>
    </tds:SetNetworkDefaultGateway>";

            return CreateAuthenticatedSoapEnvelope(body, username, password);
        }

        /// <summary>
        /// Creates SetDNS SOAP request using Camera object
        /// </summary>
        public static string CreateSetDNSRequest(Camera camera, string username, string password)
        {
            var dnsManualEntries = new List<string>();

            if (!string.IsNullOrEmpty(camera.NewDNS1))
            {
                dnsManualEntries.Add($@"<tds:DNSManual>
            <tt:Type>IPv4</tt:Type>
            <tt:IPv4Address>{camera.NewDNS1}</tt:IPv4Address>
        </tds:DNSManual>");
            }

            if (!string.IsNullOrEmpty(camera.NewDNS2))
            {
                dnsManualEntries.Add($@"<tds:DNSManual>
            <tt:Type>IPv4</tt:Type>
            <tt:IPv4Address>{camera.NewDNS2}</tt:IPv4Address>
        </tds:DNSManual>");
            }

            var body = $@"<tds:SetDNS>
        <tds:FromDHCP>false</tds:FromDHCP>
        {string.Join("", dnsManualEntries)}
    </tds:SetDNS>";

            return CreateAuthenticatedSoapEnvelope(body, username, password);
        }
        /// <summary>
        /// Extracts device information from GetDeviceInformation response
        /// </summary>
        public static Dictionary<string, string> ExtractDeviceInfo(string soapResponse)
        {
            var info = new Dictionary<string, string>();
            try
            {
                var doc = XDocument.Parse(soapResponse);

                var deviceInfo = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "GetDeviceInformationResponse");
                if (deviceInfo != null)
                {
                    foreach (var element in deviceInfo.Elements())
                    {
                        info[element.Name.LocalName] = element.Value;
                    }
                }
            }
            catch (Exception)
            {
                // Handle parsing error
            }
            return info;
        }

        /// <summary>
        /// Extracts comprehensive network information from GetNetworkInterfaces response
        /// </summary>
        public static Dictionary<string, string> ExtractNetworkInfo(string soapResponse)
        {
            var networkInfo = new Dictionary<string, string>();
            try
            {
                var doc = XDocument.Parse(soapResponse);

                // Find network interfaces
                var networkInterfaces = doc.Descendants().Where(e => e.Name.LocalName == "NetworkInterfaces");

                foreach (var networkInterface in networkInterfaces)
                {
                    // Get interface token (useful for configuration)
                    var token = networkInterface.Attribute("token")?.Value;
                    if (!string.IsNullOrEmpty(token))
                    {
                        networkInfo["interfaceToken"] = token;
                    }

                    // Extract interface info
                    var infoElement = networkInterface.Descendants().FirstOrDefault(e => e.Name.LocalName == "Info");
                    if (infoElement != null)
                    {
                        var nameElement = infoElement.Descendants().FirstOrDefault(e => e.Name.LocalName == "Name");
                        if (nameElement != null)
                        {
                            networkInfo["interfaceName"] = nameElement.Value;
                        }

                        var hwAddressElement = infoElement.Descendants().FirstOrDefault(e => e.Name.LocalName == "HwAddress");
                        if (hwAddressElement != null)
                        {
                            networkInfo["macAddress"] = hwAddressElement.Value;
                        }
                    }

                    // Extract IPv4 configuration
                    var ipv4Element = networkInterface.Descendants().FirstOrDefault(e => e.Name.LocalName == "IPv4");
                    if (ipv4Element != null)
                    {
                        var enabledElement = ipv4Element.Descendants().FirstOrDefault(e => e.Name.LocalName == "Enabled");
                        if (enabledElement != null)
                        {
                            networkInfo["ipv4Enabled"] = enabledElement.Value;
                        }

                        var configElement = ipv4Element.Descendants().FirstOrDefault(e => e.Name.LocalName == "Config");
                        if (configElement != null)
                        {
                            // Manual configuration
                            var manualElement = configElement.Descendants().FirstOrDefault(e => e.Name.LocalName == "Manual");
                            if (manualElement != null)
                            {
                                var addressElement = manualElement.Descendants().FirstOrDefault(e => e.Name.LocalName == "Address");
                                if (addressElement != null)
                                {
                                    networkInfo["currentIp"] = addressElement.Value;
                                }

                                var prefixLengthElement = manualElement.Descendants().FirstOrDefault(e => e.Name.LocalName == "PrefixLength");
                                if (prefixLengthElement != null)
                                {
                                    var prefixLength = int.Parse(prefixLengthElement.Value);
                                    networkInfo["subnetMask"] = ConvertPrefixLengthToSubnetMask(prefixLength);
                                    networkInfo["prefixLength"] = prefixLengthElement.Value;
                                }
                            }

                            // DHCP configuration
                            var dhcpElement = configElement.Descendants().FirstOrDefault(e => e.Name.LocalName == "DHCP");
                            if (dhcpElement != null)
                            {
                                networkInfo["dhcpEnabled"] = dhcpElement.Value;
                            }
                        }
                    }

                    // Only process first interface for now
                    break;
                }
            }
            catch (Exception)
            {
                // Handle parsing error
            }
            return networkInfo;
        }

        /// <summary>
        /// Extracts DNS information from GetDNS response
        /// </summary>
        public static Dictionary<string, string> ExtractDNSInfo(string soapResponse)
        {
            var dnsInfo = new Dictionary<string, string>();
            try
            {
                var doc = XDocument.Parse(soapResponse);

                var dnsResponse = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "GetDNSResponse");
                if (dnsResponse != null)
                {
                    var dnsInformation = dnsResponse.Descendants().FirstOrDefault(e => e.Name.LocalName == "DNSInformation");
                    if (dnsInformation != null)
                    {
                        // From DHCP
                        var fromDhcpElement = dnsInformation.Descendants().FirstOrDefault(e => e.Name.LocalName == "FromDHCP");
                        if (fromDhcpElement != null)
                        {
                            dnsInfo["dnsFromDhcp"] = fromDhcpElement.Value;
                        }

                        // Manual DNS servers
                        var dnsManualElements = dnsInformation.Descendants().Where(e => e.Name.LocalName == "DNSManual");
                        int dnsIndex = 1;
                        foreach (var dnsManual in dnsManualElements)
                        {
                            var typeElement = dnsManual.Descendants().FirstOrDefault(e => e.Name.LocalName == "Type");
                            var ipv4AddressElement = dnsManual.Descendants().FirstOrDefault(e => e.Name.LocalName == "IPv4Address");

                            if (typeElement?.Value == "IPv4" && ipv4AddressElement != null)
                            {
                                dnsInfo[$"dns{dnsIndex}"] = ipv4AddressElement.Value;
                                dnsIndex++;
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Handle parsing error
            }
            return dnsInfo;
        }

        /// <summary>
        /// Extracts gateway information from GetNetworkDefaultGateway response
        /// </summary>
        public static Dictionary<string, string> ExtractGatewayInfo(string soapResponse)
        {
            var gatewayInfo = new Dictionary<string, string>();
            try
            {
                var doc = XDocument.Parse(soapResponse);

                var gatewayResponse = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "GetNetworkDefaultGatewayResponse");
                if (gatewayResponse != null)
                {
                    var networkGatewayElements = gatewayResponse.Descendants().Where(e => e.Name.LocalName == "NetworkGateway");

                    foreach (var networkGateway in networkGatewayElements)
                    {
                        var ipv4AddressElement = networkGateway.Descendants().FirstOrDefault(e => e.Name.LocalName == "IPv4Address");
                        if (ipv4AddressElement != null)
                        {
                            gatewayInfo["defaultGateway"] = ipv4AddressElement.Value;
                            break; // Use first gateway
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Handle parsing error
            }
            return gatewayInfo;
        }

        /// <summary>
        /// Extracts media profiles from GetProfiles response
        /// </summary>
        public static List<MediaProfile> ExtractMediaProfiles(string soapResponse)
        {
            var profiles = new List<MediaProfile>();
            try
            {
                var doc = XDocument.Parse(soapResponse);

                var profileElements = doc.Descendants().Where(e => e.Name.LocalName == "Profiles");
                foreach (var profileElement in profileElements)
                {
                    var profile = new MediaProfile();

                    // Get profile token
                    profile.Token = profileElement.Attribute("token")?.Value ?? "";

                    // Get profile name
                    var nameElement = profileElement.Descendants().FirstOrDefault(e => e.Name.LocalName == "Name");
                    profile.Name = nameElement?.Value ?? "";

                    // Extract video encoder configuration
                    var videoEncoderConfig = profileElement.Descendants().FirstOrDefault(e => e.Name.LocalName == "VideoEncoderConfiguration");
                    if (videoEncoderConfig != null)
                    {
                        profile.VideoEncoderToken = videoEncoderConfig.Attribute("token")?.Value ?? "";

                        var encodingElement = videoEncoderConfig.Descendants().FirstOrDefault(e => e.Name.LocalName == "Encoding");
                        profile.Encoding = encodingElement?.Value ?? "";

                        var resolutionElement = videoEncoderConfig.Descendants().FirstOrDefault(e => e.Name.LocalName == "Resolution");
                        if (resolutionElement != null)
                        {
                            var widthElement = resolutionElement.Descendants().FirstOrDefault(e => e.Name.LocalName == "Width");
                            var heightElement = resolutionElement.Descendants().FirstOrDefault(e => e.Name.LocalName == "Height");

                            if (widthElement != null && heightElement != null)
                            {
                                profile.Resolution = $"{widthElement.Value}x{heightElement.Value}";
                            }
                        }

                        var frameRateElement = videoEncoderConfig.Descendants().FirstOrDefault(e => e.Name.LocalName == "FrameRateLimit");
                        profile.FrameRate = frameRateElement?.Value ?? "";

                        var bitrateElement = videoEncoderConfig.Descendants().FirstOrDefault(e => e.Name.LocalName == "BitrateLimit");
                        profile.BitRate = bitrateElement?.Value ?? "";

                        var qualityElement = videoEncoderConfig.Descendants().FirstOrDefault(e => e.Name.LocalName == "Quality");
                        profile.Quality = qualityElement?.Value ?? "";

                        var govLengthElement = videoEncoderConfig.Descendants().FirstOrDefault(e => e.Name.LocalName == "GovLength");
                        profile.GovLength = govLengthElement?.Value ?? "";
                    }

                    profiles.Add(profile);
                }
            }
            catch (Exception)
            {
                // Handle parsing error
            }
            return profiles;
        }

        /// <summary>
        /// Extracts stream URI from GetStreamUri response
        /// </summary>
        public static string ExtractStreamUri(string soapResponse)
        {
            try
            {
                var doc = XDocument.Parse(soapResponse);
                var uriElement = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Uri");
                return uriElement?.Value ?? "";
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Extracts network interface token from GetNetworkInterfaces response
        /// </summary>
        public static string ExtractNetworkInterfaceToken(string soapResponse)
        {
            try
            {
                var doc = XDocument.Parse(soapResponse);
                var networkInterface = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "NetworkInterfaces");
                return networkInterface?.Attribute("token")?.Value ?? "eth0"; // Default fallback
            }
            catch
            {
                return "eth0"; // Default fallback
            }
        }

        /// <summary>
        /// Converts CIDR prefix length to subnet mask
        /// </summary>
        public static string ConvertPrefixLengthToSubnetMask(int prefixLength)
        {
            if (prefixLength < 0 || prefixLength > 32)
                return "255.255.255.0"; // Default fallback

            uint mask = 0xFFFFFFFF << (32 - prefixLength);
            return $"{(mask >> 24) & 0xFF}.{(mask >> 16) & 0xFF}.{(mask >> 8) & 0xFF}.{mask & 0xFF}";
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
        /// Generates a random nonce for WS-Security
        /// </summary>
        private static string GenerateNonce()
        {
            var nonce = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(nonce);
            }
            return Convert.ToBase64String(nonce);
        }

        /// <summary>
        /// Generates password digest for WS-Security authentication
        /// </summary>
        private static string GeneratePasswordDigest(string nonce, string created, string password)
        {
            var nonceBytes = Convert.FromBase64String(nonce);
            var createdBytes = Encoding.UTF8.GetBytes(created);
            var passwordBytes = Encoding.UTF8.GetBytes(password);

            var combined = new byte[nonceBytes.Length + createdBytes.Length + passwordBytes.Length];
            nonceBytes.CopyTo(combined, 0);
            createdBytes.CopyTo(combined, nonceBytes.Length);
            passwordBytes.CopyTo(combined, nonceBytes.Length + createdBytes.Length);

            using (var sha1 = SHA1.Create())
            {
                var hash = sha1.ComputeHash(combined);
                return Convert.ToBase64String(hash);
            }
        }

        /// <summary>
        /// Checks if configuration has changed by comparing current and new values using Camera object
        /// </summary>
        public static bool HasConfigurationChanged(Dictionary<string, string> currentConfig, Camera camera, string configType)
        {
            return configType.ToLower() switch
            {
                "network" => HasNetworkConfigChanged(currentConfig, camera),
                "ntp" => HasNtpConfigChanged(currentConfig, camera),
                _ => true // Default to updating if we can't determine
            };
        }

        private static bool HasNetworkConfigChanged(Dictionary<string, string> currentConfig, Camera camera)
        {
            var currentIP = currentConfig.GetValueOrDefault("Address", "");
            var currentMask = currentConfig.GetValueOrDefault("PrefixLength", "");

            return (!string.IsNullOrEmpty(camera.NewIP) && currentIP != camera.NewIP) ||
                   (!string.IsNullOrEmpty(camera.NewMask) &&
                    CalculatePrefixLength(camera.NewMask).ToString() != currentMask);
        }

        private static bool HasNtpConfigChanged(Dictionary<string, string> currentConfig, Camera camera)
        {
            var currentNtpServer = currentConfig.GetValueOrDefault("IPv4Address", "");
            return !string.IsNullOrEmpty(camera.NewNTPServer) && currentNtpServer != camera.NewNTPServer;
        }
    }

    /// <summary>
    /// Represents an ONVIF media profile with video configuration
    /// </summary>
    public sealed class MediaProfile
    {
        public string Token { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string VideoEncoderToken { get; set; } = string.Empty;
        public string Encoding { get; set; } = string.Empty;
        public string Resolution { get; set; } = string.Empty;
        public string FrameRate { get; set; } = string.Empty;
        public string BitRate { get; set; } = string.Empty;
        public string Quality { get; set; } = string.Empty;
        public string GovLength { get; set; } = string.Empty;
    }
}