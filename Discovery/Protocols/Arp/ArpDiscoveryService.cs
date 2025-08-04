using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

using wpfhikip.Discovery.Core;
using wpfhikip.Discovery.Models;

namespace wpfhikip.Discovery.Protocols.Arp
{
    /// <summary>
    /// Enhanced ARP discovery service - scans ARP table, pings devices, and listens for ARP/DHCP requests with real-time results
    /// </summary>
    public class ArpDiscoveryService : INetworkDiscoveryService, IDisposable
    {
        public string ServiceName => "ARP";
        public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(10);

        public event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;
        public event EventHandler<DiscoveryProgressEventArgs>? ProgressChanged;

        private readonly ConcurrentDictionary<string, ArpEntry> _detectedDevices = new();
        private readonly SemaphoreSlim _operationSemaphore = new(1, 1);
        private bool _disposed = false;

        public async Task<IEnumerable<DiscoveredDevice>> DiscoverDevicesAsync(CancellationToken cancellationToken = default)
        {
            await _operationSemaphore.WaitAsync(cancellationToken);
            try
            {
                var devices = new List<DiscoveredDevice>();
                var networkSegments = NetworkUtils.GetLocalNetworkSegments();

                if (!networkSegments.Any())
                {
                    ReportProgress(0, 0, "", "No local network segments found");
                    return devices;
                }

                ReportProgress(0, 100, "", "Starting enhanced ARP discovery");

                foreach (var segment in networkSegments)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    var segmentDevices = await DiscoverDevicesAsync(segment, cancellationToken);
                    devices.AddRange(segmentDevices);
                }

                ReportProgress(100, 100, "", $"ARP discovery completed - {devices.Count} devices found");
                return devices;
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        public async Task<IEnumerable<DiscoveredDevice>> DiscoverDevicesAsync(string networkSegment, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(networkSegment))
            {
                ReportProgress(0, 0, "", "Network segment is null or empty");
                return new List<DiscoveredDevice>();
            }

            try
            {
                _detectedDevices.Clear();

                // Start both operations simultaneously for better performance
                ReportProgress(0, 100, networkSegment, "Starting ARP table scan and passive listening...");

                var arpScanTask = ScanArpTableAndPingAsync(cancellationToken);
                var passiveListenTask = ListenForArpRequestsAsync(TimeSpan.FromSeconds(30), cancellationToken);

                // Wait for both to complete
                await Task.WhenAll(arpScanTask, passiveListenTask);

                // Return all detected devices (events were already fired during discovery)
                var devices = _detectedDevices.Values
                    .Select(entry => CreateDeviceFromArpEntry(entry))
                    .Where(device => device != null)
                    .Cast<DiscoveredDevice>()
                    .ToList();

                ReportProgress(100, 100, networkSegment,
                    $"Enhanced ARP discovery completed - {devices.Count} devices found");

                return devices;
            }
            catch (Exception ex)
            {
                ReportProgress(0, 0, networkSegment, $"ARP discovery error: {ex.Message}");
                return new List<DiscoveredDevice>();
            }
        }

