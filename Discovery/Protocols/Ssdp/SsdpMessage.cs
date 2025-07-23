using System;
using System.Collections.Generic;
using System.Text;

namespace wpfhikip.Discovery.Protocols.Ssdp
{
    /// <summary>
    /// Represents an SSDP message (request or response)
    /// </summary>
    public class SsdpMessage
    {
        public string? Method { get; set; }
        public string? StatusCode { get; set; }
        public Dictionary<string, string> Headers { get; set; } = new();

        // Common SSDP headers
        public string? Host => Headers.GetValueOrDefault("HOST");
        public string? CacheControl => Headers.GetValueOrDefault("CACHE-CONTROL");
        public string? Location => Headers.GetValueOrDefault("LOCATION");
        public string? Server => Headers.GetValueOrDefault("SERVER");
        public string? ST => Headers.GetValueOrDefault("ST");
        public string? USN => Headers.GetValueOrDefault("USN");
        public string? MAN => Headers.GetValueOrDefault("MAN");
        public string? MX => Headers.GetValueOrDefault("MX");

        /// <summary>
        /// Creates an M-SEARCH request for SSDP discovery
        /// </summary>
        public static string CreateMSearchRequest(string searchTarget, int maxWait = 3)
        {
            var message = new StringBuilder();
            message.AppendLine("M-SEARCH * HTTP/1.1");
            message.AppendLine($"HOST: {SsdpConstants.MulticastAddress}:{SsdpConstants.MulticastPort}");
            message.AppendLine("MAN: \"ssdp:discover\"");
            message.AppendLine($"MX: {maxWait}");
            message.AppendLine($"ST: {searchTarget}");
            message.AppendLine();

            return message.ToString();
        }

        /// <summary>
        /// Parses an SSDP message from text
        /// </summary>
        public static SsdpMessage? Parse(string messageText)
        {
            if (string.IsNullOrWhiteSpace(messageText))
                return null;

            var lines = messageText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            if (lines.Length == 0)
                return null;

            var message = new SsdpMessage();

            // Parse first line (method/status)
            var firstLine = lines[0];
            if (firstLine.StartsWith("HTTP/"))
            {
                // Response
                var parts = firstLine.Split(' ');
                if (parts.Length >= 2)
                    message.StatusCode = parts[1];
            }
            else
            {
                // Request
                var parts = firstLine.Split(' ');
                if (parts.Length >= 1)
                    message.Method = parts[0];
            }

            // Parse headers
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line))
                    break;

                var colonIndex = line.IndexOf(':');
                if (colonIndex > 0)
                {
                    var headerName = line.Substring(0, colonIndex).Trim().ToUpper();
                    var headerValue = line.Substring(colonIndex + 1).Trim();
                    message.Headers[headerName] = headerValue;
                }
            }

            return message;
        }

        /// <summary>
        /// Checks if this is a valid SSDP response
        /// </summary>
        public bool IsValidResponse()
        {
            return StatusCode == "200" &&
                   !string.IsNullOrEmpty(ST) &&
                   !string.IsNullOrEmpty(USN);
        }

        /// <summary>
        /// Checks if this is a device advertisement
        /// </summary>
        public bool IsNotifyMessage()
        {
            return Method == "NOTIFY" &&
                   Headers.GetValueOrDefault("NTS") == "ssdp:alive";
        }

        public override string ToString()
        {
            var sb = new StringBuilder();

            if (!string.IsNullOrEmpty(Method))
            {
                sb.AppendLine($"{Method} * HTTP/1.1");
            }
            else if (!string.IsNullOrEmpty(StatusCode))
            {
                sb.AppendLine($"HTTP/1.1 {StatusCode} OK");
            }

            foreach (var header in Headers)
            {
                sb.AppendLine($"{header.Key}: {header.Value}");
            }

            return sb.ToString();
        }
    }
}