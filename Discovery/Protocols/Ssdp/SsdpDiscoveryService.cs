using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using wpfhikip.Discovery.Core;
using wpfhikip.Discovery.Models;

namespace wpfhikip.Discovery.Protocols.Ssdp
{
    /// <summary>
    /// SSDP (Simple Service Discovery Protocol) / UPnP discovery service
    /// </summary>
    public class SsdpDiscoveryService : INetworkDiscoveryService, IDisposable
    {
        public string ServiceName => "SSDP/UPnP";
        public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(30);

        private UdpClient? _udpClient;
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
                InitializeUdpClient();

                var searchTargets = SsdpConstants.GetCommonSearchTargets();
                var totalTargets = searchTargets.Length;
                var currentTarget = 0;

                foreach (var searchTarget in searchTargets)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    currentTarget++;
                    ReportProgress(currentTarget, totalTargets, searchTarget, $"Searching for {searchTarget}");

                    var foundDevices = await PerformMSearchAsync(searchTarget, cancellationToken);
                    devices.AddRange(foundDevices);

                    // Small delay between searches to avoid flooding the network
                    await Task.Delay(100, cancellationToken);
                }

                ReportProgress(totalTargets, totalTargets, "", "SSDP discovery completed");
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
        /// Performs M-SEARCH for a specific device type
        /// </summary>
        private async Task<List<DiscoveredDevice>> PerformMSearchAsync(string searchTarget, CancellationToken cancellationToken)
        {
            var devices = new List<DiscoveredDevice>();

            if (_udpClient == null)
                return devices;

            try
            {
                // Send M-SEARCH request
                var searchMessage = SsdpMessage.CreateMSearchRequest(searchTarget);
                var searchBytes = Encoding.UTF8.GetBytes(searchMessage);
                var multicastEndpoint = new IPEndPoint(IPAddress.Parse(SsdpConstants.MulticastAddress), SsdpConstants.MulticastPort);

                await _udpClient.SendAsync(searchBytes, searchBytes.Length, multicastEndpoint);

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
                            var device = await ParseSsdpResponseAsync(result.Buffer, result.RemoteEndPoint);

                            if (device != null && !devices.Any(d => d.UniqueId == device.UniqueId))
                            {
                                devices.Add(device);
                                DeviceDiscovered?.Invoke(this, new DeviceDiscoveredEventArgs(device, ServiceName));
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
                // Ignore individual search errors
            }

            return devices;
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
        /// Initializes UDP client for SSDP communication
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

                // Join multicast group for receiving responses
                var multicastAddress = IPAddress.Parse(SsdpConstants.MulticastAddress);
                _udpClient.JoinMulticastGroup(multicastAddress);
            }
            catch (Exception ex)
            {
                _udpClient?.Dispose();
                _udpClient = null;
                throw new InvalidOperationException($"Failed to initialize SSDP client: {ex.Message}", ex);
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