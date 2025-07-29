using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

using wpfhikip.Discovery.Core;
using wpfhikip.Discovery.Models;

namespace wpfhikip.Discovery.Protocols.Snmp
{
    /// <summary>
    /// SNMP (Simple Network Management Protocol) discovery service
    /// </summary>
    public class SnmpDiscoveryService : INetworkDiscoveryService, IDisposable
    {
        public string ServiceName => "SNMP";
        public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(10);

        private bool _disposed = false;
        private readonly SemaphoreSlim _semaphore;

        public event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;
        public event EventHandler<DiscoveryProgressEventArgs>? ProgressChanged;

        public SnmpDiscoveryService()
        {
            // Limit concurrent SNMP requests to avoid overwhelming the network
            _semaphore = new SemaphoreSlim(20, 20);
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
                    // Fallback to common segments
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
                ReportProgress(0, 0, "", $"SNMP error: {ex.Message}");
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
                var hostAddresses = NetworkUtils.GetIPAddressesInSegment(networkSegment);
                var totalHosts = hostAddresses.Count();
                var currentHost = 0;

                ReportProgress(0, totalHosts, networkSegment, $"Starting SNMP scan on {networkSegment}");

                // Process hosts in parallel with limited concurrency
                var tasks = hostAddresses.Select(async ipAddress =>
                {
                    await _semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        var device = await TrySnmpDiscoveryAsync(ipAddress, cancellationToken);
                        if (device != null)
                        {
                            lock (devices)
                            {
                                devices.Add(device);
                            }
                            DeviceDiscovered?.Invoke(this, new DeviceDiscoveredEventArgs(device, ServiceName));
                        }

                        var current = Interlocked.Increment(ref currentHost);
                        if (current % 10 == 0) // Report progress every 10 hosts
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

                ReportProgress(totalHosts, totalHosts, networkSegment, $"SNMP scan completed on {networkSegment}");
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelled
            }
            catch (Exception ex)
            {
                ReportProgress(0, 0, networkSegment, $"SNMP error on {networkSegment}: {ex.Message}");
            }

            return devices;
        }

        /// <summary>
        /// Attempts SNMP discovery on a single IP address
        /// </summary>
        private async Task<DiscoveredDevice?> TrySnmpDiscoveryAsync(IPAddress ipAddress, CancellationToken cancellationToken)
        {
            // First check if host is reachable via ping
            if (!await IsHostReachableAsync(ipAddress, cancellationToken))
                return null;

            var communities = SnmpConstants.GetCommonCommunities();

            foreach (var community in communities)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    var device = await QuerySnmpDeviceAsync(ipAddress, community, cancellationToken);
                    if (device != null)
                        return device;
                }
                catch
                {
                    // Try next community
                }
            }

            return null;
        }