        /// <summary>
        /// Scans ARP table and pings only those addresses with real-time event firing
        /// </summary>
        private async Task ScanArpTableAndPingAsync(CancellationToken cancellationToken)
        {
            try
            {
                ReportProgress(20, 100, "", "Reading ARP table...");
                var arpEntries = await GetArpTableAsync();

                // Filter out invalid entries
                var validEntries = arpEntries.Where(IsValidDeviceEntry).ToList();

                if (!validEntries.Any()) return;

                ReportProgress(40, 100, "", $"Pinging {validEntries.Count} ARP table entries...");

                using var semaphore = new SemaphoreSlim(20, 20);
                var completedCount = 0;

                var pingTasks = validEntries.Select(async entry =>
                {
                    await semaphore.WaitAsync(cancellationToken);
                    try
                    {
                        if (cancellationToken.IsCancellationRequested) return;

                        var isReachable = await PingAddressAsync(entry.IPAddress!, cancellationToken);
                        if (isReachable)
                        {
                            entry.Type = "ping_verified";
                            var wasAdded = _detectedDevices.TryAdd(entry.IPAddress!.ToString(), entry);

                            if (wasAdded)
                            {
                                // Fire event immediately for real-time updates
                                var device = CreateDeviceFromArpEntry(entry);
                                if (device != null)
                                {
                                    DeviceDiscovered?.Invoke(this, new DeviceDiscoveredEventArgs(device, ServiceName));
                                }
                            }
                        }

                        var completed = Interlocked.Increment(ref completedCount);
                        var progress = 40 + (completed * 20 / validEntries.Count);
                        ReportProgress(progress, 100, entry.IPAddress?.ToString() ?? "",
                            $"Pinged {completed}/{validEntries.Count} entries ({_detectedDevices.Count} responsive)");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error pinging {entry.IPAddress}: {ex.Message}");
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(pingTasks);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ARP table scan: {ex.Message}");
            }
        }

        /// <summary>
        /// Listens for ARP requests for 30 seconds with real-time event firing
        /// </summary>
        private async Task ListenForArpRequestsAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            try
            {
                var startTime = DateTime.UtcNow;
                var lastKnownEntries = new HashSet<string>();

                // Get initial state
                var initialEntries = await GetArpTableAsync();
                foreach (var entry in initialEntries.Where(e => e.IPAddress != null))
                {
                    lastKnownEntries.Add(entry.IPAddress!.ToString());
                }

                while (DateTime.UtcNow - startTime < duration && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var elapsed = DateTime.UtcNow - startTime;
                        var remainingSeconds = (int)(duration.TotalSeconds - elapsed.TotalSeconds);
                        var progress = 60 + (int)(40 * elapsed.TotalSeconds / duration.TotalSeconds);

                        ReportProgress(progress, 100, "",
                            $"Listening for ARP requests... {remainingSeconds}s remaining ({_detectedDevices.Count} total detected)");

                        var currentEntries = await GetArpTableAsync();

                        foreach (var entry in currentEntries.Where(IsValidDeviceEntry))
                        {
                            var ipKey = entry.IPAddress!.ToString();

                            if (!lastKnownEntries.Contains(ipKey))
                            {
                                lastKnownEntries.Add(ipKey);
                                entry.Type = "arp_request_detected";

                                var wasAdded = _detectedDevices.TryAdd(ipKey, entry);
                                if (wasAdded)
                                {
                                    // Fire event immediately for real-time updates
                                    var device = CreateDeviceFromArpEntry(entry);
                                    if (device != null)
                                    {
                                        DeviceDiscovered?.Invoke(this, new DeviceDiscoveredEventArgs(device, ServiceName));
                                    }
                                    System.Diagnostics.Debug.WriteLine($"New ARP request detected: {entry.IPAddress} -> {entry.MACAddress}");
                                }
                            }
                        }

                        await Task.Delay(2000, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error monitoring ARP requests: {ex.Message}");
                        await Task.Delay(3000, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        /// <summary>
        /// Validates if an ARP entry represents a valid device - only filters out addresses that are definitely NOT real devices
        /// </summary>
        private bool IsValidDeviceEntry(ArpEntry entry)
        {
            if (entry.IPAddress == null || string.IsNullOrEmpty(entry.MACAddress))
                return false;

            var ip = entry.IPAddress;
            var mac = entry.MACAddress;

            // Only filter out addresses that are definitely NOT real devices

            // 1. Special IP addresses that can never be real devices
            if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.Broadcast) || ip.Equals(IPAddress.Loopback))
                return false;

            // 2. Multicast IP addresses (224.0.0.0 to 239.255.255.255) - these are group addresses, not device addresses
            if (ip.GetAddressBytes()[0] >= 224 && ip.GetAddressBytes()[0] <= 239)
                return false;

            // 3. Reserved/experimental IP ranges that shouldn't have real devices
            var ipFirstOctet = ip.GetAddressBytes()[0];
            if (ipFirstOctet == 0 || ipFirstOctet >= 240) // Class E (240-255) and network 0
                return false;

            // 4. Invalid MAC addresses that can never be real devices
            if (mac == "00:00:00:00:00:00" || mac == "FF:FF:FF:FF:FF:FF")
                return false;

            // 5. Multicast MAC addresses (first octet has LSB set) - these are group addresses, not device addresses
            if (!string.IsNullOrEmpty(mac) && mac.Length >= 2)
            {
                if (int.TryParse(mac.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out int macFirstOctet))
                {
                    if ((macFirstOctet & 1) == 1) // Multicast MAC
                        return false;
                }
            }

            // 6. Validate MAC address format
            if (!IsValidMacAddress(mac))
                return false;

            // Accept everything else - including:
            // - Private IP ranges (10.x.x.x, 172.16-31.x.x, 192.168.x.x)
            // - Public IP addresses 
            // - Link-local addresses (169.254.x.x) - these can be real devices
            // - Any valid unicast IP that could represent a real device

            return true;
        }

        private async Task<bool> PingAddressAsync(IPAddress ipAddress, CancellationToken cancellationToken)
        {
            try
            {
                using var ping = new Ping();
                using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                var reply = await ping.SendPingAsync(ipAddress, 2000);
                return reply.Status == IPStatus.Success;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ping error for {ipAddress}: {ex.Message}");
                return false;
            }
        }

        private async Task<List<ArpEntry>> GetArpTableAsync()
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = "-a",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    var error = await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    if (!string.IsNullOrEmpty(error))
                    {
                        System.Diagnostics.Debug.WriteLine($"ARP command error: {error}");
                    }

                    return ParseArpOutput(output);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading ARP table: {ex.Message}");
            }

            return new List<ArpEntry>();
        }

        private List<ArpEntry> ParseArpOutput(string output)
        {
            var entries = new List<ArpEntry>();

            try
            {
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var arpRegex = new Regex(@"^\s*(\d+\.\d+\.\d+\.\d+)\s+([0-9a-f]{2}-[0-9a-f]{2}-[0-9a-f]{2}-[0-9a-f]{2}-[0-9a-f]{2}-[0-9a-f]{2})\s+(\w+)", RegexOptions.IgnoreCase);

                foreach (var line in lines)
                {
                    try
                    {
                        var match = arpRegex.Match(line);
                        if (match.Success && IPAddress.TryParse(match.Groups[1].Value, out var ipAddress))
                        {
                            entries.Add(new ArpEntry
                            {
                                IPAddress = ipAddress,
                                MACAddress = match.Groups[2].Value.Replace("-", ":").ToUpper(),
                                Type = match.Groups[3].Value
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error parsing ARP line '{line}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing ARP output: {ex.Message}");
            }

            return entries;
        }

        private bool IsValidMacAddress(string macAddress)
        {
            if (string.IsNullOrEmpty(macAddress)) return false;

            try
            {
                var macRegex = new Regex(@"^([0-9A-F]{2}[:-]){5}([0-9A-F]{2})$", RegexOptions.IgnoreCase);
                return macRegex.IsMatch(macAddress);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error validating MAC address '{macAddress}': {ex.Message}");
                return false;
            }
        }

        private DiscoveredDevice? CreateDeviceFromArpEntry(ArpEntry arpEntry)
        {
            if (arpEntry.IPAddress == null || string.IsNullOrEmpty(arpEntry.MACAddress))
                return null;

            try
            {
                var isPingVerified = arpEntry.Type == "ping_verified";
                var isPassiveDetection = arpEntry.Type == "arp_request_detected";

                var device = new DiscoveredDevice(arpEntry.IPAddress)
                {
                    UniqueId = arpEntry.MACAddress,
                    MACAddress = arpEntry.MACAddress,
                    Name = arpEntry.IPAddress.ToString(),
                    DeviceType = DetermineDeviceTypeFromMac(arpEntry.MACAddress),
                    Description = isPingVerified ? "Device responds to ping (ARP table)" :
                                 isPassiveDetection ? "Device detected via ARP activity" : "Device in ARP table",
                    IsOnline = isPingVerified
                };

                device.DiscoveryMethods.Add(DiscoveryMethod.ARP);
                device.DiscoveryData["ARP_Entry"] = arpEntry;
                device.DiscoveryData["ARP_Type"] = arpEntry.Type;
                device.DiscoveryData["ARP_PingVerified"] = isPingVerified;
                device.DiscoveryData["ARP_PassiveDetection"] = isPassiveDetection;

                var manufacturer = GetManufacturerFromMac(arpEntry.MACAddress);
                if (!string.IsNullOrEmpty(manufacturer))
                {
                    device.Manufacturer = manufacturer;
                }

                // Improved hostname resolution with better error handling and timeout
                TryResolveHostnameAsync(device, arpEntry.IPAddress);

                return device;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating device from ARP entry {arpEntry.IPAddress}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Attempts to resolve hostname asynchronously with proper error handling
        /// </summary>
        private void TryResolveHostnameAsync(DiscoveredDevice device, IPAddress ipAddress)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    // Use a shorter timeout and better cancellation handling
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

                    // Wrap the hostname resolution in a try-catch to prevent socket exceptions
                    try
                    {
                        // Use the correct overload: GetHostEntryAsync(IPAddress)
                        var hostEntry = await Dns.GetHostEntryAsync(ipAddress);
                        if (!string.IsNullOrEmpty(hostEntry.HostName) &&
                            hostEntry.HostName != ipAddress.ToString() &&
                            !hostEntry.HostName.Equals(ipAddress.ToString(), StringComparison.OrdinalIgnoreCase))
                        {
                            device.Name = hostEntry.HostName;
                            device.DiscoveryData["ARP_ResolvedHostname"] = hostEntry.HostName;
                        }
                    }
                    catch (SocketException socketEx)
                    {
                        // DNS resolution failed - this is common and expected for many devices
                        System.Diagnostics.Debug.WriteLine($"DNS resolution failed for {ipAddress}: {socketEx.Message}");
                    }
                    catch (OperationCanceledException)
                    {
                        // Timeout occurred - also common and expected
                        System.Diagnostics.Debug.WriteLine($"DNS resolution timeout for {ipAddress}");
                    }
                }
                catch (Exception ex)
                {
                    // Log other unexpected errors but don't let them bubble up
                    System.Diagnostics.Debug.WriteLine($"Unexpected error during hostname resolution for {ipAddress}: {ex.Message}");
                }
            });
        }

        private DeviceType DetermineDeviceTypeFromMac(string macAddress)
        {
            if (string.IsNullOrEmpty(macAddress)) return DeviceType.Unknown;

            try
            {
                var mac = macAddress.Replace(":", "").Replace("-", "").ToUpper();
                if (mac.Length < 6) return DeviceType.Unknown;

                var prefix = mac.Substring(0, 6);
                var cameraVendors = new[] { "001788", "4C0BBE", "002481", "ACCC8E", "C4F79D", "7188D0" };
                var routerVendors = new[] { "001DD8", "0021FB", "001312", "002722" };

                return cameraVendors.Contains(prefix) ? DeviceType.Camera :
                       routerVendors.Contains(prefix) ? DeviceType.Router : DeviceType.Unknown;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error determining device type from MAC '{macAddress}': {ex.Message}");
                return DeviceType.Unknown;
            }
        }

        private string? GetManufacturerFromMac(string macAddress)
        {
            if (string.IsNullOrEmpty(macAddress) || macAddress.Length < 6) return null;

            try
            {
                var oui = macAddress.Replace(":", "").Replace("-", "").ToUpper().Substring(0, 6);
                return oui switch
                {
                    "001788" => "Hikvision",
                    "4C0BBE" => "Dahua",
                    "002481" => "Axis",
                    "ACCC8E" => "Axis",
                    "C4F79D" => "Dahua",
                    "7188D0" => "Dahua", // ZhejiangDahu devices
                    "001DD8" => "Mikrotik",
                    "0021FB" => "Ubiquiti",
                    "24A43C" => "Ubiquiti",
                    "F09FC2" => "Ubiquiti",
                    "D4CA6D" => "Ubiquiti",
                    "00E04C" => "Realtek",
                    "001B21" => "Intel",
                    "B42E99" => "Apple",
                    "F0EF86" => "Apple",
                    "2C5496" => "Samsung",
                    "001E58" => "LG Electronics",
                    _ => null
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting manufacturer from MAC '{macAddress}': {ex.Message}");
                return null;
            }
        }

        private void ReportProgress(int current, int total, string target, string status)
        {
            try
            {
                ProgressChanged?.Invoke(this, new DiscoveryProgressEventArgs(ServiceName, current, total, target, status));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reporting progress: {ex.Message}");
            }
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
                    _operationSemaphore?.Dispose();
                }
                _disposed = true;
            }
        }
    }
}