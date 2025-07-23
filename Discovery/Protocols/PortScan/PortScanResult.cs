using System.Net;

namespace wpfhikip.Discovery.Protocols.PortScan
{
    /// <summary>
    /// Result of a port scan operation
    /// </summary>
    public class PortScanResult
    {
        /// <summary>
        /// IP address that was scanned
        /// </summary>
        public IPAddress? IPAddress { get; set; }

        /// <summary>
        /// Port number that was scanned
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// Protocol (TCP/UDP)
        /// </summary>
        public string Protocol { get; set; } = "TCP";

        /// <summary>
        /// Whether the port is open
        /// </summary>
        public bool IsOpen { get; set; }

        /// <summary>
        /// Service name associated with the port
        /// </summary>
        public string? Service { get; set; }

        /// <summary>
        /// Service banner if available
        /// </summary>
        public string? Banner { get; set; }

        /// <summary>
        /// Response time in milliseconds
        /// </summary>
        public double ResponseTime { get; set; }

        public override string ToString()
        {
            var status = IsOpen ? "Open" : "Closed";
            var service = !string.IsNullOrEmpty(Service) ? $" ({Service})" : "";
            return $"{IPAddress}:{Port} - {status}{service}";
        }
    }
}