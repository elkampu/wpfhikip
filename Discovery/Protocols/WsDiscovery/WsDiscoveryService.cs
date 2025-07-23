using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

using wpfhikip.Discovery.Core;
using wpfhikip.Discovery.Models;
using wpfhikip.Protocols.Onvif;

namespace wpfhikip.Discovery.Protocols.WsDiscovery
{
    /// <summary>
    /// WS-Discovery service for discovering ONVIF and other WS-Discovery compatible devices
    /// </summary>
    public class WsDiscoveryService : INetworkDiscoveryService, IDisposable
    {
        public string ServiceName => "WS-Discovery";
        public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(15);

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

                var probeTargets = WsDiscoveryConstants.GetCommonDeviceTypes();
                var totalTargets = probeTargets.Length;
                var currentTarget = 0;

                foreach (var deviceType in probeTargets)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    currentTarget++;
                    ReportProgress(currentTarget, totalTargets, deviceType, $"Probing for {deviceType}");

                    var foundDevices = await PerformProbeAsync(deviceType, cancellationToken);
                    devices.AddRange(foundDevices);

                    // Small delay between probes
                    await Task.Delay(200, cancellationToken);
                }

                ReportProgress(totalTargets, totalTargets, "", "WS-Discovery completed");
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
            // WS-Discovery is multicast-based, filter results after discovery
            var allDevices = await DiscoverDevicesAsync(cancellationToken);

            if (string.IsNullOrEmpty(networkSegment))
                return allDevices;

