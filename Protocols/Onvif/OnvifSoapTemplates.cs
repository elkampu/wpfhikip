using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

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
            return soapResponse.Contains("soap:Fault") || soapResponse.Contains("env:Fault") ||
                   soapResponse.Contains("Fault", StringComparison.OrdinalIgnoreCase);
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
                "GetDeviceInformationResponse",
                "GetCapabilitiesResponse",
                "soap:Envelope",
                "env:Envelope"
            };

            return onvifIndicators.Any(indicator =>
                response.Contains(indicator, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Creates SOAP envelope with WS-Security authentication
        /// </summary>
        public static string CreateAuthenticatedSoapEnvelope(string body, string username, string password, string action)
        {
            var timestamp = DateTime.UtcNow;
            var nonce = GenerateNonce();
            var passwordDigest = GeneratePasswordDigest(nonce, timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), password);

            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<soap:Envelope xmlns:soap=""http://www.w3.org/2003/05/soap-envelope"" 
               xmlns:tds=""http://www.onvif.org/ver10/device/wsdl""
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
        <wsa:Action xmlns:wsa=""http://www.w3.org/2005/08/addressing"">{action}</wsa:Action>
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
            var actionHeader = !string.IsNullOrEmpty(action)
                ? $@"<wsa:Action xmlns:wsa=""http://www.w3.org/2005/08/addressing"">{action}</wsa:Action>"
                : "";

            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<soap:Envelope xmlns:soap=""http://www.w3.org/2003/05/soap-envelope"" 
               xmlns:tds=""http://www.onvif.org/ver10/device/wsdl""
               xmlns:tt=""http://www.onvif.org/ver10/schema"">
    <soap:Header>
        {actionHeader}
    </soap:Header>
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
                return CreateAuthenticatedSoapEnvelope(body, username, password, OnvifUrl.SoapActions.GetDeviceInformation);
            }
            else
            {
                return CreateSimpleSoapEnvelope(body, OnvifUrl.SoapActions.GetDeviceInformation);
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
                return CreateAuthenticatedSoapEnvelope(body, username, password, OnvifUrl.SoapActions.GetCapabilities);
            }
            else
            {
                return CreateSimpleSoapEnvelope(body, OnvifUrl.SoapActions.GetCapabilities);
            }
        }

        /// <summary>
        /// Creates GetNetworkInterfaces SOAP request
        /// </summary>
        public static string CreateGetNetworkInterfacesRequest(string username, string password)
        {
            var body = "<tds:GetNetworkInterfaces/>";
            return CreateAuthenticatedSoapEnvelope(body, username, password, OnvifUrl.SoapActions.GetNetworkInterfaces);
        }

        /// <summary>
        /// Creates SetNetworkInterfaces SOAP request
        /// </summary>
        public static string CreateSetNetworkInterfacesRequest(NetworkConfiguration config, string interfaceToken, string username, string password)
        {
            var body = $@"<tds:SetNetworkInterfaces>
                <tds:InterfaceToken>{interfaceToken}</tds:InterfaceToken>
                <tds:NetworkInterface>
                    <tt:Enabled>true</tt:Enabled>
                    <tt:IPv4>
                        <tt:Enabled>true</tt:Enabled>
                        <tt:Config>
                            <tt:Manual>
                                <tt:Address>{config.NewIP}</tt:Address>
                                <tt:PrefixLength>{CalculatePrefixLength(config.NewMask ?? "255.255.255.0")}</tt:PrefixLength>
                            </tt:Manual>
                            <tt:DHCP>false</tt:DHCP>
                        </tt:Config>
                    </tt:IPv4>
                </tds:NetworkInterface>
            </tds:SetNetworkInterfaces>";

            return CreateAuthenticatedSoapEnvelope(body, username, password, OnvifUrl.SoapActions.SetNetworkInterfaces);
        }

        /// <summary>
        /// Creates GetNTP SOAP request
        /// </summary>
        public static string CreateGetNtpRequest(string username, string password)
        {
            var body = "<tds:GetNTP/>";
            return CreateAuthenticatedSoapEnvelope(body, username, password, OnvifUrl.SoapActions.GetNTP);
        }

        /// <summary>
        /// Creates SetNTP SOAP request
        /// </summary>
        public static string CreateSetNtpRequest(NetworkConfiguration config, string username, string password)
        {
            var body = $@"<tds:SetNTP>
                <tds:FromDHCP>false</tds:FromDHCP>
                <tds:NTPManual>
                    <tt:Type>IPv4</tt:Type>
                    <tt:IPv4Address>{config.NewNTPServer}</tt:IPv4Address>
                </tds:NTPManual>
            </tds:SetNTP>";

            return CreateAuthenticatedSoapEnvelope(body, username, password, OnvifUrl.SoapActions.SetNTP);
        }

        /// <summary>
        /// Creates WS-Discovery Probe request for ONVIF device discovery
        /// </summary>
        public static string CreateProbeRequest()
        {
            var uuid = Guid.NewGuid().ToString();

            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<soap:Envelope xmlns:soap=""http://www.w3.org/2003/05/soap-envelope""
               xmlns:wsa=""http://www.w3.org/2005/08/addressing""
               xmlns:tds=""http://www.onvif.org/ver10/device/wsdl""
               xmlns:dn=""http://www.onvif.org/ver10/network/wsdl"">
    <soap:Header>
        <wsa:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</wsa:Action>
        <wsa:MessageID>urn:uuid:{uuid}</wsa:MessageID>
        <wsa:ReplyTo>
            <wsa:Address>http://www.w3.org/2005/08/addressing/anonymous</wsa:Address>
        </wsa:ReplyTo>
        <wsa:To>urn:schemas-xmlsoap-org:ws:2005:04:discovery</wsa:To>
    </soap:Header>
    <soap:Body>
        <Probe xmlns=""http://schemas.xmlsoap.org/ws/2005/04/discovery"">
            <d:Types xmlns:d=""http://schemas.xmlsoap.org/ws/2005/04/discovery"">dn:NetworkVideoTransmitter</d:Types>
        </Probe>
    </soap:Body>
</soap:Envelope>";
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
                var ns = XNamespace.Get("http://www.onvif.org/ver10/schema");

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
        /// Extracts network interface information from GetNetworkInterfaces response
        /// </summary>
        public static string ExtractNetworkInterfaceToken(string soapResponse)
        {
            try
            {
                var doc = XDocument.Parse(soapResponse);
                var tokenElement = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "token");
                return tokenElement?.Value ?? "eth0"; // Default fallback
            }
            catch
            {
                return "eth0"; // Default fallback
            }
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
        /// Checks if configuration has changed by comparing current and new values
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
            var currentIP = currentConfig.GetValueOrDefault("Address", "");
            var currentMask = currentConfig.GetValueOrDefault("PrefixLength", "");

            return (!string.IsNullOrEmpty(newConfig.NewIP) && currentIP != newConfig.NewIP) ||
                   (!string.IsNullOrEmpty(newConfig.NewMask) &&
                    CalculatePrefixLength(newConfig.NewMask).ToString() != currentMask);
        }

        private static bool HasNtpConfigChanged(Dictionary<string, string> currentConfig, NetworkConfiguration newConfig)
        {
            var currentNtpServer = currentConfig.GetValueOrDefault("IPv4Address", "");
            return !string.IsNullOrEmpty(newConfig.NewNTPServer) && currentNtpServer != newConfig.NewNTPServer;
        }
    }
}
