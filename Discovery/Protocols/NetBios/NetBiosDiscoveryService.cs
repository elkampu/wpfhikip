using System.Net;
using System.Net.Sockets;
using System.Text;

using wpfhikip.Discovery.Core;
using wpfhikip.Discovery.Models;

namespace wpfhikip.Discovery.Protocols.NetBios
{
    /// <summary>
    /// NetBIOS name resolution discovery service
    /// </summary>
    public class NetBiosDiscoveryService : INetworkDiscoveryService, IDisposable
    {
        public string ServiceName => "NetBIOS";
        public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(8);

        private UdpClient? _udpClient;
        private bool _disposed = false;
        private readonly SemaphoreSlim _semaphore;

        public event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;
        public event EventHandler<DiscoveryProgressEventArgs>? ProgressChanged;

        public NetBiosDiscoveryService()
        {
            _semaphore = new SemaphoreSlim(15, 15); // Limit concurrent requests
        }

        /// <summary>
        /// Discovers devices on all available network segments
        /// </summary>
        public async Task<IEnumerable<DiscoveredDevice>> DiscoverDevicesAsync(CancellationToken cancellationToken = default)
        {
            var devices = new List<DiscoveredDevice>();

            try
            {
                var interfaces = NetworkUtils.GetLocalNetworkInterfaces();
                var segments = new List<string>();

                foreach (var kvp in interfaces)
                {
                    var interfaceInfo = kvp.Value;
                    foreach (var address in interfaceInfo.IPv4Addresses)
                    {
                        segments.Add($"{address.NetworkAddress}/{address.PrefixLength}");
                    }
                }

                if (!segments.Any())
                {
                    segments.AddRange(new[] { "192.168.1.0/24", "192.168.0.0/24", "10.0.0.0/24" });
                }

                foreach (var segment in segments)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var segmentDevices = await DiscoverDevicesAsync(segment, cancellationToken);
                    devices.AddRange(segmentDevices);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelled
            }
            catch (Exception ex)
            {
                ReportProgress(0, 0, "", $"NetBIOS error: {ex.Message}");
            }

            return devices;
        }

