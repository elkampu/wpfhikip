using System.Text;
using System.Xml.Linq;

namespace wpfhikip.Discovery.Protocols.WsDiscovery
{
    /// <summary>
    /// Handles WS-Discovery message creation and parsing
    /// </summary>
    public class WsDiscoveryMessage
    {
        public string? MessageId { get; set; }
        public string? EndpointReference { get; set; }
        public string? Types { get; set; }
        public List<string> Scopes { get; set; } = new();
        public List<string> XAddrs { get; set; } = new();
        public string? MetadataVersion { get; set; }

        /// <summary>
        /// Creates a WS-Discovery probe request
        /// </summary>
        public static string CreateProbeRequest(string? types = null)
        {
            var messageId = $"urn:uuid:{Guid.NewGuid()}";
            var typesElement = !string.IsNullOrEmpty(types)
                ? $"<d:Types>{types}</d:Types>"
                : "";

            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<soap:Envelope xmlns:soap=""http://www.w3.org/2003/05/soap-envelope""
               xmlns:wsa=""http://www.w3.org/2005/08/addressing""
               xmlns:d=""http://schemas.xmlsoap.org/ws/2005/04/discovery""
               xmlns:dn=""http://www.onvif.org/ver10/network/wsdl"">
    <soap:Header>
        <wsa:Action>http://schemas.xmlsoap.org/ws/2005/04/discovery/Probe</wsa:Action>
        <wsa:MessageID>{messageId}</wsa:MessageID>
        <wsa:ReplyTo>
            <wsa:Address>http://www.w3.org/2005/08/addressing/anonymous</wsa:Address>
        </wsa:ReplyTo>
        <wsa:To>urn:schemas-xmlsoap-org:ws:2005:04:discovery</wsa:To>
    </soap:Header>
    <soap:Body>
        <d:Probe>
            {typesElement}
        </d:Probe>
    </soap:Body>
</soap:Envelope>";
        }

        /// <summary>
        /// Parses a WS-Discovery probe match response
        /// </summary>
        public static WsDiscoveryMessage? ParseProbeMatch(string xml)
        {
            try
            {
                var doc = XDocument.Parse(xml);

                // Check if this is a ProbeMatches response
                var probeMatches = doc.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "ProbeMatches");

                if (probeMatches == null)
                    return null;

                var probeMatch = probeMatches.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "ProbeMatch");

                if (probeMatch == null)
                    return null;

                var message = new WsDiscoveryMessage();

                // Extract endpoint reference
                var endpointRef = probeMatch.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "EndpointReference");
                if (endpointRef != null)
                {
                    var address = endpointRef.Descendants()
                        .FirstOrDefault(e => e.Name.LocalName == "Address");
                    message.EndpointReference = address?.Value;
                }

                // Extract types
                var types = probeMatch.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "Types");
                message.Types = types?.Value;

                // Extract scopes
                var scopes = probeMatch.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "Scopes");
                if (scopes != null && !string.IsNullOrEmpty(scopes.Value))
                {
                    message.Scopes = scopes.Value
                        .Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                        .ToList();
                }

                // Extract XAddrs
                var xaddrs = probeMatch.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "XAddrs");
                if (xaddrs != null && !string.IsNullOrEmpty(xaddrs.Value))
                {
                    message.XAddrs = xaddrs.Value
                        .Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                        .ToList();
                }

                // Extract metadata version
                var metadataVersion = probeMatch.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "MetadataVersion");
                message.MetadataVersion = metadataVersion?.Value;

                return message;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Checks if this is a valid probe match
        /// </summary>
        public bool IsValidProbeMatch()
        {
            return !string.IsNullOrEmpty(EndpointReference) &&
                   !string.IsNullOrEmpty(Types) &&
                   XAddrs.Any();
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"EndpointReference: {EndpointReference}");
            sb.AppendLine($"Types: {Types}");
            sb.AppendLine($"Scopes: {string.Join(", ", Scopes)}");
            sb.AppendLine($"XAddrs: {string.Join(", ", XAddrs)}");
            sb.AppendLine($"MetadataVersion: {MetadataVersion}");
            return sb.ToString();
        }
    }
}