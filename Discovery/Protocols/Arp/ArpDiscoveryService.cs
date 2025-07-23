using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using wpfhikip.Discovery.Core;
using wpfhikip.Discovery.Models;

namespace wpfhikip.Discovery.Protocols.Arp
{
    /// <summary>
    /// ARP table discovery service - discovers devices by reading the system ARP table
    /// </summary>
    public class ArpDiscoveryService : INetworkDiscoveryService
    {
        public string ServiceName => "ARP";
        public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(5);

        public event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;
        public event EventHandler<DiscoveryProgressEventArgs>? ProgressChanged;

        /// <summary>
        /// Discovers devices by reading the ARP table
        /// </summary>
        public async Task<IEnumerable<DiscoveredDevice>> DiscoverDevicesAsync(CancellationToken cancellationToken = default)
        {
            var devices = new List<DiscoveredDevice>();

            try
            {
                ReportProgress(0, 100, "", "Reading ARP table...");

                var arpEntries = await GetArpTableAsync();
                var totalEntries = arpEntries.Count;
                var currentEntry = 0;

                foreach (var arpEntry in arpEntries)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    currentEntry++;
                    ReportProgress(currentEntry, totalEntries, arpEntry.IPAddress.ToString(), "Processing ARP entry");

                    var device = CreateDeviceFromArpEntry(arpEntry);
                    if (device != null)
                    {
                        devices.Add(device);
                        DeviceDiscovered?.Invoke(this, new DeviceDiscoveredEventArgs(device, ServiceName));
                    }
                }

                ReportProgress(totalEntries, totalEntries, "", $"ARP discovery completed - {devices.Count} devices found");
            }
            catch (Exception ex)
            {
                ReportProgress(0, 0, "", $"ARP discovery error: {ex.Message}");
            }

            return devices;
        }

        /// <summary>
        /// Discovers devices on a specific network segment
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
        /// Gets ARP table entries from the system
        /// </summary>
        private async Task<List<ArpEntry>> GetArpTableAsync()
        {
            var arpEntries = new List<ArpEntry>();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                arpEntries = await GetWindowsArpTableAsync();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                arpEntries = await GetLinuxArpTableAsync();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                arpEntries = await GetMacArpTableAsync();
            }

