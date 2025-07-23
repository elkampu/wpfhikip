using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

using wpfhikip.Discovery.Core;
using wpfhikip.Discovery.Models;

namespace wpfhikip.Discovery.Protocols.PortScan
{
    /// <summary>
    /// TCP/UDP port scanning discovery service
    /// </summary>
    public class PortScanService : IPortScanningService
    {
        public string ServiceName => "Port Scan";
        public TimeSpan DefaultTimeout => TimeSpan.FromMinutes(2);

        public event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;
        public event EventHandler<DiscoveryProgressEventArgs>? ProgressChanged;

        /// <summary>
        /// Discovers devices by scanning common ports on local networks
        /// </summary>
        public async Task<IEnumerable<DiscoveredDevice>> DiscoverDevicesAsync(CancellationToken cancellationToken = default)
        {
            var devices = new List<DiscoveredDevice>();

            try
            {
                var networkSegments = NetworkUtils.GetLocalNetworkSegments();

                foreach (var segment in networkSegments)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var segmentDevices = await DiscoverDevicesAsync(segment, cancellationToken);
                    devices.AddRange(segmentDevices);
                }
            }
            catch (Exception ex)
            {
                ReportProgress(0, 0, "", $"Port scan discovery error: {ex.Message}");
            }

            return devices;
        }

