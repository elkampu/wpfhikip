using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

using wpfhikip.Discovery.Core;
using wpfhikip.Discovery.Models;

namespace wpfhikip.Discovery.Protocols.Icmp
{
    /// <summary>
    /// ICMP ping sweep discovery service
    /// </summary>
    public class IcmpDiscoveryService : INetworkDiscoveryService
    {
        public string ServiceName => "ICMP";
        public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(5);

        public event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;
        public event EventHandler<DiscoveryProgressEventArgs>? ProgressChanged;

        /// <summary>
        /// Discovers devices by pinging all local network segments
        /// </summary>
        public async Task<IEnumerable<DiscoveredDevice>> DiscoverDevicesAsync(CancellationToken cancellationToken = default)
        {
            var devices = new List<DiscoveredDevice>();

            try
            {
                // Get all local network segments
                var networkSegments = NetworkUtils.GetLocalNetworkSegments();

                ReportProgress(0, networkSegments.Count, "", "Starting ICMP discovery on local segments");

                foreach (var segment in networkSegments)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var segmentDevices = await DiscoverDevicesAsync(segment, cancellationToken);
                    devices.AddRange(segmentDevices);
                }

                ReportProgress(networkSegments.Count, networkSegments.Count, "", $"ICMP discovery completed - {devices.Count} devices found");
            }
            catch (Exception ex)
            {
                ReportProgress(0, 0, "", $"ICMP discovery error: {ex.Message}");
            }

            return devices;
        }

        /// <summary>
        /// Discovers devices on a specific network segment using ICMP ping
        /// </summary>
        public async Task<IEnumerable<DiscoveredDevice>> DiscoverDevicesAsync(string networkSegment, CancellationToken cancellationToken = default)
        {
            var devices = new List<DiscoveredDevice>();

            if (string.IsNullOrEmpty(networkSegment))
                return devices;

            try
            {
                // Get all IP addresses in the segment
                var ipAddresses = NetworkUtils.GetIPAddressesInSegment(networkSegment);
                if (!ipAddresses.Any())
                {
                    ReportProgress(0, 0, networkSegment, "No IP addresses in segment");
                    return devices;
                }

                ReportProgress(0, ipAddresses.Count, networkSegment, $"Pinging {ipAddresses.Count} addresses in {networkSegment}");

                // Use semaphore to limit concurrent pings
                using var semaphore = new SemaphoreSlim(50); // Limit to 50 concurrent pings
                var pingTasks = ipAddresses.Select(async (ip, index) =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        if (cancellationToken.IsCancellationRequested)
                            return;

                        ReportProgress(index + 1, ipAddresses.Count, ip.ToString(), "Pinging...");

                        var device = await PingAddressAsync(ip, cancellationToken);
                        if (device != null)
                        {
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

                await Task.WhenAll(pingTasks);

                ReportProgress(ipAddresses.Count, ipAddresses.Count, networkSegment,
                    $"Segment {networkSegment} completed - {devices.Count} devices respond to ping");
            }
            catch (Exception ex)
            {
                ReportProgress(0, 0, networkSegment, $"Error scanning {networkSegment}: {ex.Message}");
            }

            return devices;
        }

        /// <summary>
        /// Pings a specific IP address and creates a device if responsive
        /// </summary>
        private async Task<DiscoveredDevice?> PingAddressAsync(IPAddress ipAddress, CancellationToken cancellationToken)
        {
            try
            {
                using var ping = new Ping();
                var timeout = (int)TimeSpan.FromSeconds(2).TotalMilliseconds;

                var reply = await ping.SendPingAsync(ipAddress, timeout);

                if (reply.Status == IPStatus.Success)
                {
                    var device = new DiscoveredDevice(ipAddress)
                    {
                        UniqueId = ipAddress.ToString(),
                        Name = ipAddress.ToString(),
                        DeviceType = DeviceType.Unknown,
                        Description = "Device responds to ICMP ping",
                        IsOnline = true
                    };

                    device.DiscoveryMethods.Add(DiscoveryMethod.ICMP);
                    device.DiscoveryData["ICMP_Reply"] = reply;
                    device.DiscoveryData["ICMP_RoundtripTime"] = reply.RoundtripTime;
                    device.DiscoveryData["ICMP_Status"] = reply.Status.ToString();

                    // Try to resolve hostname
                    try
                    {
                        var hostname = await NetworkUtils.GetHostnameAsync(ipAddress);
                        if (!string.IsNullOrEmpty(hostname) && hostname != ipAddress.ToString())
                        {
                            device.Name = hostname;
                            device.DiscoveryData["ICMP_Hostname"] = hostname;
                        }
                    }
                    catch
                    {
                        // Hostname resolution failed - not critical
                    }

                    return device;
                }
            }
            catch (PingException)
            {
                // Ping failed - device not responsive or blocked
            }
            catch (Exception)
            {
                // Other error - ignore
            }

            return null;
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