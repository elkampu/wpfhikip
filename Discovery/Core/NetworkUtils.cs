using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace wpfhikip.Discovery.Core
{
    /// <summary>
    /// Network utility functions for discovery operations
    /// </summary>
    public static class NetworkUtils
    {
        /// <summary>
        /// Gets all local network interfaces with their IP addresses and subnets
        /// </summary>
        /// <returns>Dictionary mapping interface names to network information</returns>
        public static Dictionary<string, NetworkInterfaceInfo> GetLocalNetworkInterfaces()
        {
            var interfaces = new Dictionary<string, NetworkInterfaceInfo>();

            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var ipProperties = networkInterface.GetIPProperties();
                var interfaceInfo = new NetworkInterfaceInfo
                {
                    Name = networkInterface.Name,
                    Description = networkInterface.Description,
                    Type = networkInterface.NetworkInterfaceType.ToString(),
                    IsUp = networkInterface.OperationalStatus == OperationalStatus.Up,
                    Speed = networkInterface.Speed,
                    MacAddress = networkInterface.GetPhysicalAddress().ToString()
                };

                foreach (var unicastAddress in ipProperties.UnicastAddresses)
                {
                    if (unicastAddress.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        interfaceInfo.IPv4Addresses.Add(new NetworkAddressInfo
                        {
                            IPAddress = unicastAddress.Address,
                            SubnetMask = unicastAddress.IPv4Mask,
                            NetworkAddress = GetNetworkAddress(unicastAddress.Address, unicastAddress.IPv4Mask),
                            BroadcastAddress = GetBroadcastAddress(unicastAddress.Address, unicastAddress.IPv4Mask),
                            PrefixLength = GetPrefixLength(unicastAddress.IPv4Mask)
                        });
                    }
                }

                if (interfaceInfo.IPv4Addresses.Any())
                {
                    interfaces[networkInterface.Id] = interfaceInfo;
                }
            }

            return interfaces;
        }

        /// <summary>
        /// Gets all local network segments (subnets)
        /// </summary>
        /// <returns>List of network segments in CIDR notation</returns>
        public static List<string> GetLocalNetworkSegments()
        {
            var segments = new HashSet<string>();
            var interfaces = GetLocalNetworkInterfaces();

            foreach (var interfaceInfo in interfaces.Values)
            {
                foreach (var address in interfaceInfo.IPv4Addresses)
                {
                    var cidr = $"{address.NetworkAddress}/{address.PrefixLength}";
                    segments.Add(cidr);
                }
            }

            return segments.ToList();
        }

        /// <summary>
        /// Generates all IP addresses in a given network segment
        /// </summary>
        /// <param name="networkSegment">Network segment in CIDR notation (e.g., "192.168.1.0/24")</param>
        /// <returns>List of IP addresses in the segment</returns>
        public static List<IPAddress> GetIPAddressesInSegment(string networkSegment)
        {
            var addresses = new List<IPAddress>();

            if (!TryParseCidr(networkSegment, out var networkAddress, out var prefixLength))
                return addresses;

            var networkBytes = networkAddress.GetAddressBytes();
            var totalHosts = (int)Math.Pow(2, 32 - prefixLength) - 2; // Exclude network and broadcast

            if (totalHosts <= 0 || totalHosts > 65534) // Reasonable limit
                return addresses;

            for (int i = 1; i <= totalHosts; i++)
            {
                var hostBytes = BitConverter.GetBytes(i);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(hostBytes);

                var addressBytes = new byte[4];
                for (int j = 0; j < 4; j++)
                {
                    addressBytes[j] = (byte)(networkBytes[j] | hostBytes[j]);
                }

                addresses.Add(new IPAddress(addressBytes));
            }

            return addresses;
        }

        /// <summary>
        /// Checks if an IP address is within a network segment
        /// </summary>
        /// <param name="ipAddress">IP address to check</param>
        /// <param name="networkSegment">Network segment in CIDR notation</param>
        /// <returns>True if the IP is within the segment</returns>
        public static bool IsIPInSegment(IPAddress ipAddress, string networkSegment)
        {
            if (!TryParseCidr(networkSegment, out var networkAddress, out var prefixLength))
                return false;

            var ipBytes = ipAddress.GetAddressBytes();
            var networkBytes = networkAddress.GetAddressBytes();
            var bitsToCheck = prefixLength;

            for (int i = 0; i < 4 && bitsToCheck > 0; i++)
            {
                var bitsInThisByte = Math.Min(8, bitsToCheck);
                var mask = (byte)(0xFF << (8 - bitsInThisByte));

                if ((ipBytes[i] & mask) != (networkBytes[i] & mask))
                    return false;

                bitsToCheck -= bitsInThisByte;
            }

            return true;
        }

        /// <summary>
        /// Gets the network address for an IP address and subnet mask
        /// </summary>
        public static IPAddress GetNetworkAddress(IPAddress ipAddress, IPAddress subnetMask)
        {
            var ipBytes = ipAddress.GetAddressBytes();
            var maskBytes = subnetMask.GetAddressBytes();
            var networkBytes = new byte[4];

            for (int i = 0; i < 4; i++)
            {
                networkBytes[i] = (byte)(ipBytes[i] & maskBytes[i]);
            }

            return new IPAddress(networkBytes);
        }

        /// <summary>
        /// Gets the broadcast address for an IP address and subnet mask
        /// </summary>
        public static IPAddress GetBroadcastAddress(IPAddress ipAddress, IPAddress subnetMask)
        {
            var ipBytes = ipAddress.GetAddressBytes();
            var maskBytes = subnetMask.GetAddressBytes();
            var broadcastBytes = new byte[4];

            for (int i = 0; i < 4; i++)
            {
                broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);
            }

            return new IPAddress(broadcastBytes);
        }

        /// <summary>
        /// Converts a subnet mask to CIDR prefix length
        /// </summary>
        public static int GetPrefixLength(IPAddress subnetMask)
        {
            var maskBytes = subnetMask.GetAddressBytes();
            var binaryString = string.Join("", maskBytes.Select(b => Convert.ToString(b, 2).PadLeft(8, '0')));
            return binaryString.TakeWhile(c => c == '1').Count();
        }

        /// <summary>
        /// Converts CIDR prefix length to subnet mask
        /// </summary>
        public static IPAddress GetSubnetMask(int prefixLength)
        {
            if (prefixLength < 0 || prefixLength > 32)
                throw new ArgumentException("Prefix length must be between 0 and 32", nameof(prefixLength));

            uint mask = prefixLength == 0 ? 0 : 0xFFFFFFFF << (32 - prefixLength);
            return new IPAddress(BitConverter.GetBytes(IPAddress.HostToNetworkOrder((int)mask)));
        }

        /// <summary>
        /// Parses a CIDR notation string (e.g., "192.168.1.0/24")
        /// </summary>
        public static bool TryParseCidr(string cidr, out IPAddress networkAddress, out int prefixLength)
        {
            networkAddress = IPAddress.Any;
            prefixLength = 0;

            if (string.IsNullOrEmpty(cidr))
                return false;

            var parts = cidr.Split('/');
            if (parts.Length != 2)
                return false;

            if (!IPAddress.TryParse(parts[0], out networkAddress))
                return false;

            if (!int.TryParse(parts[1], out prefixLength) || prefixLength < 0 || prefixLength > 32)
                return false;

            return true;
        }

        /// <summary>
        /// Checks if a host is reachable via ping
        /// </summary>
        public static async Task<bool> PingHostAsync(IPAddress ipAddress, TimeSpan timeout)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ipAddress, (int)timeout.TotalMilliseconds);
                return reply.Status == IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if a TCP port is open on a host
        /// </summary>
        public static async Task<bool> IsPortOpenAsync(IPAddress ipAddress, int port, TimeSpan timeout)
        {
            try
            {
                using var tcpClient = new TcpClient();
                var connectTask = tcpClient.ConnectAsync(ipAddress, port);
                var timeoutTask = Task.Delay(timeout);

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                if (completedTask == timeoutTask)
                    return false;

                await connectTask; // Ensure we handle any exceptions
                return tcpClient.Connected;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the hostname for an IP address
        /// </summary>
        public static async Task<string?> GetHostnameAsync(IPAddress ipAddress)
        {
            try
            {
                var hostEntry = await Dns.GetHostEntryAsync(ipAddress);
                return hostEntry.HostName;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Information about a network interface
    /// </summary>
    public class NetworkInterfaceInfo
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool IsUp { get; set; }
        public long Speed { get; set; }
        public string MacAddress { get; set; } = string.Empty;
        public List<NetworkAddressInfo> IPv4Addresses { get; set; } = new();
    }

    /// <summary>
    /// Information about a network address
    /// </summary>
    public class NetworkAddressInfo
    {
        public IPAddress IPAddress { get; set; } = IPAddress.Any;
        public IPAddress SubnetMask { get; set; } = IPAddress.Any;
        public IPAddress NetworkAddress { get; set; } = IPAddress.Any;
        public IPAddress BroadcastAddress { get; set; } = IPAddress.Any;
        public int PrefixLength { get; set; }
    }
}