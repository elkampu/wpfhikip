using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml;

using wpfhikip.Discovery.Core;
using wpfhikip.Discovery.Models;

namespace wpfhikip.Discovery.Protocols.OnvifProbe
{
    /// <summary>
    /// ONVIF-specific WS-Discovery probe service for finding ONVIF cameras
    /// Enhanced to listen on all available network interfaces
    /// </summary>
    public class OnvifProbeDiscoveryService : INetworkDiscoveryService, IDisposable
    {
        public string ServiceName => "ONVIF Probe";
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

                var deviceTypes = OnvifProbeConstants.GetOnvifDeviceTypes();
                var totalTypes = deviceTypes.Length;

                for (int i = 0; i < totalTypes; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var deviceType = deviceTypes[i];
                    ReportProgress(i, totalTypes, deviceType, $"Probing for {deviceType}");

                    var foundDevices = await PerformMultiInterfaceOnvifProbeAsync(deviceType, cancellationToken);

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

                ReportProgress(totalTypes, totalTypes, "", $"ONVIF probe completed. Found {devices.Count} unique devices.");
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelled
            }
            catch (Exception ex)
            {
                ReportProgress(0, 0, "", $"ONVIF probe error: {ex.Message}");
            }

            return devices;
        }

        /// <summary>
        /// Discovers devices on a specific network segment
        /// </summary>
        public async Task<IEnumerable<DiscoveredDevice>> DiscoverDevicesAsync(string networkSegment, CancellationToken cancellationToken = default)
        {
            // ONVIF probe is multicast-based, so it discovers devices across all segments
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
                var multicastAddress = IPAddress.Parse(OnvifProbeConstants.MulticastAddress);

                ReportProgress(0, 0, "", $"Initializing ONVIF clients on {interfaces.Count} network interfaces");

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

                            ReportProgress(0, 0, "", $"ONVIF client initialized on {addressInfo.IPAddress} ({interfaceInfo.Name})");
                        }
                        catch (Exception ex)
                        {
                            ReportProgress(0, 0, "", $"Failed to initialize ONVIF client on {addressInfo.IPAddress}: {ex.Message}");
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

                    ReportProgress(0, 0, "", "General ONVIF client initialized (fallback)");
                }
                catch (Exception ex)
                {
                    ReportProgress(0, 0, "", $"Failed to initialize general ONVIF client: {ex.Message}");
                }

