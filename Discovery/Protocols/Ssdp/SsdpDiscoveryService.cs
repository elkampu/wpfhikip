using System.Net;
using System.Net.Sockets;
using System.Text;

using wpfhikip.Discovery.Core;
using wpfhikip.Discovery.Models;

namespace wpfhikip.Discovery.Protocols.Ssdp
{
    /// <summary>
    /// SSDP (Simple Service Discovery Protocol) / UPnP discovery service
    /// Enhanced to listen on all available network interfaces
    /// </summary>
    public class SsdpDiscoveryService : INetworkDiscoveryService, IDisposable
    {
        public string ServiceName => "SSDP/UPnP";
        public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(30);

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

                var searchTargets = SsdpConstants.GetCommonSearchTargets();
                var totalTargets = searchTargets.Length;
                var currentTarget = 0;

                foreach (var searchTarget in searchTargets)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    currentTarget++;
                    ReportProgress(currentTarget, totalTargets, searchTarget, $"Searching for {searchTarget}");

                    var foundDevices = await PerformMultiInterfaceMSearchAsync(searchTarget, cancellationToken);

                    // Deduplicate devices based on UniqueId
                    foreach (var device in foundDevices)
                    {
                        if (!devices.Any(d => d.UniqueId == device.UniqueId))
                        {
                            devices.Add(device);
                        }
                    }

                    // Small delay between searches to avoid flooding the network
                    await Task.Delay(100, cancellationToken);
                }