            return allDevices.Where(device =>
                device.IPAddress != null &&
                NetworkUtils.IsIPInSegment(device.IPAddress, networkSegment));
        }

        /// <summary>
        /// Performs WS-Discovery probe for specific device types
        /// </summary>
        private async Task<List<DiscoveredDevice>> PerformProbeAsync(string deviceTypes, CancellationToken cancellationToken)
        {
            var devices = new List<DiscoveredDevice>();

            if (_udpClient == null)
                return devices;

            try
            {
                // Create and send probe request
                var probeRequest = WsDiscoveryMessage.CreateProbeRequest(deviceTypes);
                var probeBytes = Encoding.UTF8.GetBytes(probeRequest);
                var multicastEndpoint = new IPEndPoint(
                    IPAddress.Parse(WsDiscoveryConstants.MulticastAddress),
                    WsDiscoveryConstants.MulticastPort);

                await _udpClient.SendAsync(probeBytes, probeBytes.Length, multicastEndpoint);

                // Listen for probe matches
                var listenTimeout = DateTime.UtcNow.Add(TimeSpan.FromSeconds(10));

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
                            var device = await ParseProbeMatchAsync(result.Buffer, result.RemoteEndPoint);

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
                // Ignore individual probe errors
            }

            return devices;
        }

        /// <summary>
        /// Parses WS-Discovery probe match response
        /// </summary>
        private async Task<DiscoveredDevice?> ParseProbeMatchAsync(byte[] responseBytes, IPEndPoint remoteEndPoint)
        {
            try
            {
                var responseText = Encoding.UTF8.GetString(responseBytes);
                var probeMatch = WsDiscoveryMessage.ParseProbeMatch(responseText);

                if (probeMatch == null)
                    return null;

                var device = new DiscoveredDevice(remoteEndPoint.Address, remoteEndPoint.Port)
                {
                    UniqueId = probeMatch.EndpointReference ?? remoteEndPoint.Address.ToString(),
                    DeviceType = DetermineDeviceType(probeMatch.Types, probeMatch.Scopes),
                    Description = "WS-Discovery Device"
                };

                device.DiscoveryMethods.Add(DiscoveryMethod.WSDiscovery);
                device.DiscoveryData["WS-Discovery_Response"] = probeMatch;

                // Extract device information from scopes
                ExtractDeviceInfoFromScopes(device, probeMatch.Scopes);

                // Try to get additional device information via ONVIF if it's a camera
                if (device.DeviceType == DeviceType.Camera)
                {
                    await EnrichOnvifDeviceAsync(device, probeMatch.XAddrs);
                }

                return device;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Determines device type from WS-Discovery types and scopes
        /// </summary>
        private DeviceType DetermineDeviceType(string? types, List<string> scopes)
        {
            if (string.IsNullOrEmpty(types))
                return DeviceType.Unknown;

            var typesList = types.ToLower();

            // ONVIF network video devices
            if (typesList.Contains("networkvideotransmitter") ||
                typesList.Contains("networkvideorecorder") ||
                typesList.Contains("device") && scopes.Any(s => s.ToLower().Contains("onvif")))
            {
                return DeviceType.Camera;
            }

            // Network video recorder
            if (typesList.Contains("recorder"))
            {
                return DeviceType.NVR;
            }

            // Generic device
            if (typesList.Contains("device"))
            {
                return DeviceType.Unknown;
            }

            return DeviceType.Unknown;
        }

        /// <summary>
        /// Extracts device information from WS-Discovery scopes
        /// </summary>
        private void ExtractDeviceInfoFromScopes(DiscoveredDevice device, List<string> scopes)
        {
            foreach (var scope in scopes)
            {
                var lowerScope = scope.ToLower();

                // Extract name
                if (lowerScope.Contains("name/") && string.IsNullOrEmpty(device.Name))
                {
                    var name = ExtractScopeValue(scope, "name/");
                    if (!string.IsNullOrEmpty(name))
                        device.Name = Uri.UnescapeDataString(name);
                }

                // Extract hardware information
                if (lowerScope.Contains("hardware/") && string.IsNullOrEmpty(device.Model))
                {
                    var hardware = ExtractScopeValue(scope, "hardware/");
                    if (!string.IsNullOrEmpty(hardware))
                        device.Model = Uri.UnescapeDataString(hardware);
                }

                // Extract location
                if (lowerScope.Contains("location/") && string.IsNullOrEmpty(device.Description))
                {
                    var location = ExtractScopeValue(scope, "location/");
                    if (!string.IsNullOrEmpty(location))
                        device.Description = Uri.UnescapeDataString(location);
                }

                // Detect manufacturer from scope patterns
                if (string.IsNullOrEmpty(device.Manufacturer))
                {
                    if (lowerScope.Contains("axis.com"))
                        device.Manufacturer = "Axis";
                    else if (lowerScope.Contains("hikvision"))
                        device.Manufacturer = "Hikvision";
                    else if (lowerScope.Contains("dahua"))
                        device.Manufacturer = "Dahua";
                    else if (lowerScope.Contains("bosch"))
                        device.Manufacturer = "Bosch";
                    else if (lowerScope.Contains("hanwha"))
                        device.Manufacturer = "Hanwha";
                }
            }
        }

        /// <summary>
        /// Extracts value from a scope string
        /// </summary>
        private string? ExtractScopeValue(string scope, string prefix)
        {
            var index = scope.ToLower().IndexOf(prefix.ToLower());
            if (index == -1) return null;

            var startIndex = index + prefix.Length;
            var endIndex = scope.IndexOf('/', startIndex);

            if (endIndex == -1)
                endIndex = scope.Length;

            if (startIndex >= endIndex)
                return null;

            return scope.Substring(startIndex, endIndex - startIndex);
        }

        /// <summary>
        /// Enriches device information using ONVIF GetDeviceInformation
        /// </summary>
        private async Task EnrichOnvifDeviceAsync(DiscoveredDevice device, List<string> xAddrs)
        {
            if (!xAddrs.Any() || device.IPAddress == null)
                return;

            try
            {
                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5);

                // Try to get device information without credentials first
                var deviceInfoRequest = OnvifSoapTemplates.CreateGetDeviceInformationRequest();
                var content = new System.Net.Http.StringContent(deviceInfoRequest, Encoding.UTF8, "application/soap+xml");

                foreach (var xAddr in xAddrs.Take(2)) // Try first 2 addresses
                {
                    try
                    {
                        var response = await httpClient.PostAsync(xAddr, content);
                        if (response.IsSuccessStatusCode)
                        {
                            var responseContent = await response.Content.ReadAsStringAsync();
                            var deviceInfo = OnvifSoapTemplates.ExtractDeviceInfo(responseContent);

                            // Update device with ONVIF information
                            if (deviceInfo.ContainsKey("Manufacturer") && string.IsNullOrEmpty(device.Manufacturer))
                                device.Manufacturer = deviceInfo["Manufacturer"];

                            if (deviceInfo.ContainsKey("Model") && string.IsNullOrEmpty(device.Model))
                                device.Model = deviceInfo["Model"];

                            if (deviceInfo.ContainsKey("FirmwareVersion") && string.IsNullOrEmpty(device.FirmwareVersion))
                                device.FirmwareVersion = deviceInfo["FirmwareVersion"];

                            if (deviceInfo.ContainsKey("SerialNumber") && string.IsNullOrEmpty(device.SerialNumber))
                                device.SerialNumber = deviceInfo["SerialNumber"];

                            device.DiscoveryData["ONVIF_DeviceInfo"] = deviceInfo;
                            device.Capabilities.Add("ONVIF");
                            break;
                        }
                    }
                    catch
                    {
                        // Try next address
                        continue;
                    }
                }
            }
            catch
            {
                // ONVIF enrichment failed - not critical
            }
        }

        /// <summary>
        /// Initializes UDP client for WS-Discovery communication
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

                // Join multicast group
                var multicastAddress = IPAddress.Parse(WsDiscoveryConstants.MulticastAddress);
                _udpClient.JoinMulticastGroup(multicastAddress);
            }
            catch (Exception ex)
            {
                _udpClient?.Dispose();
                _udpClient = null;
                throw new InvalidOperationException($"Failed to initialize WS-Discovery client: {ex.Message}", ex);
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