                if (!_udpClients.Any())
                {
                    throw new InvalidOperationException("Failed to initialize any ONVIF clients");
                }
            }
            catch (Exception ex)
            {
                DisposeClients();
                throw new InvalidOperationException($"Failed to initialize ONVIF clients: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Performs ONVIF probe across all network interfaces
        /// </summary>
        private async Task<List<DiscoveredDevice>> PerformMultiInterfaceOnvifProbeAsync(string deviceType, CancellationToken cancellationToken)
        {
            var devices = new List<DiscoveredDevice>();
            var multicastEndpoint = new IPEndPoint(IPAddress.Parse(OnvifProbeConstants.MulticastAddress), OnvifProbeConstants.MulticastPort);

            if (!_udpClients.Any())
                return devices;

            try
            {
                // Send ONVIF probe from all clients simultaneously
                var probeMessage = CreateOnvifProbeMessage(deviceType);
                var probeBytes = Encoding.UTF8.GetBytes(probeMessage);

                var sendTasks = _udpClients.Select(async client =>
                {
                    try
                    {
                        await client.SendAsync(probeBytes, probeBytes.Length, multicastEndpoint);
                    }
                    catch (Exception ex)
                    {
                        ReportProgress(0, 0, "", $"Failed to send ONVIF probe from client: {ex.Message}");
                    }
                });

                await Task.WhenAll(sendTasks);

                // Listen for responses from all clients
                var listenTimeout = DateTime.UtcNow.Add(TimeSpan.FromSeconds(10)); // Increased timeout for multi-interface
                var receiveTasks = new List<Task>();

                foreach (var client in _udpClients)
                {
                    var receiveTask = ListenForOnvifResponsesAsync(client, devices, listenTimeout, cancellationToken);
                    receiveTasks.Add(receiveTask);
                }

                await Task.WhenAll(receiveTasks);
            }
            catch (Exception ex)
            {
                ReportProgress(0, 0, "", $"Error during multi-interface ONVIF probe: {ex.Message}");
            }

            return devices;
        }

        /// <summary>
        /// Listens for ONVIF responses on a specific client
        /// </summary>
        private async Task ListenForOnvifResponsesAsync(UdpClient client, List<DiscoveredDevice> devices, DateTime timeout, CancellationToken cancellationToken)
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
                            var device = ParseOnvifProbeResponse(result.Buffer, result.RemoteEndPoint);

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
        /// Creates ONVIF probe message for specific device type
        /// </summary>
        private string CreateOnvifProbeMessage(string deviceType)
        {
            var messageId = Guid.NewGuid().ToString();
            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<soap:Envelope 
    xmlns:soap=""http://www.w3.org/2003/05/soap-envelope""
    xmlns:wsa=""http://www.w3.org/2005/08/addressing""
    xmlns:wsd=""http://schemas.xmlsoap.org/ws/2005/04/discovery""
    xmlns:tds=""http://www.onvif.org/ver10/device/wsdl""
    xmlns:dn=""http://www.onvif.org/ver10/network/wsdl"">
    <soap:Header>
        <wsa:MessageID>urn:uuid:{messageId}</wsa:MessageID>
        <wsa:To soap:mustUnderstand=""true"">urn:schemas-xmlsoap-org:ws:2005:04:discovery</wsa:To>
        <wsa:Action soap:mustUnderstand=""true"">{OnvifProbeConstants.ProbeAction}</wsa:Action>
    </soap:Header>
    <soap:Body>
        <wsd:Probe>
            <wsd:Types>{deviceType}</wsd:Types>
        </wsd:Probe>
    </soap:Body>
</soap:Envelope>";
        }

        /// <summary>
        /// Parses ONVIF probe response
        /// </summary>
        private DiscoveredDevice? ParseOnvifProbeResponse(byte[] responseBytes, IPEndPoint remoteEndPoint)
        {
            try
            {
                var responseText = Encoding.UTF8.GetString(responseBytes);

                if (!responseText.Contains("ProbeMatches"))
                    return null;

                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(responseText);

                var namespaceManager = new XmlNamespaceManager(xmlDoc.NameTable);
                namespaceManager.AddNamespace("soap", "http://www.w3.org/2003/05/soap-envelope");
                namespaceManager.AddNamespace("wsd", "http://schemas.xmlsoap.org/ws/2005/04/discovery");
                namespaceManager.AddNamespace("wsa", "http://www.w3.org/2005/08/addressing");

                var probeMatchNode = xmlDoc.SelectSingleNode("//wsd:ProbeMatch", namespaceManager);
                if (probeMatchNode == null)
                    return null;

                // Extract endpoint reference
                var endpointNode = probeMatchNode.SelectSingleNode("wsa:EndpointReference/wsa:Address", namespaceManager);
                var endpointAddress = endpointNode?.InnerText;

                // Extract XAddrs (service URLs)
                var xAddrsNode = probeMatchNode.SelectSingleNode("wsd:XAddrs", namespaceManager);
                var xAddrs = xAddrsNode?.InnerText?.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                // Extract Types
                var typesNode = probeMatchNode.SelectSingleNode("wsd:Types", namespaceManager);
                var types = typesNode?.InnerText;

                // Extract Scopes
                var scopesNode = probeMatchNode.SelectSingleNode("wsd:Scopes", namespaceManager);
                var scopes = scopesNode?.InnerText;

                // Create device
                var device = new DiscoveredDevice(remoteEndPoint.Address, remoteEndPoint.Port)
                {
                    UniqueId = endpointAddress ?? remoteEndPoint.Address.ToString(),
                    DeviceType = DeviceType.Camera,
                    Description = "ONVIF Camera"
                };

                device.DiscoveryMethods.Add(DiscoveryMethod.ONVIFProbe);
                device.DiscoveryData["ONVIF_EndpointReference"] = endpointAddress ?? "";
                device.DiscoveryData["ONVIF_Types"] = types ?? "";
                device.DiscoveryData["ONVIF_Scopes"] = scopes ?? "";

                // Extract service URLs
                if (xAddrs != null && xAddrs.Any())
                {
                    device.DiscoveryData["ONVIF_XAddrs"] = string.Join(", ", xAddrs);

                    // Try to extract port from service URL
                    var firstUrl = xAddrs[0];
                    if (Uri.TryCreate(firstUrl, UriKind.Absolute, out var uri))
                    {
                        device.Port = uri.Port;
                        device.DiscoveryData["ONVIF_ServiceURL"] = firstUrl;
                    }
                }

                // Parse ONVIF scopes for device information
                if (!string.IsNullOrEmpty(scopes))
                {
                    var (name, hardware, location, type) = OnvifProbeConstants.ParseOnvifScopes(scopes);

                    if (!string.IsNullOrEmpty(name))
                    {
                        device.Name = name;
                    }

                    if (!string.IsNullOrEmpty(hardware))
                    {
                        device.DiscoveryData["ONVIF_Hardware"] = hardware;

                        // Try to extract manufacturer and model from hardware string
                        var parts = hardware.Split('-', '_', ' ');
                        if (parts.Length > 0)
                        {
                            device.Manufacturer = DetermineManufacturer(parts[0]);
                        }
                        if (parts.Length > 1)
                        {
                            device.Model = string.Join(" ", parts.Skip(1));
                        }
                    }

                    if (!string.IsNullOrEmpty(location))
                    {
                        device.DiscoveryData["ONVIF_Location"] = location;
                    }

                    if (!string.IsNullOrEmpty(type))
                    {
                        device.DiscoveryData["ONVIF_DeviceType"] = type;
                        device.Description = $"ONVIF {type}";
                    }
                }

                // Determine more specific device type based on ONVIF types
                if (!string.IsNullOrEmpty(types))
                {
                    if (types.Contains("NetworkVideoRecorder"))
                    {
                        device.DeviceType = DeviceType.NVR;
                        device.Description = "ONVIF Network Video Recorder";
                    }
                    else if (types.Contains("NetworkVideoDisplay"))
                    {
                        device.DeviceType = DeviceType.Monitor;
                        device.Description = "ONVIF Network Video Display";
                    }
                }

                return device;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Determines manufacturer from hardware identifier
        /// </summary>
        private string DetermineManufacturer(string hardwareId)
        {
            if (string.IsNullOrEmpty(hardwareId))
                return "Unknown";

            var id = hardwareId.ToLower();

            return id switch
            {
                var h when h.StartsWith("hik") || h.StartsWith("ds-") => "Hikvision",
                var h when h.StartsWith("axis") => "Axis Communications",
                var h when h.StartsWith("dahua") || h.StartsWith("dh-") => "Dahua Technology",
                var h when h.StartsWith("hanwha") || h.StartsWith("snp-") => "Hanwha Techwin",
                var h when h.StartsWith("bosch") => "Bosch Security Systems",
                var h when h.StartsWith("sony") => "Sony",
                var h when h.StartsWith("panasonic") => "Panasonic",
                var h when h.StartsWith("vivotek") => "Vivotek",
                var h when h.StartsWith("acti") => "ACTi Corporation",
                var h when h.StartsWith("avigilon") => "Avigilon",
                _ => "Unknown"
            };
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