using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

using wpfhikip.Discovery.Core;
using wpfhikip.Discovery.Models;

namespace wpfhikip.Discovery.Protocols.Dhcp
{
    /// <summary>
    /// DHCP lease discovery service - discovers devices from DHCP server logs/leases
    /// </summary>
    public class DhcpDiscoveryService : INetworkDiscoveryService
    {
        public string ServiceName => "DHCP Leases";
        public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(15);

        public event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;
        public event EventHandler<DiscoveryProgressEventArgs>? ProgressChanged;

        /// <summary>
        /// Discovers devices from DHCP lease information
        /// </summary>
        public async Task<IEnumerable<DiscoveredDevice>> DiscoverDevicesAsync(CancellationToken cancellationToken = default)
        {
            var devices = new List<DiscoveredDevice>();

            try
            {
                ReportProgress(0, 3, "", "Starting DHCP lease discovery");

                // Method 1: Check local DHCP client cache
                var clientDevices = await DiscoverFromDhcpClientAsync(cancellationToken);
                devices.AddRange(clientDevices);

                ReportProgress(1, 3, "", $"Found {clientDevices.Count} devices from DHCP client");

                // Method 2: Scan for DHCP servers and try to get lease info
                var serverDevices = await DiscoverFromDhcpServersAsync(cancellationToken);

                // Merge with existing devices
                foreach (var device in serverDevices)
                {
                    var existing = devices.FirstOrDefault(d => d.UniqueId == device.UniqueId);
                    if (existing != null)
                    {
                        existing.UpdateFrom(device);
                    }
                    else
                    {
                        devices.Add(device);
                    }
                }

                ReportProgress(2, 3, "", $"Found {serverDevices.Count} devices from DHCP servers");

                // Method 3: Parse system network configuration for known devices
                var configDevices = await DiscoverFromNetworkConfigAsync(cancellationToken);

                foreach (var device in configDevices)
                {
                    var existing = devices.FirstOrDefault(d => d.UniqueId == device.UniqueId);
                    if (existing != null)
                    {
                        existing.UpdateFrom(device);
                    }
                    else
                    {
                        devices.Add(device);
                    }
                }

                ReportProgress(3, 3, "", $"DHCP discovery completed - {devices.Count} total devices");
            }
            catch (Exception ex)
            {
                ReportProgress(0, 0, "", $"DHCP discovery error: {ex.Message}");
            }

            return devices;
        }

        /// <summary>
        /// Discovers devices on a specific network segment from DHCP
        /// </summary>
        public async Task<IEnumerable<DiscoveredDevice>> DiscoverDevicesAsync(string networkSegment, CancellationToken cancellationToken = default)
        {
            var allDevices = await DiscoverDevicesAsync(cancellationToken);

            if (string.IsNullOrEmpty(networkSegment))
                return allDevices;

            return allDevices.Where(device =>
                device.IPAddress != null &&
                NetworkUtils.IsIPInSegment(device.IPAddress, networkSegment));
        }

        /// <summary>
        /// Discovers devices from local DHCP client information
        /// </summary>
        private async Task<List<DiscoveredDevice>> DiscoverFromDhcpClientAsync(CancellationToken cancellationToken)
        {
            var devices = new List<DiscoveredDevice>();

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    devices.AddRange(await DiscoverWindowsDhcpClientAsync(cancellationToken));
                }
                else if (OperatingSystem.IsLinux())
                {
                    devices.AddRange(await DiscoverLinuxDhcpClientAsync(cancellationToken));
                }
            }
            catch (Exception ex)
            {
                ReportProgress(0, 0, "", $"DHCP client discovery error: {ex.Message}");
            }

