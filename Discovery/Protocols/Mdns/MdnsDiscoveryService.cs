using System.Net;
using System.Net.Sockets;

using wpfhikip.Discovery.Core;
using wpfhikip.Discovery.Models;

namespace wpfhikip.Discovery.Protocols.Mdns
{
    /// <summary>
    /// mDNS (Multicast DNS) / Bonjour discovery service
    /// </summary>
    public class MdnsDiscoveryService : INetworkDiscoveryService, IDisposable
    {
        public string ServiceName => "mDNS/Bonjour";
        public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(10);

        private UdpClient? _udpClient;
        private bool _disposed = false;

        public event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;
        public event EventHandler<DiscoveryProgressEventArgs>? ProgressChanged;

        /// <summary>
        /// Discovers devices using mDNS/Bonjour
        /// </summary>
        public async Task<IEnumerable<DiscoveredDevice>> DiscoverDevicesAsync(CancellationToken cancellationToken = default)
        {
            var devices = new List<DiscoveredDevice>();

            try
            {
                InitializeUdpClient();

                var serviceTypes = MdnsConstants.GetCommonServiceTypes();
                var totalServices = serviceTypes.Length;
                var currentService = 0;

                foreach (var serviceType in serviceTypes)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    currentService++;
                    ReportProgress(currentService, totalServices, serviceType, $"Querying for {serviceType}");

                    var foundDevices = await QueryServiceTypeAsync(serviceType, cancellationToken);
                    devices.AddRange(foundDevices);

                    // Small delay between queries
                    await Task.Delay(300, cancellationToken);
                }

                ReportProgress(totalServices, totalServices, "", "mDNS discovery completed");
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelled
            }
            catch (Exception ex)
            {
                ReportProgress(0, 0, "", $"mDNS error: {ex.Message}");
            }

            return devices;
        }

        /// <summary>
        /// Discovers devices on a specific network segment
        /// </summary>
        public async Task<IEnumerable<DiscoveredDevice>> DiscoverDevicesAsync(string networkSegment, CancellationToken cancellationToken = default)
        {
            // mDNS is multicast-based, filter results after discovery
            var allDevices = await DiscoverDevicesAsync(cancellationToken);

            if (string.IsNullOrEmpty(networkSegment))
                return allDevices;

            return allDevices.Where(device =>
                device.IPAddress != null &&
                NetworkUtils.IsIPInSegment(device.IPAddress, networkSegment));
        }

