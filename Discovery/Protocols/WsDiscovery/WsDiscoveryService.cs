using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml;

using wpfhikip.Discovery.Core;
using wpfhikip.Discovery.Models;

namespace wpfhikip.Discovery.Protocols.WsDiscovery
{
    /// <summary>
    /// WS-Discovery service for finding network devices
    /// Enhanced to listen on all available network interfaces
    /// </summary>
    public class WsDiscoveryService : INetworkDiscoveryService, IDisposable
    {
        public string ServiceName => "WS-Discovery";
        public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(15);

        private readonly List<UdpClient> _udpClients = new();
        private bool _disposed = false;

        public event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;
        public event EventHandler<DiscoveryProgressEventArgs>? ProgressChanged;

        /// <summary>
        /// Discovers devices on all available network segments
        /// </summary>
        public async Task<IEnumerable<DiscoveredDevice>> DiscoverDevicesAsync(CancellationToken cancellationToken = default)
        {
            var devices = new List<DiscoveredDevice>();

            try
            {
                InitializeMultiInterfaceClients();

                var deviceTypes = WsDiscoveryConstants.GetCommonDeviceTypes();
                var totalTypes = deviceTypes.Length;

                for (int i = 0; i < totalTypes; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var deviceType = deviceTypes[i];
                    ReportProgress(i, totalTypes, deviceType, $"Probing for {deviceType}");

                    var foundDevices = await PerformMultiInterfaceProbeAsync(deviceType, cancellationToken);

                    // Deduplicate devices based on UniqueId
                    foreach (var device in foundDevices)
                    {
                        if (!devices.Any(d => d.UniqueId == device.UniqueId))
                        {
                            devices.Add(device);
                        }
                    }

                    // Small delay between probes
                    await Task.Delay(200, cancellationToken);
                }

                ReportProgress(totalTypes, totalTypes, "", $"WS-Discovery completed. Found {devices.Count} unique devices.");
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelled
            }
            catch (Exception ex)
            {
                ReportProgress(0, 0, "", $"WS-Discovery error: {ex.Message}");
            }

            return devices;
        }

        /// <summary>
        /// Discovers devices on a specific network segment
        /// </summary>
        public async Task<IEnumerable<DiscoveredDevice>> DiscoverDevicesAsync(string networkSegment, CancellationToken cancellationToken = default)
        {
            // WS-Discovery is multicast-based, so it discovers devices across all segments
            // We'll filter results to the specified segment after discovery
            var allDevices = await DiscoverDevicesAsync(cancellationToken);

            if (string.IsNullOrEmpty(networkSegment))
                return allDevices;

            return allDevices.Where(device =>
                device.IPAddress != null &&
                NetworkUtils.IsIPInSegment(device.IPAddress, networkSegment));
        }