        /// <summary>
        /// Discovers devices on a specific network segment
        /// </summary>
        public async Task<IEnumerable<DiscoveredDevice>> DiscoverDevicesAsync(string networkSegment, CancellationToken cancellationToken = default)
        {
            var devices = new List<DiscoveredDevice>();

            if (string.IsNullOrEmpty(networkSegment))
                return devices;

            try
            {
                InitializeUdpClient();

                var hostAddresses = NetworkUtils.GetIPAddressesInSegment(networkSegment);
                var totalHosts = hostAddresses.Count();
                var currentHost = 0;

                ReportProgress(0, totalHosts, networkSegment, $"Starting NetBIOS scan on {networkSegment}");

                // Process hosts in parallel with limited concurrency
                var tasks = hostAddresses.Select(async ipAddress =>
                {
                    await _semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        var device = await TryNetBiosDiscoveryAsync(ipAddress, cancellationToken);
                        if (device != null)
                        {
                            lock (devices)
                            {
                                devices.Add(device);
                            }
                            DeviceDiscovered?.Invoke(this, new DeviceDiscoveredEventArgs(device, ServiceName));
                        }

                        var current = Interlocked.Increment(ref currentHost);
                        if (current % 20 == 0) // Report progress every 20 hosts
                        {
                            ReportProgress(current, totalHosts, networkSegment, $"Scanned {current}/{totalHosts} hosts");
                        }
                    }
                    finally
                    {
                        _semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);

                ReportProgress(totalHosts, totalHosts, networkSegment, $"NetBIOS scan completed on {networkSegment}");
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelled
            }
            catch (Exception ex)
            {
                ReportProgress(0, 0, networkSegment, $"NetBIOS error on {networkSegment}: {ex.Message}");
            }

            return devices;
        }

        /// <summary>
        /// Attempts NetBIOS discovery on a single IP address
        /// </summary>
        private async Task<DiscoveredDevice?> TryNetBiosDiscoveryAsync(IPAddress ipAddress, CancellationToken cancellationToken)
        {
            try
            {
                if (_udpClient == null)
                    return null;

                // Try NetBIOS name query
                var nameTable = await QueryNetBiosNameTableAsync(ipAddress, cancellationToken);
                if (nameTable == null || !nameTable.Any())
                    return null;

                var device = new DiscoveredDevice(ipAddress, NetBiosConstants.NameServicePort);
                device.DiscoveryMethods.Add(DiscoveryMethod.NetBIOS);

                // Process name table entries
                var hostnames = new List<string>();
                var services = new List<string>();
                var workgroups = new List<string>();

                foreach (var entry in nameTable)
                {
                    device.DiscoveryData[$"NetBIOS_Name_{entry.Name}"] = $"{entry.Name} ({NetBiosConstants.GetServiceTypeName(entry.Type)})";

                    switch (entry.Type)
                    {
                        case NetBiosConstants.WorkstationService:
                            hostnames.Add(entry.Name);
                            break;
                        case NetBiosConstants.ServerService:
                            services.Add("File Server");
                            break;
                        case NetBiosConstants.DomainMasterBrowser:
                        case NetBiosConstants.MasterBrowser:
                            services.Add("Browser Service");
                            break;
                        case NetBiosConstants.BrowserService:
                            workgroups.Add(entry.Name);
                            break;
                    }
                }

                // Set device properties
                if (hostnames.Any())
                {
                    device.Name = hostnames.First();
                }

                if (services.Any())
                {
                    device.Description = $"NetBIOS Device - {string.Join(", ", services)}";
                    device.DeviceType = DetermineDeviceType(services);
                }
                else
                {
                    device.Description = "NetBIOS Device";
                    device.DeviceType = DeviceType.Computer;
                }

                if (workgroups.Any())
                {
                    device.DiscoveryData["NetBIOS_Workgroup"] = workgroups.First();
                }

                return device;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Queries NetBIOS name table from a host
        /// </summary>
        private async Task<List<NetBiosNameEntry>?> QueryNetBiosNameTableAsync(IPAddress ipAddress, CancellationToken cancellationToken)
        {
            try
            {
                if (_udpClient == null)
                    return null;

                var packet = CreateNetBiosNameQueryPacket();
                var endpoint = new IPEndPoint(ipAddress, NetBiosConstants.NameServicePort);

                await _udpClient.SendAsync(packet, packet.Length, endpoint);

                // Wait for response with timeout
                var timeoutTask = Task.Delay(3000, cancellationToken);
                var receiveTask = _udpClient.ReceiveAsync();

                var completedTask = await Task.WhenAny(receiveTask, timeoutTask);
                if (completedTask == receiveTask)
                {
                    var result = await receiveTask;
                    if (result.RemoteEndPoint.Address.Equals(ipAddress))
                    {
                        return ParseNetBiosNameResponse(result.Buffer);
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Creates NetBIOS name query packet
        /// </summary>
        private byte[] CreateNetBiosNameQueryPacket()
        {
            var packet = new List<byte>();

            // Transaction ID (2 bytes)
            packet.AddRange(BitConverter.GetBytes((ushort)0x1234).Reverse());

            // Flags (2 bytes) - Standard query
            packet.AddRange(BitConverter.GetBytes((ushort)0x0110).Reverse());

            // Questions (2 bytes)
            packet.AddRange(BitConverter.GetBytes((ushort)1).Reverse());

            // Answer RRs (2 bytes)
            packet.AddRange(BitConverter.GetBytes((ushort)0).Reverse());

            // Authority RRs (2 bytes)
            packet.AddRange(BitConverter.GetBytes((ushort)0).Reverse());

            // Additional RRs (2 bytes)
            packet.AddRange(BitConverter.GetBytes((ushort)0).Reverse());

            // Query name (wildcard)
            var encodedName = EncodeNetBiosName("*");
            packet.AddRange(encodedName);

            // Query type (2 bytes) - NBSTAT
            packet.AddRange(BitConverter.GetBytes(NetBiosConstants.NBStatQuery).Reverse());

            // Query class (2 bytes) - IN
            packet.AddRange(BitConverter.GetBytes((ushort)1).Reverse());

            return packet.ToArray();
        }

        /// <summary>
        /// Encodes NetBIOS name according to RFC 1001/1002
        /// </summary>
        private byte[] EncodeNetBiosName(string name)
        {
            var encoded = new List<byte>();

            // Pad name to 16 characters
            var paddedName = name.PadRight(15, ' ') + '\x00';
            var nameBytes = Encoding.ASCII.GetBytes(paddedName);

            // Length of encoded name
            encoded.Add(0x20);

            // Encode each byte as two characters
            foreach (var b in nameBytes)
            {
                encoded.Add((byte)('A' + (b >> 4)));
                encoded.Add((byte)('A' + (b & 0x0F)));
            }

            // Null terminator
            encoded.Add(0x00);

            return encoded.ToArray();
        }

        /// <summary>
        /// Parses NetBIOS name response
        /// </summary>
        private List<NetBiosNameEntry>? ParseNetBiosNameResponse(byte[] responseBytes)
        {
            try
            {
                if (responseBytes.Length < 12)
                    return null;

                var entries = new List<NetBiosNameEntry>();

                // Skip header (12 bytes)
                var offset = 12;

                // Skip query section
                while (offset < responseBytes.Length && responseBytes[offset] != 0)
                {
                    offset++;
                }
                offset += 5; // Skip null terminator and query type/class

                // Parse answer section
                if (offset + 10 < responseBytes.Length)
                {
                    // Skip name pointer (2 bytes)
                    offset += 2;

                    // Skip type and class (4 bytes)
                    offset += 4;

                    // Skip TTL (4 bytes)
                    offset += 4;

                    // Data length (2 bytes)
                    var dataLength = (responseBytes[offset] << 8) | responseBytes[offset + 1];
                    offset += 2;

                    // Number of names (1 byte)
                    if (offset < responseBytes.Length)
                    {
                        var nameCount = responseBytes[offset];
                        offset++;

                        // Parse each name entry (18 bytes each)
                        for (int i = 0; i < nameCount && offset + 18 <= responseBytes.Length; i++)
                        {
                            var nameBytes = new byte[15];
                            Array.Copy(responseBytes, offset, nameBytes, 0, 15);
                            var name = Encoding.ASCII.GetString(nameBytes).TrimEnd();

                            var type = responseBytes[offset + 15];
                            var flags = (responseBytes[offset + 16] << 8) | responseBytes[offset + 17];

                            entries.Add(new NetBiosNameEntry
                            {
                                Name = name,
                                Type = type,
                                Flags = flags
                            });

                            offset += 18;
                        }
                    }
                }

                return entries;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Determines device type based on NetBIOS services
        /// </summary>
        private DeviceType DetermineDeviceType(List<string> services)
        {
            if (services.Contains("File Server"))
                return DeviceType.FileServer;

            if (services.Contains("Browser Service"))
                return DeviceType.Computer;

            return DeviceType.Computer;
        }

        /// <summary>
        /// Initializes UDP client for NetBIOS communication
        /// </summary>
        private void InitializeUdpClient()
        {
            if (_udpClient != null)
                return;

            try
            {
                _udpClient = new UdpClient();
                _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
                _udpClient.Client.ReceiveTimeout = 3000;
                _udpClient.Client.SendTimeout = 3000;
            }
            catch (Exception ex)
            {
                _udpClient?.Dispose();
                _udpClient = null;
                throw new InvalidOperationException($"Failed to initialize NetBIOS client: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Reports discovery progress
        /// </summary>
        private void ReportProgress(int current, int total, string target, string status)
        {
            ProgressChanged?.Invoke(this, new DiscoveryProgressEventArgs(ServiceName, current, total, target, status));
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _udpClient?.Close();
                    _udpClient?.Dispose();
                    _semaphore?.Dispose();
                }
                _disposed = true;
            }
        }

        /// <summary>
        /// NetBIOS name table entry
        /// </summary>
        private class NetBiosNameEntry
        {
            public string Name { get; set; } = string.Empty;
            public byte Type { get; set; }
            public int Flags { get; set; }
        }
    }
}