            return arpEntries;
        }

        /// <summary>
        /// Gets ARP table on Windows using arp command
        /// </summary>
        private async Task<List<ArpEntry>> GetWindowsArpTableAsync()
        {
            var arpEntries = new List<ArpEntry>();

            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = "-a",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    arpEntries = ParseWindowsArpOutput(output);
                }
            }
            catch
            {
                // Fall back to reading from registry or WMI if available
            }

            return arpEntries;
        }

        /// <summary>
        /// Gets ARP table on Linux
        /// </summary>
        private async Task<List<ArpEntry>> GetLinuxArpTableAsync()
        {
            var arpEntries = new List<ArpEntry>();

            try
            {
                // Try reading /proc/net/arp first
                if (System.IO.File.Exists("/proc/net/arp"))
                {
                    var arpContent = await System.IO.File.ReadAllTextAsync("/proc/net/arp");
                    arpEntries = ParseLinuxArpOutput(arpContent);
                }
                else
                {
                    // Fall back to arp command
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = "arp",
                        Arguments = "-a",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(processInfo);
                    if (process != null)
                    {
                        var output = await process.StandardOutput.ReadToEndAsync();
                        await process.WaitForExitAsync();

                        arpEntries = ParseLinuxArpCommandOutput(output);
                    }
                }
            }
            catch
            {
                // ARP table reading failed
            }

            return arpEntries;
        }

        /// <summary>
        /// Gets ARP table on macOS
        /// </summary>
        private async Task<List<ArpEntry>> GetMacArpTableAsync()
        {
            var arpEntries = new List<ArpEntry>();

            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = "-a",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(processInfo);
                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    arpEntries = ParseMacArpOutput(output);
                }
            }
            catch
            {
                // ARP table reading failed
            }

            return arpEntries;
        }

        /// <summary>
        /// Parses Windows arp -a output
        /// </summary>
        private List<ArpEntry> ParseWindowsArpOutput(string output)
        {
            var entries = new List<ArpEntry>();
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // Windows ARP output format: IP Address Physical Address Type
            var arpRegex = new Regex(@"^\s*(\d+\.\d+\.\d+\.\d+)\s+([0-9a-f]{2}-[0-9a-f]{2}-[0-9a-f]{2}-[0-9a-f]{2}-[0-9a-f]{2}-[0-9a-f]{2})\s+(\w+)", RegexOptions.IgnoreCase);

            foreach (var line in lines)
            {
                var match = arpRegex.Match(line);
                if (match.Success)
                {
                    if (IPAddress.TryParse(match.Groups[1].Value, out var ipAddress))
                    {
                        var macAddress = match.Groups[2].Value.Replace("-", ":").ToUpper();
                        var entryType = match.Groups[3].Value;

                        entries.Add(new ArpEntry
                        {
                            IPAddress = ipAddress,
                            MACAddress = macAddress,
                            Type = entryType,
                            IsStatic = entryType.Equals("static", StringComparison.OrdinalIgnoreCase)
                        });
                    }
                }
            }

            return entries;
        }

        /// <summary>
        /// Parses Linux /proc/net/arp output
        /// </summary>
        private List<ArpEntry> ParseLinuxArpOutput(string output)
        {
            var entries = new List<ArpEntry>();
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // Skip header line
            for (int i = 1; i < lines.Length; i++)
            {
                var fields = lines[i].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length >= 6)
                {
                    if (IPAddress.TryParse(fields[0], out var ipAddress))
                    {
                        var macAddress = fields[3];
                        if (!string.IsNullOrEmpty(macAddress) && macAddress != "00:00:00:00:00:00")
                        {
                            entries.Add(new ArpEntry
                            {
                                IPAddress = ipAddress,
                                MACAddress = macAddress.ToUpper(),
                                Type = "dynamic",
                                Interface = fields[5]
                            });
                        }
                    }
                }
            }

            return entries;
        }

        /// <summary>
        /// Parses Linux arp command output
        /// </summary>
        private List<ArpEntry> ParseLinuxArpCommandOutput(string output)
        {
            var entries = new List<ArpEntry>();
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // Format: hostname (ip) at mac [ether] on interface
            var arpRegex = new Regex(@"(\S+)\s+\((\d+\.\d+\.\d+\.\d+)\)\s+at\s+([0-9a-f]{2}:[0-9a-f]{2}:[0-9a-f]{2}:[0-9a-f]{2}:[0-9a-f]{2}:[0-9a-f]{2})", RegexOptions.IgnoreCase);

            foreach (var line in lines)
            {
                var match = arpRegex.Match(line);
                if (match.Success)
                {
                    if (IPAddress.TryParse(match.Groups[2].Value, out var ipAddress))
                    {
                        var hostname = match.Groups[1].Value;
                        var macAddress = match.Groups[3].Value.ToUpper();

                        entries.Add(new ArpEntry
                        {
                            IPAddress = ipAddress,
                            MACAddress = macAddress,
                            Hostname = hostname != ipAddress.ToString() ? hostname : null,
                            Type = "dynamic"
                        });
                    }
                }
            }

            return entries;
        }

        /// <summary>
        /// Parses macOS arp -a output
        /// </summary>
        private List<ArpEntry> ParseMacArpOutput(string output)
        {
            var entries = new List<ArpEntry>();
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // Format: hostname (ip) at mac on interface ifscope [ethernet]
            var arpRegex = new Regex(@"(\S+)\s+\((\d+\.\d+\.\d+\.\d+)\)\s+at\s+([0-9a-f]{2}:[0-9a-f]{2}:[0-9a-f]{2}:[0-9a-f]{2}:[0-9a-f]{2}:[0-9a-f]{2})", RegexOptions.IgnoreCase);

            foreach (var line in lines)
            {
                var match = arpRegex.Match(line);
                if (match.Success)
                {
                    if (IPAddress.TryParse(match.Groups[2].Value, out var ipAddress))
                    {
                        var hostname = match.Groups[1].Value;
                        var macAddress = match.Groups[3].Value.ToUpper();

                        entries.Add(new ArpEntry
                        {
                            IPAddress = ipAddress,
                            MACAddress = macAddress,
                            Hostname = hostname != ipAddress.ToString() ? hostname : null,
                            Type = "dynamic"
                        });
                    }
                }
            }

            return entries;
        }

        /// <summary>
        /// Creates a DiscoveredDevice from an ARP entry
        /// </summary>
        private DiscoveredDevice? CreateDeviceFromArpEntry(ArpEntry arpEntry)
        {
            if (arpEntry.IPAddress == null || string.IsNullOrEmpty(arpEntry.MACAddress))
                return null;

            // Skip invalid MAC addresses
            if (arpEntry.MACAddress == "00:00:00:00:00:00" || arpEntry.MACAddress == "FF:FF:FF:FF:FF:FF")
                return null;

            var device = new DiscoveredDevice(arpEntry.IPAddress)
            {
                UniqueId = arpEntry.MACAddress, // Use MAC as unique identifier
                MACAddress = arpEntry.MACAddress,
                Name = arpEntry.Hostname ?? arpEntry.IPAddress.ToString(),
                DeviceType = DetermineDeviceTypeFromMac(arpEntry.MACAddress),
                Description = "Device found in ARP table"
            };

            device.DiscoveryMethods.Add(DiscoveryMethod.ARP);
            device.DiscoveryData["ARP_Entry"] = arpEntry;
            device.DiscoveryData["ARP_Type"] = arpEntry.Type;
            device.DiscoveryData["ARP_Interface"] = arpEntry.Interface;

            // Try to determine manufacturer from MAC address
            var manufacturer = GetManufacturerFromMac(arpEntry.MACAddress);
            if (!string.IsNullOrEmpty(manufacturer))
            {
                device.Manufacturer = manufacturer;
            }

            return device;
        }

        /// <summary>
        /// Determines device type based on MAC address patterns
        /// </summary>
        private DeviceType DetermineDeviceTypeFromMac(string macAddress)
        {
            var mac = macAddress.Replace(":", "").Replace("-", "").ToUpper();

            // Known MAC prefixes for different device types
            var cameraVendors = new[] { "001788", "4C0BBE", "002481", "ACCC8E", "C4F79D" }; // Hikvision, Dahua, Axis, etc.
            var routerVendors = new[] { "001DD8", "0021FB", "001312", "002722" }; // Various router manufacturers

            var prefix = mac.Substring(0, 6);

            if (cameraVendors.Contains(prefix))
                return DeviceType.Camera;

            if (routerVendors.Contains(prefix))
                return DeviceType.Router;

            return DeviceType.Unknown;
        }

        /// <summary>
        /// Gets manufacturer name from MAC address OUI
        /// </summary>
        private string? GetManufacturerFromMac(string macAddress)
        {
            var mac = macAddress.Replace(":", "").Replace("-", "").ToUpper();
            var oui = mac.Substring(0, 6);

            // Common security camera manufacturers
            return oui switch
            {
                "001788" => "Hikvision",
                "4C0BBE" => "Dahua",
                "002481" => "Axis",
                "ACCC8E" => "Axis",
                "C4F79D" => "Dahua",
                "001DD8" => "Mikrotik",
                "0021FB" => "Ubiquiti",
                "24A43C" => "Ubiquiti",
                "F09FC2" => "Ubiquiti",
                "D4CA6D" => "Ubiquiti",
                _ => null
            };
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