        /// <summary>
        /// Initializes UDP clients for all available network interfaces
        /// </summary>
        private void InitializeMultiInterfaceClients()
        {
            DisposeClients();

            try
            {
                var interfaces = NetworkUtils.GetLocalNetworkInterfaces();
                var multicastAddress = IPAddress.Parse(WsDiscoveryConstants.MulticastAddress);

                ReportProgress(0, 0, "", $"Initializing WS-Discovery clients on {interfaces.Count} network interfaces");

                // Create a client for each network interface
                foreach (var kvp in interfaces)
                {
                    var interfaceInfo = kvp.Value;

                    foreach (var addressInfo in interfaceInfo.IPv4Addresses)
                    {
                        try
                        {
                            var udpClient = new UdpClient();
                            udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                            // Bind to the specific interface address
                            udpClient.Client.Bind(new IPEndPoint(addressInfo.IPAddress, 0));

                            // Join multicast group on this interface
                            udpClient.JoinMulticastGroup(multicastAddress, addressInfo.IPAddress);

                            _udpClients.Add(udpClient);

                            ReportProgress(0, 0, "", $"WS-Discovery client initialized on {addressInfo.IPAddress} ({interfaceInfo.Name})");
                        }
                        catch (Exception ex)
                        {
                            ReportProgress(0, 0, "", $"Failed to initialize WS-Discovery client on {addressInfo.IPAddress}: {ex.Message}");
                        }
                    }
                }

                // Also create a general client bound to any address as fallback
                try
                {
                    var generalClient = new UdpClient();
                    generalClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    generalClient.Client.Bind(new IPEndPoint(IPAddress.Any, 0));
                    generalClient.JoinMulticastGroup(multicastAddress);
                    _udpClients.Add(generalClient);

                    ReportProgress(0, 0, "", "General WS-Discovery client initialized (fallback)");
                }
                catch (Exception ex)
                {
                    ReportProgress(0, 0, "", $"Failed to initialize general WS-Discovery client: {ex.Message}");
                }

                if (!_udpClients.Any())
                {
                    throw new InvalidOperationException("Failed to initialize any WS-Discovery clients");
                }
            }
            catch (Exception ex)
            {
                DisposeClients();
                throw new InvalidOperationException($"Failed to initialize WS-Discovery clients: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Performs WS-Discovery probe across all network interfaces
        /// </summary>
        private async Task<List<DiscoveredDevice>> PerformMultiInterfaceProbeAsync(string deviceType, CancellationToken cancellationToken)
        {
            var devices = new List<DiscoveredDevice>();
            var multicastEndpoint = new IPEndPoint(IPAddress.Parse(WsDiscoveryConstants.MulticastAddress), WsDiscoveryConstants.MulticastPort);

            if (!_udpClients.Any())
                return devices;

            try
            {
                // Send WS-Discovery probe from all clients simultaneously
                var probeMessage = CreateWsDiscoveryProbeMessage(deviceType);
                var probeBytes = Encoding.UTF8.GetBytes(probeMessage);

                var sendTasks = _udpClients.Select(async client =>
                {
                    try
                    {
                        await client.SendAsync(probeBytes, probeBytes.Length, multicastEndpoint);
                    }
                    catch (Exception ex)
                    {
                        ReportProgress(0, 0, "", $"Failed to send WS-Discovery probe from client: {ex.Message}");
                    }
                });

                await Task.WhenAll(sendTasks);

                // Listen for responses from all clients
                var listenTimeout = DateTime.UtcNow.Add(TimeSpan.FromSeconds(8));
                var receiveTasks = new List<Task>();

                foreach (var client in _udpClients)
                {
                    var receiveTask = ListenForWsDiscoveryResponsesAsync(client, devices, listenTimeout, cancellationToken);
                    receiveTasks.Add(receiveTask);
                }

                await Task.WhenAll(receiveTasks);
            }
            catch (Exception ex)
            {
                ReportProgress(0, 0, "", $"Error during multi-interface WS-Discovery probe: {ex.Message}");
            }

            return devices;
        }

        /// <summary>
        /// Listens for WS-Discovery responses on a specific client
        /// </summary>
        private async Task ListenForWsDiscoveryResponsesAsync(UdpClient client, List<DiscoveredDevice> devices, DateTime timeout, CancellationToken cancellationToken)
        {
            try
            {
                while (DateTime.UtcNow < timeout && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var receiveTask = client.ReceiveAsync();
                        var timeoutTask = Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);

                        var completedTask = await Task.WhenAny(receiveTask, timeoutTask);

                        if (completedTask == receiveTask)
                        {
                            var result = await receiveTask;
                            var device = ParseWsDiscoveryResponse(result.Buffer, result.RemoteEndPoint);

                            if (device != null)
                            {
                                lock (devices)
                                {
                                    // Check for duplicates
                                    if (!devices.Any(d => d.UniqueId == device.UniqueId))
                                    {
                                        devices.Add(device);
                                        DeviceDiscovered?.Invoke(this, new DeviceDiscoveredEventArgs(device, ServiceName));
                                    }
                                }
                            }
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        break;
                    }
                    catch (SocketException)
                    {
                        break;
                    }
                    catch (Exception)
                    {
                        // Continue listening despite individual errors
                    }
                }
            }
            catch (Exception)
            {
                // Ignore errors from individual clients
            }
        }

        // Additional methods for creating probe messages and parsing responses would go here...
        // Similar to the existing WS-Discovery implementation but enhanced for multi-interface

        /// <summary>
        /// Disposes all UDP clients
        /// </summary>
        private void DisposeClients()
        {
            foreach (var client in _udpClients)
            {
                try
                {
                    client?.Close();
                    client?.Dispose();
                }
                catch
                {
                    // Ignore disposal errors
                }
            }
            _udpClients.Clear();
        }

        /// <summary>
        /// Reports discovery progress
        /// </summary>
        private void ReportProgress(int current, int total, string target, string status)
        {
            ProgressChanged?.Invoke(this, new DiscoveryProgressEventArgs(ServiceName, current, total, target, status));
        }

        // Placeholder methods - implement based on existing WS-Discovery logic
        private string CreateWsDiscoveryProbeMessage(string deviceType) => "";
        private DiscoveredDevice? ParseWsDiscoveryResponse(byte[] buffer, IPEndPoint remoteEndPoint) => null;

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
                    DisposeClients();
                }
                _disposed = true;
            }
        }
    }
}