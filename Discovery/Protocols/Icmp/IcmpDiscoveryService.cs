using System.Net;
using System.Net.NetworkInformation;

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

                if (!networkSegments.Any())
                {
                    ReportProgress(0, 0, "", "No local network segments found");
                    return devices;
                }

                ReportProgress(0, networkSegments.Count, "", "Starting ICMP discovery on local segments");

                var segmentIndex = 0;
                foreach (var segment in networkSegments)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    segmentIndex++;
                    ReportProgress(segmentIndex, networkSegments.Count, segment, $"Scanning segment {segment}");

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
            {
                ReportProgress(0, 0, "", "Network segment is null or empty");
                return devices;
            }

            try
            {
                // Get all IP addresses in the segment
                var ipAddresses = NetworkUtils.GetIPAddressesInSegment(networkSegment);
                if (!ipAddresses.Any())
                {
                    ReportProgress(0, 0, networkSegment, "No IP addresses in segment");
                    return devices;
                }

                // Limit the number of addresses to scan for performance
                var maxAddresses = 254; // Reasonable limit for /24 networks
                if (ipAddresses.Count > maxAddresses)
                {
                    ReportProgress(0, 0, networkSegment, $"Limiting scan to first {maxAddresses} addresses (of {ipAddresses.Count} total)");
                    ipAddresses = ipAddresses.Take(maxAddresses).ToList();
                }

                ReportProgress(0, ipAddresses.Count, networkSegment, $"Pinging {ipAddresses.Count} addresses in {networkSegment}");

                // Use semaphore to limit concurrent pings to avoid overwhelming the network
                using var semaphore = new SemaphoreSlim(25); // Reduced from 50 for better stability
                var completedCount = 0;

                var pingTasks = ipAddresses.Select(async (ip, index) =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        if (cancellationToken.IsCancellationRequested)
                            return;

                        var device = await PingAddressAsync(ip, cancellationToken);
                        if (device != null)
                        {
                            lock (devices)
                            {
                                devices.Add(device);
                            }
                            DeviceDiscovered?.Invoke(this, new DeviceDiscoveredEventArgs(device, ServiceName));
                        }

                        // Update progress
                        var completed = Interlocked.Increment(ref completedCount);
                        if (completed % 10 == 0 || completed == ipAddresses.Count) // Report every 10 addresses or at the end
                        {
                            ReportProgress(completed, ipAddresses.Count, ip.ToString(), $"Completed {completed}/{ipAddresses.Count}");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log individual ping errors but don't stop the scan
                        var completed = Interlocked.Increment(ref completedCount);
                        ReportProgress(completed, ipAddresses.Count, ip.ToString(), $"Error pinging {ip}: {ex.Message}");
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
                var timeout = 3000; // 3 second timeout - reasonable for local networks

                // Use the async version with proper cancellation handling
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeout));
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

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

                    // Try to resolve hostname (but don't wait too long)
                    try
                    {
                        using var hostnameCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        using var hostnameTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, hostnameCts.Token);

                        var hostname = await NetworkUtils.GetHostnameAsync(ipAddress);
                        if (!string.IsNullOrEmpty(hostname) && hostname != ipAddress.ToString())
                        {
                            device.Name = hostname;
                            device.DiscoveryData["ICMP_Hostname"] = hostname;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Hostname resolution timed out or was cancelled - not critical
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
                // Ping failed - device not responsive or ICMP blocked
            }
            catch (OperationCanceledException)
            {
                // Operation was cancelled - normal during shutdown
            }
            catch (Exception)
            {
                // Other error - ignore individual ping failures
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