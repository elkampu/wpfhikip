using System.Net;
using System.Text;

using wpfhikip.Discovery.Core;
using wpfhikip.Discovery.Models;

namespace wpfhikip.Discovery.Protocols.Mdns
{
    /// <summary>
    /// Processes mDNS responses and creates discovered devices
    /// </summary>
    internal class MdnsResponseProcessor
    {
        public event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;

        public List<DiscoveredDevice> ProcessRecords(List<MdnsRecord> records, IPEndPoint source, string? networkSegment)
        {
            var devices = new List<DiscoveredDevice>();
            var deviceMap = new Dictionary<string, DiscoveredDevice>();

            try
            {
                System.Diagnostics.Debug.WriteLine($"mDNS: Processing {records.Count} records from {source.Address}");

                // Skip processing if this is from a local IP address
                if (NetworkUtils.IsLocalIPAddress(source.Address))
                {
                    System.Diagnostics.Debug.WriteLine($"mDNS: Skipping records from local IP {source.Address}");
                    return devices;
                }

                // Process A records first to establish IP mappings
                var aRecords = records.Where(r => r.Type == MdnsRecordType.A).ToList();
                foreach (var aRecord in aRecords)
                {
                    if (IPAddress.TryParse(aRecord.Data, out var ipAddress))
                    {
                        // Skip if this A record points to a local IP
                        if (NetworkUtils.IsLocalIPAddress(ipAddress))
                        {
                            System.Diagnostics.Debug.WriteLine($"mDNS: Skipping A record for local IP {ipAddress}");
                            continue;
                        }

                        var device = GetOrCreateDevice(deviceMap, ipAddress.ToString(), ipAddress, networkSegment);
                        if (!string.IsNullOrEmpty(aRecord.Name))
                        {
                            device.Name = ExtractHostname(aRecord.Name);
                        }
                        System.Diagnostics.Debug.WriteLine($"mDNS: Found A record for {ipAddress} -> {aRecord.Name}");
                    }
                }

                // ... rest of the processing methods remain the same ...
                // [Keep all the existing processing logic for PTR, SRV, TXT records]

                devices.AddRange(deviceMap.Values);

                // If no devices found but we got a response, create a basic device
                if (!devices.Any() && source != null)
                {
                    System.Diagnostics.Debug.WriteLine($"mDNS: Creating basic device for {source.Address}");
                    var basicDevice = CreateBasicDevice(source.Address, networkSegment);
                    devices.Add(basicDevice);
                }

                foreach (var device in devices)
                {
                    System.Diagnostics.Debug.WriteLine($"mDNS: Discovered device: {device.IPAddress} ({device.Name}) - {device.DeviceType}");
                    DeviceDiscovered?.Invoke(this, new DeviceDiscoveredEventArgs(device, "mDNS"));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing mDNS records: {ex.Message}");
            }

            return devices;
        }

        public DiscoveredDevice? ProcessQuestions(List<MdnsRecord> questions, IPEndPoint source, string? networkSegment)
        {
            try
            {
                // Skip processing questions from local IP addresses to prevent self-discovery
                if (NetworkUtils.IsLocalIPAddress(source.Address))
                {
                    System.Diagnostics.Debug.WriteLine($"mDNS: Skipping questions from local IP {source.Address}");
                    return null;
                }

                // ... rest of the method remains the same ...
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing mDNS questions: {ex.Message}");
            }

            return null;
        }

        private void ProcessPtrRecord(MdnsRecord ptrRecord, IPEndPoint source, Dictionary<string, DiscoveredDevice> deviceMap, string? networkSegment)
        {
            try
            {
                var serviceName = ptrRecord.Name;
                var instanceName = ptrRecord.Data;

                if (string.IsNullOrEmpty(instanceName)) return;

                // Extract device info from service name
                var deviceType = DetermineDeviceTypeFromService(serviceName);

                // Use source IP address for the device
                var deviceIP = source.Address;
                var device = GetOrCreateDevice(deviceMap, deviceIP.ToString(), deviceIP, networkSegment);

                // Update device type if it's more specific
                if (device.DeviceType == DeviceType.Unknown || device.DeviceType == DeviceType.NetworkDevice)
                {
                    device.DeviceType = deviceType;
                }

                // Add service capability
                device.Capabilities.Add($"Service: {serviceName}");

                // Try to extract device name from instance
                if (!string.IsNullOrEmpty(instanceName))
                {
                    var extractedName = ExtractDeviceNameFromInstance(instanceName);
                    if (!string.IsNullOrEmpty(extractedName) && string.IsNullOrEmpty(device.Name))
                    {
                        device.Name = extractedName;
                    }

                    // Extract manufacturer from instance name (e.g., "HIKVISION DS-2CD2523G0-IS")
                    if (instanceName.ToUpper().Contains("HIKVISION"))
                    {
                        device.Manufacturer = "Hikvision";
                        device.DeviceType = DeviceType.Camera;

                        // Extract model from the instance name
                        var parts = instanceName.Split(' ', '-');
                        foreach (var part in parts)
                        {
                            if (part.StartsWith("DS-") || part.StartsWith("HIK"))
                            {
                                device.Model = part;
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing PTR record: {ex.Message}");
            }
        }

        private void ProcessSrvRecord(MdnsRecord srvRecord, IPEndPoint source, Dictionary<string, DiscoveredDevice> deviceMap, string? networkSegment)
        {
            try
            {
                if (string.IsNullOrEmpty(srvRecord.Data)) return;

                // Parse SRV data: priority,weight,port,target
                var parts = srvRecord.Data.Split(',');
                if (parts.Length >= 4 && int.TryParse(parts[2], out int port))
                {
                    var target = parts[3];

                    // Use source IP if target is not an IP
                    var deviceIP = source.Address;

                    var device = GetOrCreateDevice(deviceMap, deviceIP.ToString(), deviceIP, networkSegment);

                    // Set the primary port if not already set
                    if (device.Port == 0)
                    {
                        device.Port = port;
                    }

                    device.Ports.Add(port);

                    // Add service port info
                    device.Capabilities.Add($"Port: {port}");

                    if (!string.IsNullOrEmpty(target) && target != ".")
                    {
                        device.Capabilities.Add($"Target: {target}");

                        // If target contains hostname info, extract it
                        if (string.IsNullOrEmpty(device.Name) && target.Contains(".local"))
                        {
                            device.Name = ExtractHostname(target);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing SRV record: {ex.Message}");
            }
        }

        private void ProcessTxtRecord(MdnsRecord txtRecord, Dictionary<string, DiscoveredDevice> deviceMap)
        {
            try
            {
                if (string.IsNullOrEmpty(txtRecord.Data)) return;

                // TXT records contain key=value pairs separated by semicolons
                var txtData = txtRecord.Data.Split(';', StringSplitOptions.RemoveEmptyEntries);

                // Try to match TXT record to a device (simplified approach)
                var device = deviceMap.Values.FirstOrDefault();
                if (device != null)
                {
                    foreach (var kvp in txtData.Take(10)) // Limit to avoid overflow
                    {
                        if (kvp.Contains('='))
                        {
                            var parts = kvp.Split('=', 2);
                            var key = parts[0].Trim();
                            var value = parts.Length > 1 ? parts[1].Trim() : "";

                            // Extract useful device information
                            switch (key.ToLower())
                            {
                                case "model":
                                case "md":
                                    device.Model = value;
                                    break;
                                case "manufacturer":
                                case "mf":
                                    device.Manufacturer = value;
                                    break;
                                case "version":
                                case "ver":
                                    device.FirmwareVersion = value;
                                    break;
                                case "name":
                                case "fn":
                                    if (string.IsNullOrEmpty(device.Name))
                                        device.Name = value;
                                    break;
                                default:
                                    device.Capabilities.Add($"{key}={value}");
                                    break;
                            }
                        }
                        else
                        {
                            device.Capabilities.Add(kvp);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing TXT record: {ex.Message}");
            }
        }

        private DiscoveredDevice GetOrCreateDevice(Dictionary<string, DiscoveredDevice> deviceMap, string key, IPAddress ipAddress, string? networkSegment)
        {
            if (!deviceMap.TryGetValue(key, out var device))
            {
                device = CreateBasicDevice(ipAddress, networkSegment);
                deviceMap[key] = device;
            }
            return device;
        }

        private DiscoveredDevice CreateBasicDevice(IPAddress ipAddress, string? networkSegment)
        {
            var device = new DiscoveredDevice(ipAddress)
            {
                // Note: NetworkSegment is computed from IPAddress, no need to set it manually
                LastSeen = DateTime.UtcNow,
                IsOnline = true
            };

            device.DiscoveryMethods.Add(DiscoveryMethod.mDNS);
            return device;
        }

        private DeviceType DetermineDeviceTypeFromService(string serviceName)
        {
            var service = serviceName.ToLower();

            return service switch
            {
                var s when s.Contains("camera") || s.Contains("onvif") || s.Contains("rtsp") || s.Contains("psia") || s.Contains("cgi") => DeviceType.Camera,
                var s when s.Contains("nvr") || s.Contains("dvr") => DeviceType.NVR,
                var s when s.Contains("printer") || s.Contains("ipp") => DeviceType.Printer,
                var s when s.Contains("airplay") || s.Contains("raop") => DeviceType.MediaServer,
                var s when s.Contains("router") => DeviceType.Router,
                var s when s.Contains("switch") => DeviceType.Switch,
                var s when s.Contains("workstation") || s.Contains("smb") => DeviceType.Workstation,
                var s when s.Contains("server") => DeviceType.Server,
                var s when s.Contains("smart") => DeviceType.SmartTV,
                _ => DeviceType.NetworkDevice
            };
        }

        private DeviceType DetermineDeviceTypeFromQueries(List<string> queries)
        {
            var queryText = string.Join(" ", queries).ToLower();

            // HomeKit/Apple device detection
            if (queryText.Contains("_hap.") || queryText.Contains("_homekit.") || queryText.Contains("_companion-link."))
                return DeviceType.MobileDevice;

            // Other device types
            if (queryText.Contains("camera") || queryText.Contains("onvif")) return DeviceType.Camera;
            if (queryText.Contains("airplay") || queryText.Contains("chromecast")) return DeviceType.SmartTV;
            if (queryText.Contains("printer")) return DeviceType.Printer;
            if (queryText.Contains("server")) return DeviceType.Server;

            return DeviceType.Workstation; // Default for active queriers
        }

        private static string ExtractHostname(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return "";

            var parts = fullName.Split('.');
            return parts.Length > 0 ? parts[0] : fullName;
        }

        private static string ExtractDeviceNameFromInstance(string instanceName)
        {
            if (string.IsNullOrEmpty(instanceName)) return "";

            // Remove common suffixes
            var name = instanceName
                .Replace(".local", "")
                .Replace("._tcp", "")
                .Replace("._udp", "");

            var parts = name.Split('.');
            return parts.Length > 0 ? parts[0] : name;
        }
    }
}