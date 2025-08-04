using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

using wpfhikip.Discovery.Models;

namespace wpfhikip.Discovery.Core
{
    /// <summary>
    /// Network utility functions for discovery operations
    /// </summary>
    public static class NetworkUtils
    {
        // Cache frequently used calculations to avoid repeated work
        private static readonly Dictionary<IPAddress, int> s_prefixLengthCache = new();
        private static readonly object s_cacheLock = new();

        // Cache for local IP addresses to avoid repeated enumeration
        private static HashSet<string>? s_cachedLocalIPs;
        private static DateTime s_lastLocalIPUpdate = DateTime.MinValue;
        private static readonly TimeSpan s_localIPCacheDuration = TimeSpan.FromMinutes(5);

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

                if (interfaceInfo.IPv4Addresses.Count > 0)
                {
                    interfaces[networkInterface.Id] = interfaceInfo;
                }
            }

            return interfaces;
        }

        /// <summary>
        /// Gets all local IP addresses from network interfaces (cached for performance)
        /// </summary>
        public static HashSet<string> GetLocalIPAddresses()
        {
            lock (s_cacheLock)
            {
                // Check if cache is still valid
                if (s_cachedLocalIPs != null && DateTime.UtcNow - s_lastLocalIPUpdate < s_localIPCacheDuration)
                {
                    return new HashSet<string>(s_cachedLocalIPs);
                }

                // Refresh cache
                var localIPs = new HashSet<string>();

                try
                {
                    // Add common local addresses
                    localIPs.Add("127.0.0.1");
                    localIPs.Add("::1");
                    localIPs.Add("0.0.0.0");

                    // Get all local network interface addresses
                    var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces()
                        .Where(ni => ni.OperationalStatus == OperationalStatus.Up);

                    foreach (var networkInterface in networkInterfaces)
                    {
                        var properties = networkInterface.GetIPProperties();
                        foreach (var addr in properties.UnicastAddresses)
                        {
                            if (addr.Address.AddressFamily == AddressFamily.InterNetwork ||
                                addr.Address.AddressFamily == AddressFamily.InterNetworkV6)
                            {
                                localIPs.Add(addr.Address.ToString());
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error getting local IPs: {ex.Message}");
                }

                // Update cache
                s_cachedLocalIPs = localIPs;
                s_lastLocalIPUpdate = DateTime.UtcNow;

                return new HashSet<string>(localIPs);
            }
        }

        /// <summary>
        /// Checks if an IP address is a local IP address
        /// </summary>
        public static bool IsLocalIPAddress(IPAddress ipAddress)
        {
            var localIPs = GetLocalIPAddresses();
            return localIPs.Contains(ipAddress.ToString());
        }

        /// <summary>
        /// Checks if a target IP address is in the same subnet as any local interface
        /// </summary>
        public static bool IsLocalSubnet(IPAddress? targetAddress, IEnumerable<NetworkInterface> networkInterfaces)
        {
            if (targetAddress == null) return false;

            try
            {
                foreach (var networkInterface in networkInterfaces)
                {
                    var properties = networkInterface.GetIPProperties();
                    foreach (var unicast in properties.UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            var localNetwork = GetNetworkAddress(unicast.Address, unicast.IPv4Mask);
                            var targetNetwork = GetNetworkAddress(targetAddress, unicast.IPv4Mask);

                            if (localNetwork != null && targetNetwork != null && localNetwork.Equals(targetNetwork))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking subnet for {targetAddress}: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Enhanced device information with DNS lookup
        /// </summary>
        public static async Task EnhanceDeviceInformation(DiscoveredDevice device, DiscoveryMethod discoveryMethod)
        {
            try
            {
                // Try to get hostname if not already set
                if (string.IsNullOrEmpty(device.Name) && device.IPAddress != null)
                {
                    try
                    {
                        var hostEntry = await Dns.GetHostEntryAsync(device.IPAddress);
                        if (!string.IsNullOrEmpty(hostEntry.HostName))
                        {
                            device.Name = hostEntry.HostName;
                        }
                    }
                    catch
                    {
                        // Ignore DNS lookup failures
                    }
                }

                // Add discovery method
                device.DiscoveryMethods.Add(discoveryMethod);

                // Update last seen
                device.LastSeen = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error enhancing device {device.IPAddress}: {ex.Message}");
            }
        }

        /// <summary>
        /// Invalidates the local IP address cache to force refresh
        /// </summary>
        public static void InvalidateLocalIPCache()
        {
            lock (s_cacheLock)
            {
                s_cachedLocalIPs = null;
                s_lastLocalIPUpdate = DateTime.MinValue;
            }
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
            if (!TryParseCidr(networkSegment, out var networkAddress, out var prefixLength))
                return new List<IPAddress>();

            var totalHosts = Math.Min((int)Math.Pow(2, 32 - prefixLength) - 2, 65534); // Reasonable limit
            if (totalHosts <= 0)
                return new List<IPAddress>();

            var addresses = new List<IPAddress>(totalHosts);
            var networkBytes = networkAddress.GetAddressBytes();

            // Pre-allocate buffer outside loop to fix CA2014 warning
            var addressBytes = new byte[4];

            for (int i = 1; i <= totalHosts; i++)
            {
                // Copy network bytes to address bytes
                Array.Copy(networkBytes, addressBytes, 4);

                // Add host part using bit manipulation
                var hostValue = (uint)i;
                addressBytes[3] = (byte)(addressBytes[3] | (hostValue & 0xFF));
                addressBytes[2] = (byte)(addressBytes[2] | ((hostValue >> 8) & 0xFF));
                addressBytes[1] = (byte)(addressBytes[1] | ((hostValue >> 16) & 0xFF));
                addressBytes[0] = (byte)(addressBytes[0] | ((hostValue >> 24) & 0xFF));

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

            ReadOnlySpan<byte> ipBytes = ipAddress.GetAddressBytes();
            ReadOnlySpan<byte> networkBytes = networkAddress.GetAddressBytes();
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
            // Check cache first
            lock (s_cacheLock)
            {
                if (s_prefixLengthCache.TryGetValue(subnetMask, out int cachedResult))
                {
                    return cachedResult;
                }
            }

            ReadOnlySpan<byte> maskBytes = subnetMask.GetAddressBytes();
            var prefixLength = 0;

            for (int i = 0; i < 4; i++)
            {
                var b = maskBytes[i];
                while (b > 0)
                {
                    if ((b & 0x80) != 0)
                        prefixLength++;
                    else
                        break;
                    b <<= 1;
                }

                if (b > 0) // Found a gap, stop counting
                    break;
            }

            // Cache the result
            lock (s_cacheLock)
            {
                s_prefixLengthCache.TryAdd(subnetMask, prefixLength);
            }

            return prefixLength;
        }

        /// <summary>
        /// Converts CIDR prefix length to subnet mask
        /// </summary>
        public static IPAddress GetSubnetMask(int prefixLength)
        {
            if (prefixLength is < 0 or > 32)
                throw new ArgumentException("Prefix length must be between 0 and 32", nameof(prefixLength));

            uint mask = prefixLength == 0 ? 0 : 0xFFFFFFFF << (32 - prefixLength);
            var bytes = new byte[4];

            bytes[0] = (byte)((mask >> 24) & 0xFF);
            bytes[1] = (byte)((mask >> 16) & 0xFF);
            bytes[2] = (byte)((mask >> 8) & 0xFF);
            bytes[3] = (byte)(mask & 0xFF);

            return new IPAddress(bytes);
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

            var separatorIndex = cidr.IndexOf('/');
            if (separatorIndex <= 0 || separatorIndex == cidr.Length - 1)
                return false;

            if (!IPAddress.TryParse(cidr.AsSpan(0, separatorIndex), out networkAddress))
                return false;

            if (!int.TryParse(cidr.AsSpan(separatorIndex + 1), out prefixLength) ||
                prefixLength is < 0 or > 32)
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
                var reply = await ping.SendPingAsync(ipAddress, (int)timeout.TotalMilliseconds).ConfigureAwait(false);
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

                var completedTask = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);

                if (completedTask == timeoutTask)
                    return false;

                await connectTask.ConfigureAwait(false); // Ensure we handle any exceptions
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
                var hostEntry = await Dns.GetHostEntryAsync(ipAddress).ConfigureAwait(false);
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