            return devices;
        }

        /// <summary>
        /// Discovers DHCP information on Windows
        /// </summary>
        private async Task<List<DiscoveredDevice>> DiscoverWindowsDhcpClientAsync(CancellationToken cancellationToken)
        {
            var devices = new List<DiscoveredDevice>();

            try
            {
                // Use ipconfig /all to get DHCP server information
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "ipconfig",
                        Arguments = "/all",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync(cancellationToken);

                // Parse DHCP server addresses from ipconfig output
                var dhcpServerPattern = @"DHCP Server[.\s]*:\s*(\d+\.\d+\.\d+\.\d+)";
                var matches = Regex.Matches(output, dhcpServerPattern, RegexOptions.IgnoreCase);

                foreach (Match match in matches)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    if (IPAddress.TryParse(match.Groups[1].Value, out var dhcpServerIP))
                    {
                        var device = new DiscoveredDevice(dhcpServerIP)
                        {
                            UniqueId = dhcpServerIP.ToString(),
                            Name = $"DHCP Server ({dhcpServerIP})",
                            DeviceType = DeviceType.Router, // DHCP servers are often routers
                            Description = "DHCP Server discovered from client configuration"
                        };

                        device.DiscoveryMethods.Add(DiscoveryMethod.DHCP);
                        device.DiscoveryData["DHCP_Role"] = "Server";
                        device.Capabilities.Add("DHCP Server");

                        devices.Add(device);
                        DeviceDiscovered?.Invoke(this, new DeviceDiscoveredEventArgs(device, ServiceName));
                    }
                }

                // Also parse gateway information
                var gatewayPattern = @"Default Gateway[.\s]*:\s*(\d+\.\d+\.\d+\.\d+)";
                var gatewayMatches = Regex.Matches(output, gatewayPattern, RegexOptions.IgnoreCase);

                foreach (Match match in gatewayMatches)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    if (IPAddress.TryParse(match.Groups[1].Value, out var gatewayIP))
                    {
                        var existing = devices.FirstOrDefault(d => d.IPAddress?.Equals(gatewayIP) == true);
                        if (existing == null)
                        {
                            var device = new DiscoveredDevice(gatewayIP)
                            {
                                UniqueId = gatewayIP.ToString(),
                                Name = $"Gateway ({gatewayIP})",
                                DeviceType = DeviceType.Gateway,
                                Description = "Default Gateway from DHCP configuration"
                            };

                            device.DiscoveryMethods.Add(DiscoveryMethod.DHCP);
                            device.DiscoveryData["DHCP_Role"] = "Gateway";
                            device.Capabilities.Add("Gateway");

                            devices.Add(device);
                            DeviceDiscovered?.Invoke(this, new DeviceDiscoveredEventArgs(device, ServiceName));
                        }
                        else
                        {
                            existing.Capabilities.Add("Gateway");
                            existing.DiscoveryData["DHCP_Gateway"] = "true";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ReportProgress(0, 0, "", $"Windows DHCP client discovery error: {ex.Message}");
            }

            return devices;
        }

        /// <summary>
        /// Discovers DHCP information on Linux
        /// </summary>
        private async Task<List<DiscoveredDevice>> DiscoverLinuxDhcpClientAsync(CancellationToken cancellationToken)
        {
            var devices = new List<DiscoveredDevice>();

            try
            {
                // Check DHCP lease files
                var leaseFiles = new[]
                {
                    "/var/lib/dhcp/dhclient.leases",
                    "/var/lib/dhclient/dhclient.leases",
                    "/var/lib/NetworkManager/*.lease"
                };

                foreach (var leaseFile in leaseFiles)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    try
                    {
                        if (File.Exists(leaseFile))
                        {
                            var content = await File.ReadAllTextAsync(leaseFile, cancellationToken);
                            devices.AddRange(ParseDhcpLeaseFile(content));
                        }
                    }
                    catch
                    {
                        // File read error - continue with next file
                    }
                }
            }
            catch (Exception ex)
            {
                ReportProgress(0, 0, "", $"Linux DHCP client discovery error: {ex.Message}");
            }

            return devices;
        }

        /// <summary>
        /// Parses DHCP lease file content
        /// </summary>
        private List<DiscoveredDevice> ParseDhcpLeaseFile(string content)
        {
            var devices = new List<DiscoveredDevice>();

            try
            {
                // Parse dhcp-server-identifier
                var serverPattern = @"dhcp-server-identifier\s+(\d+\.\d+\.\d+\.\d+)";
                var serverMatches = Regex.Matches(content, serverPattern);

                foreach (Match match in serverMatches)
                {
                    if (IPAddress.TryParse(match.Groups[1].Value, out var serverIP))
                    {
                        var existing = devices.FirstOrDefault(d => d.IPAddress?.Equals(serverIP) == true);
                        if (existing == null)
                        {
                            var device = new DiscoveredDevice(serverIP)
                            {
                                UniqueId = serverIP.ToString(),
                                Name = $"DHCP Server ({serverIP})",
                                DeviceType = DeviceType.Router,
                                Description = "DHCP Server from lease file"
                            };

                            device.DiscoveryMethods.Add(DiscoveryMethod.DHCP);
                            device.DiscoveryData["DHCP_Role"] = "Server";
                            device.Capabilities.Add("DHCP Server");

                            devices.Add(device);
                        }
                    }
                }

                // Parse routers (gateways)
                var routerPattern = @"option routers\s+(\d+\.\d+\.\d+\.\d+)";
                var routerMatches = Regex.Matches(content, routerPattern);

                foreach (Match match in routerMatches)
                {
                    if (IPAddress.TryParse(match.Groups[1].Value, out var routerIP))
                    {
                        var existing = devices.FirstOrDefault(d => d.IPAddress?.Equals(routerIP) == true);
                        if (existing == null)
                        {
                            var device = new DiscoveredDevice(routerIP)
                            {
                                UniqueId = routerIP.ToString(),
                                Name = $"Gateway ({routerIP})",
                                DeviceType = DeviceType.Gateway,
                                Description = "Gateway from DHCP lease"
                            };

                            device.DiscoveryMethods.Add(DiscoveryMethod.DHCP);
                            device.DiscoveryData["DHCP_Role"] = "Gateway";
                            device.Capabilities.Add("Gateway");

                            devices.Add(device);
                        }
                        else
                        {
                            existing.Capabilities.Add("Gateway");
                        }
                    }
                }
            }
            catch
            {
                // Parsing error
            }

            return devices;
        }

        /// <summary>
        /// Discovers devices from DHCP servers (if accessible)
        /// </summary>
        private async Task<List<DiscoveredDevice>> DiscoverFromDhcpServersAsync(CancellationToken cancellationToken)
        {
            var devices = new List<DiscoveredDevice>();

            // This would require implementing DHCP server communication
            // which is complex and often requires admin privileges
            // For now, return empty list but structure is here for future implementation

            await Task.Delay(1, cancellationToken); // Placeholder

            return devices;
        }

        /// <summary>
        /// Discovers devices from network configuration files
        /// </summary>
        private async Task<List<DiscoveredDevice>> DiscoverFromNetworkConfigAsync(CancellationToken cancellationToken)
        {
            var devices = new List<DiscoveredDevice>();

            // This could parse various network configuration files
            // like /etc/hosts, network manager configs, etc.

            await Task.Delay(1, cancellationToken); // Placeholder

            return devices;
        }

        private void ReportProgress(int current, int total, string target, string status)
        {
            ProgressChanged?.Invoke(this, new DiscoveryProgressEventArgs(ServiceName, current, total, target, status));
        }
    }
}