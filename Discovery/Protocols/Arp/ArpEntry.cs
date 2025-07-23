using System.Net;

namespace wpfhikip.Discovery.Protocols.Arp
{
    /// <summary>
    /// Represents an entry in the ARP table
    /// </summary>
    public class ArpEntry
    {
        /// <summary>
        /// IP address of the device
        /// </summary>
        public IPAddress? IPAddress { get; set; }

        /// <summary>
        /// MAC address of the device
        /// </summary>
        public string MACAddress { get; set; } = string.Empty;

        /// <summary>
        /// Hostname if resolved
        /// </summary>
        public string? Hostname { get; set; }

        /// <summary>
        /// ARP entry type (dynamic, static, etc.)
        /// </summary>
        public string Type { get; set; } = "dynamic";

        /// <summary>
        /// Network interface where this entry was found
        /// </summary>
        public string? Interface { get; set; }

        /// <summary>
        /// Whether this is a static ARP entry
        /// </summary>
        public bool IsStatic { get; set; }

        public override string ToString()
        {
            return $"{IPAddress} -> {MACAddress} ({Type})";
        }
    }
}