        /// <summary>
        /// Checks if host is reachable via ping
        /// </summary>
        private async Task<bool> IsHostReachableAsync(IPAddress ipAddress, CancellationToken cancellationToken)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ipAddress, 1000);
                return reply.Status == IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Queries SNMP device for system information
        /// </summary>
        private async Task<DiscoveredDevice?> QuerySnmpDeviceAsync(IPAddress ipAddress, string community, CancellationToken cancellationToken)
        {
            try
            {
                using var udpClient = new UdpClient();
                udpClient.Client.ReceiveTimeout = 5000;
                udpClient.Client.SendTimeout = 5000;

                var endpoint = new IPEndPoint(ipAddress, SnmpConstants.DefaultPort);

                // Try to get system description first
                var sysDescrOid = SnmpConstants.OIDs.SysDescr;
                var response = await SendSnmpGetRequest(udpClient, endpoint, community, sysDescrOid, cancellationToken);

                if (response == null || string.IsNullOrEmpty(response.Value))
                    return null;

                // Create device with basic information
                var device = new DiscoveredDevice(ipAddress, SnmpConstants.DefaultPort)
                {
                    Description = response.Value,
                    DeviceType = DetermineDeviceType(response.Value)
                };

                device.DiscoveryMethods.Add(DiscoveryMethod.SNMP);
                device.DiscoveryData["SNMP_Community"] = community;
                device.DiscoveryData["SNMP_SysDescr"] = response.Value;

                // Try to get additional system information
                await EnrichDeviceWithSnmpDataAsync(udpClient, endpoint, community, device, cancellationToken);

                return device;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Enriches device with additional SNMP data
        /// </summary>
        private async Task EnrichDeviceWithSnmpDataAsync(UdpClient udpClient, IPEndPoint endpoint, string community, DiscoveredDevice device, CancellationToken cancellationToken)
        {
            var oids = new Dictionary<string, string>
            {
                [SnmpConstants.OIDs.SysName] = "SysName",
                [SnmpConstants.OIDs.SysContact] = "SysContact",
                [SnmpConstants.OIDs.SysLocation] = "SysLocation",
                [SnmpConstants.OIDs.SysObjectId] = "SysObjectId"
            };

            foreach (var kvp in oids)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    var response = await SendSnmpGetRequest(udpClient, endpoint, community, kvp.Key, cancellationToken);
                    if (response != null && !string.IsNullOrEmpty(response.Value))
                    {
                        device.DiscoveryData[$"SNMP_{kvp.Value}"] = response.Value;

                        // Map specific values to device properties
                        switch (kvp.Value)
                        {
                            case "SysName":
                                device.Name = response.Value;
                                break;
                            case "SysObjectId":
                                device.DeviceType = DetermineDeviceTypeFromOID(response.Value) ?? device.DeviceType;
                                device.Manufacturer = ExtractManufacturerFromOID(response.Value) ?? device.Manufacturer;
                                break;
                        }
                    }
                }
                catch
                {
                    // Continue with next OID
                }
            }
        }

        /// <summary>
        /// Sends SNMP GET request (simplified implementation)
        /// </summary>
        private async Task<SnmpResponse?> SendSnmpGetRequest(UdpClient udpClient, IPEndPoint endpoint, string community, string oid, CancellationToken cancellationToken)
        {
            try
            {
                // This is a simplified SNMP implementation
                // In a production environment, you'd use a proper SNMP library like Lextm.SharpSnmpLib

                var packet = CreateSimpleSnmpGetPacket(community, oid);
                await udpClient.SendAsync(packet, packet.Length, endpoint);

                var receiveTask = udpClient.ReceiveAsync();
                var timeoutTask = Task.Delay(5000, cancellationToken);

                var completedTask = await Task.WhenAny(receiveTask, timeoutTask);
                if (completedTask == receiveTask)
                {
                    var result = await receiveTask;
                    return ParseSimpleSnmpResponse(result.Buffer);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Creates a simple SNMP GET packet (basic implementation)
        /// </summary>
        private byte[] CreateSimpleSnmpGetPacket(string community, string oid)
        {
            // This is a very basic SNMP packet creation
            // For production use, implement proper ASN.1 encoding or use a library
            var packet = new List<byte>();

            // SNMP version (v2c = 1)
            packet.AddRange(new byte[] { 0x30, 0x00 }); // Sequence placeholder
            packet.AddRange(new byte[] { 0x02, 0x01, 0x01 }); // Version 2c

            // Community string
            packet.AddRange(new byte[] { 0x04, (byte)community.Length });
            packet.AddRange(Encoding.ASCII.GetBytes(community));

            // PDU (simplified)
            packet.AddRange(new byte[] { 0xA0, 0x00 }); // GetRequest placeholder
            packet.AddRange(new byte[] { 0x02, 0x01, 0x01 }); // Request ID
            packet.AddRange(new byte[] { 0x02, 0x01, 0x00 }); // Error status
            packet.AddRange(new byte[] { 0x02, 0x01, 0x00 }); // Error index

            // Variable bindings (simplified)
            packet.AddRange(new byte[] { 0x30, 0x00 }); // Sequence placeholder

            // Update lengths (simplified)
            var totalLength = packet.Count - 2;
            packet[1] = (byte)totalLength;

            return packet.ToArray();
        }

        /// <summary>
        /// Parses simple SNMP response (basic implementation)
        /// </summary>
        private SnmpResponse? ParseSimpleSnmpResponse(byte[] responseBytes)
        {
            try
            {
                // This is a very simplified parser
                // For production use, implement proper ASN.1 decoding or use a library

                if (responseBytes.Length < 10)
                    return null;

                // Extract value from response (simplified)
                var valueStart = Array.IndexOf(responseBytes, (byte)0x04);
                if (valueStart > 0 && valueStart + 1 < responseBytes.Length)
                {
                    var valueLength = responseBytes[valueStart + 1];
                    if (valueStart + 2 + valueLength <= responseBytes.Length)
                    {
                        var value = Encoding.UTF8.GetString(responseBytes, valueStart + 2, valueLength);
                        return new SnmpResponse { Value = value };
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
        /// Determines device type from system description
        /// </summary>
        private DeviceType DetermineDeviceType(string sysDescr)
        {
            if (string.IsNullOrEmpty(sysDescr))
                return DeviceType.Unknown;

            var desc = sysDescr.ToLower();

            if (desc.Contains("camera") || desc.Contains("ipcam") || desc.Contains("video"))
                return DeviceType.Camera;

            if (desc.Contains("router") || desc.Contains("gateway"))
                return DeviceType.Router;

            if (desc.Contains("switch"))
                return DeviceType.Switch;

            if (desc.Contains("printer"))
                return DeviceType.Printer;

            if (desc.Contains("nas") || desc.Contains("storage"))
                return DeviceType.NAS;

            if (desc.Contains("access point") || desc.Contains("wireless"))
                return DeviceType.AccessPoint;

            return DeviceType.NetworkDevice;
        }

        /// <summary>
        /// Determines device type from system object ID
        /// </summary>
        private DeviceType? DetermineDeviceTypeFromOID(string sysObjectId)
        {
            if (string.IsNullOrEmpty(sysObjectId))
                return null;

            return sysObjectId switch
            {
                var oid when oid.StartsWith(SnmpConstants.OIDs.HikvisionRoot) => DeviceType.Camera,
                var oid when oid.StartsWith(SnmpConstants.OIDs.AxisRoot) => DeviceType.Camera,
                var oid when oid.StartsWith(SnmpConstants.OIDs.DahuaRoot) => DeviceType.Camera,
                var oid when oid.StartsWith(SnmpConstants.OIDs.HanwhaRoot) => DeviceType.Camera,
                var oid when oid.StartsWith(SnmpConstants.OIDs.PrinterRoot) => DeviceType.Printer,
                _ => DeviceType.NetworkDevice
            };
        }

        /// <summary>
        /// Extracts manufacturer from system object ID
        /// </summary>
        private string? ExtractManufacturerFromOID(string sysObjectId)
        {
            if (string.IsNullOrEmpty(sysObjectId))
                return null;

            return sysObjectId switch
            {
                var oid when oid.StartsWith(SnmpConstants.OIDs.HikvisionRoot) => "Hikvision",
                var oid when oid.StartsWith(SnmpConstants.OIDs.AxisRoot) => "Axis Communications",
                var oid when oid.StartsWith(SnmpConstants.OIDs.DahuaRoot) => "Dahua Technology",
                var oid when oid.StartsWith(SnmpConstants.OIDs.HanwhaRoot) => "Hanwha Techwin",
                var oid when oid.StartsWith(SnmpConstants.OIDs.CiscoRoot) => "Cisco Systems",
                var oid when oid.StartsWith(SnmpConstants.OIDs.HPRoot) => "Hewlett-Packard",
                var oid when oid.StartsWith(SnmpConstants.OIDs.JuniperRoot) => "Juniper Networks",
                _ => null
            };
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
                    _semaphore?.Dispose();
                }
                _disposed = true;
            }
        }

        /// <summary>
        /// Simple SNMP response structure
        /// </summary>
        private class SnmpResponse
        {
            public string Value { get; set; } = string.Empty;
        }
    }
}