                ReportProgress(totalTargets, totalTargets, "", $"SSDP discovery completed. Found {devices.Count} unique devices.");
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelled
            }
            catch (Exception ex)
            {
                // Log error but don't throw - return partial results
                ReportProgress(0, 0, "", $"SSDP error: {ex.Message}");
            }

            return devices;
        }

        /// <summary>
        /// Discovers devices on a specific network segment
        /// </summary>
        public async Task<IEnumerable<DiscoveredDevice>> DiscoverDevicesAsync(string networkSegment, CancellationToken cancellationToken = default)
        {
            // SSDP is multicast-based, so it discovers devices across all segments
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
                var multicastAddress = IPAddress.Parse(SsdpConstants.MulticastAddress);

                ReportProgress(0, 0, "", $"Initializing SSDP clients on {interfaces.Count} network interfaces");

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

                            ReportProgress(0, 0, "", $"SSDP client initialized on {addressInfo.IPAddress} ({interfaceInfo.Name})");
                        }
                        catch (Exception ex)
                        {
                            ReportProgress(0, 0, "", $"Failed to initialize SSDP client on {addressInfo.IPAddress}: {ex.Message}");
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

                    ReportProgress(0, 0, "", "General SSDP client initialized (fallback)");
                }
                catch (Exception ex)
                {
                    ReportProgress(0, 0, "", $"Failed to initialize general SSDP client: {ex.Message}");
                }

                if (!_udpClients.Any())
                {
                    throw new InvalidOperationException("Failed to initialize any SSDP clients");
                }
            }
            catch (Exception ex)
            {
                DisposeClients();
                throw new InvalidOperationException($"Failed to initialize SSDP clients: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Performs M-SEARCH across all network interfaces
        /// </summary>
        private async Task<List<DiscoveredDevice>> PerformMultiInterfaceMSearchAsync(string searchTarget, CancellationToken cancellationToken)
        {
            var devices = new List<DiscoveredDevice>();
            var multicastEndpoint = new IPEndPoint(IPAddress.Parse(SsdpConstants.MulticastAddress), SsdpConstants.MulticastPort);

            if (!_udpClients.Any())
                return devices;

            try
            {
                // Send M-SEARCH request from all clients simultaneously
                var searchMessage = SsdpMessage.CreateMSearchRequest(searchTarget);
                var searchBytes = Encoding.UTF8.GetBytes(searchMessage);

                var sendTasks = _udpClients.Select(async client =>
                {
                    try
                    {
                        await client.SendAsync(searchBytes, searchBytes.Length, multicastEndpoint);
                    }
                    catch (Exception ex)
                    {
                        ReportProgress(0, 0, "", $"Failed to send SSDP request from client: {ex.Message}");
                    }
                });

                await Task.WhenAll(sendTasks);

                // Listen for responses from all clients
                var listenTimeout = DateTime.UtcNow.Add(TimeSpan.FromSeconds(8)); // Increased timeout for multi-interface
                var receiveTasks = new List<Task>();

                foreach (var client in _udpClients)
                {
                    var receiveTask = ListenForResponsesAsync(client, devices, listenTimeout, cancellationToken);
                    receiveTasks.Add(receiveTask);
                }

                await Task.WhenAll(receiveTasks);
            }
            catch (Exception ex)
            {
                ReportProgress(0, 0, "", $"Error during multi-interface M-SEARCH: {ex.Message}");
            }

            return devices;
        }

        /// <summary>
        /// Listens for SSDP responses on a specific client
        /// </summary>
        private async Task ListenForResponsesAsync(UdpClient client, List<DiscoveredDevice> devices, DateTime timeout, CancellationToken cancellationToken)
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
                            var device = await ParseSsdpResponseAsync(result.Buffer, result.RemoteEndPoint);

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

        /// <summary>
        /// Parses SSDP response and creates DiscoveredDevice
        /// </summary>
        private async Task<DiscoveredDevice?> ParseSsdpResponseAsync(byte[] responseBytes, IPEndPoint remoteEndPoint)
        {
            try
            {
                var responseText = Encoding.UTF8.GetString(responseBytes);
                var ssdpResponse = SsdpMessage.Parse(responseText);

                if (ssdpResponse == null || string.IsNullOrEmpty(ssdpResponse.Location))
                    return null;

                var device = new DiscoveredDevice(remoteEndPoint.Address, remoteEndPoint.Port)
                {
                    UniqueId = ssdpResponse.USN ?? remoteEndPoint.Address.ToString(),
                    DeviceType = DetermineDeviceType(ssdpResponse.ST, ssdpResponse.Server),
                    Description = ssdpResponse.ST ?? "SSDP Device"
                };

                device.DiscoveryMethods.Add(DiscoveryMethod.SSDP);
                device.DiscoveryData["SSDP_Response"] = ssdpResponse;
                device.DiscoveryData["SSDP_Location"] = ssdpResponse.Location;

                // Try to get device details from the location URL
                await EnrichDeviceFromLocationAsync(device, ssdpResponse.Location);

                return device;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Enriches device information by fetching device description from location URL
        /// </summary>
        private async Task EnrichDeviceFromLocationAsync(DiscoveredDevice device, string locationUrl)
        {
            try
            {
                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5);

                var response = await httpClient.GetStringAsync(locationUrl);

                // Parse XML device description (simplified)
                if (response.Contains("<device>"))
                {
                    device.Name = ExtractXmlValue(response, "friendlyName") ?? device.Name;
                    device.Manufacturer = ExtractXmlValue(response, "manufacturer") ?? device.Manufacturer;
                    device.Model = ExtractXmlValue(response, "modelName") ?? device.Model;
                    device.SerialNumber = ExtractXmlValue(response, "serialNumber") ?? device.SerialNumber;
                    device.FirmwareVersion = ExtractXmlValue(response, "firmwareVersion") ?? device.FirmwareVersion;

                    device.DiscoveryData["DeviceDescription"] = response;
                }
            }
            catch
            {
                // Failed to fetch device description - not critical
            }
        }

        /// <summary>
        /// Extracts value from XML content (simple string-based extraction)
        /// </summary>
        private string? ExtractXmlValue(string xml, string tagName)
        {
            var startTag = $"<{tagName}>";
            var endTag = $"</{tagName}>";

            var startIndex = xml.IndexOf(startTag);
            if (startIndex == -1) return null;

            startIndex += startTag.Length;
            var endIndex = xml.IndexOf(endTag, startIndex);
            if (endIndex == -1) return null;

            var value = xml.Substring(startIndex, endIndex - startIndex).Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>
        /// Determines device type based on SSDP service type and server information
        /// </summary>
        private DeviceType DetermineDeviceType(string? serviceType, string? server)
        {
            if (string.IsNullOrEmpty(serviceType))
                return DeviceType.Unknown;

            var st = serviceType.ToLower();
            var srv = server?.ToLower() ?? "";

            // Network infrastructure
            if (st.Contains("internetgatewaydevice") || st.Contains("wanconnectiondevice"))
                return DeviceType.Router;

            // Media devices
            if (st.Contains("mediaserver") || st.Contains("mediarenderer"))
                return DeviceType.MediaServer;

            // Smart TV and streaming
            if (srv.Contains("roku") || srv.Contains("chromecast") || st.Contains("dial"))
                return DeviceType.StreamingDevice;

            if (srv.Contains("samsung") && srv.Contains("tv"))
                return DeviceType.SmartTV;

            // Printers
            if (st.Contains("printer") || srv.Contains("printer"))
                return DeviceType.Printer;

            // NAS devices
            if (srv.Contains("synology") || srv.Contains("qnap") || srv.Contains("nas"))
                return DeviceType.NAS;

            // Security cameras (some support UPnP)
            if (srv.Contains("camera") || srv.Contains("ipcam") || st.Contains("videosource"))
                return DeviceType.Camera;

            return DeviceType.Unknown;
        }

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