        /// <summary>
        /// Queries for a specific mDNS service type
        /// </summary>
        private async Task<List<DiscoveredDevice>> QueryServiceTypeAsync(string serviceType, CancellationToken cancellationToken)
        {
            var devices = new List<DiscoveredDevice>();

            if (_udpClient == null)
                return devices;

            try
            {
                // Create mDNS query
                var query = MdnsMessage.CreateQuery(serviceType);
                var queryBytes = query.ToByteArray();
                var multicastEndpoint = new IPEndPoint(IPAddress.Parse(MdnsConstants.MulticastAddress), MdnsConstants.MulticastPort);

                await _udpClient.SendAsync(queryBytes, queryBytes.Length, multicastEndpoint);

                // Listen for responses
                var listenTimeout = DateTime.UtcNow.Add(TimeSpan.FromSeconds(5));

                while (DateTime.UtcNow < listenTimeout && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var receiveTask = _udpClient.ReceiveAsync();
                        var timeoutTask = Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);

                        var completedTask = await Task.WhenAny(receiveTask, timeoutTask);

                        if (completedTask == receiveTask)
                        {
                            var result = await receiveTask;
                            var foundDevices = ParseMdnsResponse(result.Buffer, result.RemoteEndPoint, serviceType);

                            foreach (var device in foundDevices)
                            {
                                if (!devices.Any(d => d.UniqueId == device.UniqueId))
                                {
                                    devices.Add(device);
                                    DeviceDiscovered?.Invoke(this, new DeviceDiscoveredEventArgs(device, ServiceName));
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
                }
            }
            catch (Exception)
            {
                // Ignore individual query errors
            }

            return devices;
        }

        /// <summary>
        /// Parses mDNS response messages
        /// </summary>
        private List<DiscoveredDevice> ParseMdnsResponse(byte[] responseBytes, IPEndPoint remoteEndPoint, string serviceType)
        {
            var devices = new List<DiscoveredDevice>();

            try
            {
                var response = MdnsMessage.Parse(responseBytes);
                if (response == null || !response.IsResponse)
                    return devices;

                var serviceRecords = response.Answers
                    .Where(r => r.Type == MdnsRecordType.PTR && r.Name.Contains(serviceType))
                    .ToList();

                foreach (var serviceRecord in serviceRecords)
                {
                    var device = CreateDeviceFromMdnsRecord(response, serviceRecord, serviceType);
                    if (device != null)
                    {
                        devices.Add(device);
                    }
                }
            }
            catch
            {
                // Parsing error - ignore
            }

            return devices;
        }

        /// <summary>
        /// Creates a DiscoveredDevice from mDNS records
        /// </summary>
        private DiscoveredDevice? CreateDeviceFromMdnsRecord(MdnsMessage response, MdnsRecord serviceRecord, string serviceType)
        {
            try
            {
                // Extract service instance name
                var serviceName = serviceRecord.Data;
                if (string.IsNullOrEmpty(serviceName))
                    return null;

                // Find SRV record for this service
                var srvRecord = response.Answers
                    .FirstOrDefault(r => r.Type == MdnsRecordType.SRV && r.Name == serviceName);

                // Find A records for the target host
                var targetHost = srvRecord?.Data?.Split(' ').LastOrDefault();
                var aRecord = response.Answers
                    .FirstOrDefault(r => r.Type == MdnsRecordType.A && r.Name == targetHost);

                if (aRecord?.Data == null || !IPAddress.TryParse(aRecord.Data, out var ipAddress))
                    return null;

                // Extract port from SRV record
                var port = 80; // default
                if (srvRecord?.Data != null)
                {
                    var srvParts = srvRecord.Data.Split(' ');
                    if (srvParts.Length >= 3 && int.TryParse(srvParts[2], out var srvPort))
                    {
                        port = srvPort;
                    }
                }

                var device = new DiscoveredDevice(ipAddress, port)
                {
                    UniqueId = serviceName,
                    Name = ExtractServiceInstanceName(serviceName),
                    DeviceType = DetermineDeviceType(serviceType, serviceName),
                    Description = $"mDNS service: {serviceType}"
                };

                device.DiscoveryMethods.Add(DiscoveryMethod.mDNS);
                device.DiscoveryData["mDNS_ServiceType"] = serviceType;
                device.DiscoveryData["mDNS_ServiceName"] = serviceName;
                device.DiscoveryData["mDNS_Response"] = response;

                // Extract additional information from TXT records
                var txtRecord = response.Answers
                    .FirstOrDefault(r => r.Type == MdnsRecordType.TXT && r.Name == serviceName);

                if (txtRecord?.Data != null)
                {
                    ExtractDeviceInfoFromTxt(device, txtRecord.Data);
                }

                return device;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Extracts service instance name from full service name
        /// </summary>
        private string ExtractServiceInstanceName(string fullServiceName)
        {
            // Format: "Device Name._service._tcp.local."
            var parts = fullServiceName.Split('.');
            return parts.Length > 0 ? parts[0] : fullServiceName;
        }

        /// <summary>
        /// Determines device type from mDNS service type
        /// </summary>
        private DeviceType DetermineDeviceType(string serviceType, string serviceName)
        {
            var service = serviceType.ToLower();
            var name = serviceName.ToLower();

            // Printers
            if (service.Contains("ipp") || service.Contains("printer") || service.Contains("pdl-datastream"))
                return DeviceType.Printer;

            // Scanners
            if (service.Contains("scanner") || service.Contains("scan"))
                return DeviceType.Scanner;

            // Media devices
            if (service.Contains("airplay") || service.Contains("raop"))
                return DeviceType.StreamingDevice;

            if (service.Contains("mediaserver") || service.Contains("upnp"))
                return DeviceType.MediaServer;

            // Smart home devices
            if (service.Contains("homekit") || name.Contains("homekit"))
                return DeviceType.SmartSensor; // Generic smart device

            // Apple devices
            if (service.Contains("afpovertcp") || name.Contains("apple") || name.Contains("mac"))
                return DeviceType.Workstation;

            // Network infrastructure
            if (service.Contains("router") || name.Contains("router"))
                return DeviceType.Router;

            // Web services (might be cameras, NAS, etc.)
            if (service.Contains("http"))
            {
                if (name.Contains("camera") || name.Contains("ipcam"))
                    return DeviceType.Camera;
                if (name.Contains("nas") || name.Contains("synology") || name.Contains("qnap"))
                    return DeviceType.NAS;
            }

            return DeviceType.Unknown;
        }

        /// <summary>
        /// Extracts device information from TXT record data
        /// </summary>
        private void ExtractDeviceInfoFromTxt(DiscoveredDevice device, string txtData)
        {
            try
            {
                // TXT records contain key=value pairs
                var attributes = ParseTxtAttributes(txtData);

                foreach (var attr in attributes)
                {
                    switch (attr.Key.ToLower())
                    {
                        case "model":
                        case "md":
                            if (string.IsNullOrEmpty(device.Model))
                                device.Model = attr.Value;
                            break;

                        case "manufacturer":
                        case "mfg":
                            if (string.IsNullOrEmpty(device.Manufacturer))
                                device.Manufacturer = attr.Value;
                            break;

                        case "version":
                        case "ver":
                        case "firmware":
                            if (string.IsNullOrEmpty(device.FirmwareVersion))
                                device.FirmwareVersion = attr.Value;
                            break;

                        case "serial":
                        case "sn":
                            if (string.IsNullOrEmpty(device.SerialNumber))
                                device.SerialNumber = attr.Value;
                            break;

                        case "features":
                            device.Capabilities.UnionWith(attr.Value.Split(','));
                            break;
                    }

                    // Store all attributes
                    device.DiscoveryData[$"mDNS_TXT_{attr.Key}"] = attr.Value;
                }
            }
            catch
            {
                // TXT parsing error - not critical
            }
        }

        /// <summary>
        /// Parses TXT record attributes
        /// </summary>
        private Dictionary<string, string> ParseTxtAttributes(string txtData)
        {
            var attributes = new Dictionary<string, string>();

            // Simple parsing - in real implementation, would need proper TXT record parsing
            var parts = txtData.Split('\0', StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                var equalIndex = part.IndexOf('=');
                if (equalIndex > 0)
                {
                    var key = part.Substring(0, equalIndex);
                    var value = part.Substring(equalIndex + 1);
                    attributes[key] = value;
                }
            }

            return attributes;
        }

        /// <summary>
        /// Initializes UDP client for mDNS communication
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

                // Join mDNS multicast group
                var multicastAddress = IPAddress.Parse(MdnsConstants.MulticastAddress);
                _udpClient.JoinMulticastGroup(multicastAddress);
            }
            catch (Exception ex)
            {
                _udpClient?.Dispose();
                _udpClient = null;
                throw new InvalidOperationException($"Failed to initialize mDNS client: {ex.Message}", ex);
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
                    try
                    {
                        _udpClient?.Close();
                        _udpClient?.Dispose();
                    }
                    catch
                    {
                        // Ignore disposal errors
                    }
                }

                _disposed = true;
            }
        }
    }
}