        /// <summary>
        /// Discovers devices on a specific network segment using port scanning
        /// </summary>
        public async Task<IEnumerable<DiscoveredDevice>> DiscoverDevicesAsync(string networkSegment, CancellationToken cancellationToken = default)
        {
            var devices = new List<DiscoveredDevice>();

            if (string.IsNullOrEmpty(networkSegment))
                return devices;

            try
            {
                var ipAddresses = NetworkUtils.GetIPAddressesInSegment(networkSegment);
                var commonPorts = PortScanConstants.GetCommonPorts();

                ReportProgress(0, ipAddresses.Count, networkSegment, $"Port scanning {ipAddresses.Count} addresses");

                using var semaphore = new SemaphoreSlim(20); // Limit concurrent scans

                var scanTasks = ipAddresses.Select(async (ip, index) =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        if (cancellationToken.IsCancellationRequested)
                            return;

                        ReportProgress(index + 1, ipAddresses.Count, ip.ToString(), "Scanning ports...");

                        var openPorts = await ScanPortsAsync(ip.ToString(), commonPorts, cancellationToken);
                        if (openPorts.Any())
                        {
                            var device = CreateDeviceFromPortScan(ip, openPorts);
                            lock (devices)
                            {
                                devices.Add(device);
                            }
                            DeviceDiscovered?.Invoke(this, new DeviceDiscoveredEventArgs(device, ServiceName));
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(scanTasks);

                ReportProgress(ipAddresses.Count, ipAddresses.Count, networkSegment,
                    $"Port scan completed - {devices.Count} devices with open ports found");
            }
            catch (Exception ex)
            {
                ReportProgress(0, 0, networkSegment, $"Error port scanning {networkSegment}: {ex.Message}");
            }

            return devices;
        }

        /// <summary>
        /// Scans specific ports on a target
        /// </summary>
        public async Task<IEnumerable<PortScanResult>> ScanPortsAsync(string target, IEnumerable<int> ports, CancellationToken cancellationToken = default)
        {
            var results = new List<PortScanResult>();

            if (!IPAddress.TryParse(target, out var ipAddress))
            {
                try
                {
                    var hostEntry = await Dns.GetHostEntryAsync(target);
                    ipAddress = hostEntry.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                    if (ipAddress == null)
                        return results;
                }
                catch
                {
                    return results;
                }
            }

            using var semaphore = new SemaphoreSlim(100); // Limit concurrent port scans

            var scanTasks = ports.Select(async port =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var result = await ScanSinglePortAsync(ipAddress, port, cancellationToken);
                    if (result.IsOpen)
                    {
                        lock (results)
                        {
                            results.Add(result);
                        }
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(scanTasks);
            return results;
        }

        /// <summary>
        /// Scans a range of ports on a target
        /// </summary>
        public async Task<IEnumerable<PortScanResult>> ScanPortRangeAsync(string target, int startPort, int endPort, CancellationToken cancellationToken = default)
        {
            var ports = Enumerable.Range(startPort, endPort - startPort + 1);
            return await ScanPortsAsync(target, ports, cancellationToken);
        }

        /// <summary>
        /// Scans a single port on a target
        /// </summary>
        private async Task<PortScanResult> ScanSinglePortAsync(IPAddress ipAddress, int port, CancellationToken cancellationToken)
        {
            var result = new PortScanResult
            {
                IPAddress = ipAddress,
                Port = port,
                Protocol = "TCP",
                IsOpen = false
            };

            try
            {
                using var tcpClient = new TcpClient();
                var connectTask = tcpClient.ConnectAsync(ipAddress, port);
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);

                var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                if (completedTask == connectTask && !connectTask.IsFaulted)
                {
                    await connectTask; // Ensure we handle any exceptions

                    if (tcpClient.Connected)
                    {
                        result.IsOpen = true;
                        result.Service = PortScanConstants.GetServiceName(port);

                        // Try to grab banner
                        try
                        {
                            result.Banner = await GrabBannerAsync(tcpClient, cancellationToken);
                        }
                        catch
                        {
                            // Banner grab failed - not critical
                        }
                    }
                }
            }
            catch
            {
                // Port is closed or filtered
            }

            return result;
        }

        /// <summary>
        /// Attempts to grab service banner
        /// </summary>
        private async Task<string?> GrabBannerAsync(TcpClient tcpClient, CancellationToken cancellationToken)
        {
            try
            {
                var stream = tcpClient.GetStream();
                var buffer = new byte[1024];

                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, combinedCts.Token);

                if (bytesRead > 0)
                {
                    return System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();
                }
            }
            catch
            {
                // Banner grab failed
            }

            return null;
        }

        /// <summary>
        /// Creates a DiscoveredDevice from port scan results
        /// </summary>
        private DiscoveredDevice CreateDeviceFromPortScan(IPAddress ipAddress, IEnumerable<PortScanResult> openPorts)
        {
            var portList = openPorts.ToList();
            var primaryPort = portList.OrderBy(p => PortScanConstants.GetPortPriority(p.Port)).First();

            var device = new DiscoveredDevice(ipAddress, primaryPort.Port)
            {
                UniqueId = ipAddress.ToString(),
                Name = ipAddress.ToString(),
                DeviceType = DetermineDeviceTypeFromPorts(portList),
                Description = $"Device with {portList.Count} open ports"
            };

            // Add all discovered ports
            device.Ports.AddRange(portList.Select(p => p.Port));

            device.DiscoveryMethods.Add(DiscoveryMethod.PortScan);
            device.DiscoveryData["PortScan_Results"] = portList;
            device.DiscoveryData["PortScan_OpenPortCount"] = portList.Count;

            // Add services based on open ports
            foreach (var portResult in portList)
            {
                var serviceName = portResult.Service ?? $"Port{portResult.Port}";
                device.Services[serviceName] = new DeviceService
                {
                    Name = serviceName,
                    Port = portResult.Port,
                    Protocol = portResult.Protocol,
                    Properties = new Dictionary<string, string>
                    {
                        ["Banner"] = portResult.Banner ?? "",
                        ["ScanResult"] = "Open"
                    }
                };

                // Add capabilities based on detected services
                if (!string.IsNullOrEmpty(portResult.Service))
                {
                    device.Capabilities.Add(portResult.Service);
                }
            }

            return device;
        }

        /// <summary>
        /// Determines device type based on open ports
        /// </summary>
        private DeviceType DetermineDeviceTypeFromPorts(List<PortScanResult> openPorts)
        {
            var ports = openPorts.Select(p => p.Port).ToHashSet();

            // Web cameras and devices
            if (ports.Contains(80) || ports.Contains(8080) || ports.Contains(8000))
            {
                if (ports.Contains(554) || ports.Contains(8554)) // RTSP
                    return DeviceType.Camera;

                if (ports.Contains(631)) // CUPS printing
                    return DeviceType.Printer;
            }

            // Network infrastructure
            if (ports.Contains(23) && ports.Contains(80)) // Telnet + HTTP
                return DeviceType.Router;

            // Printers
            if (ports.Contains(631) || ports.Contains(9100) || ports.Contains(515))
                return DeviceType.Printer;

            // SSH servers (likely Linux/Unix systems)
            if (ports.Contains(22))
                return DeviceType.Server;

            // SMB/CIFS (Windows systems)
            if (ports.Contains(139) || ports.Contains(445))
                return DeviceType.Workstation;

            // Database servers
            if (ports.Contains(3306) || ports.Contains(5432) || ports.Contains(1433))
                return DeviceType.Server;

            return DeviceType.Unknown;
        }

        /// <summary>
        /// Reports discovery progress
        /// </summary>
        private void ReportProgress(int current, int total, string target, string status)
        {
            ProgressChanged?.Invoke(this, new DiscoveryProgressEventArgs(ServiceName, current, total, target, status));
        }
    }
}