using System.Net;

using wpfhikip.Discovery.Core;
using wpfhikip.Discovery.Models;

namespace wpfhikip.Discovery.Protocols.Mdns
{
    /// <summary>
    /// Simplified mDNS response parser
    /// </summary>
    internal class MdnsResponseParser
    {
        /// <summary>
        /// Parse mDNS response and create discovered devices
        /// </summary>
        public List<DiscoveredDevice> ParseResponse(byte[] data, IPEndPoint remoteEndPoint)
        {
            var devices = new List<DiscoveredDevice>();

            try
            {
                var message = MdnsMessage.Parse(data);
                if (message == null)
                {
                    // Create basic device from endpoint
                    devices.Add(CreateBasicDevice(remoteEndPoint));
                    return devices;
                }

                // Process records to find devices
                var allRecords = message.Answers.Concat(message.Additional).ToList();
                if (allRecords.Any())
                {
                    var foundDevices = ProcessRecords(allRecords, remoteEndPoint);
                    devices.AddRange(foundDevices);
                }

                // Process questions as device indicators
                if (message.Questions.Any())
                {
                    var queryDevice = ProcessQuestions(message.Questions, remoteEndPoint);
                    if (queryDevice != null) devices.Add(queryDevice);
                }

                // Ensure we have at least one device
                if (!devices.Any())
                {
                    devices.Add(CreateBasicDevice(remoteEndPoint));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing mDNS response: {ex.Message}");
                devices.Add(CreateBasicDevice(remoteEndPoint));
            }

            return devices;
        }

        private DiscoveredDevice CreateBasicDevice(IPEndPoint endPoint)
        {
            var device = new DiscoveredDevice(endPoint.Address, 80)
            {
                Name = $"Device-{endPoint.Address}",
                UniqueId = $"mdns:{endPoint.Address}",
                DeviceType = DeviceType.Unknown,
                Description = "mDNS responding device"
            };

            device.DiscoveryMethods.Add(DiscoveryMethod.mDNS);
            device.DiscoveryData["Source"] = "Basic detection";
            device.Capabilities.Add("mDNS");

            return device;
        }

        private List<DiscoveredDevice> ProcessRecords(List<MdnsRecord> records, IPEndPoint remoteEndPoint)
        {
            var devices = new List<DiscoveredDevice>();
            var deviceMap = new Dictionary<string, DiscoveredDevice>();

            foreach (var record in records)
            {
                try
                {
                    if (record.Type == MdnsRecordType.A && !string.IsNullOrEmpty(record.Data))
                    {
                        // A record contains IP address
                        if (IPAddress.TryParse(record.Data, out var ip))
                        {
                            var device = CreateDeviceFromIP(ip, record.Name);
                            deviceMap[ip.ToString()] = device;
                        }
                    }
                    else if (record.Type == MdnsRecordType.PTR)
                    {
                        // PTR record indicates service
                        var device = CreateDeviceFromService(record, remoteEndPoint);
                        if (device != null)
                        {
                            var key = device.IPAddress?.ToString() ?? device.UniqueId;
                            deviceMap[key] = device;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error processing record: {ex.Message}");
                }
            }

            return deviceMap.Values.ToList();
        }

        private DiscoveredDevice CreateDeviceFromIP(IPAddress ip, string name)
        {
            var device = new DiscoveredDevice(ip, 80)
            {
                Name = ExtractHostname(name),
                UniqueId = $"mdns:{ip}",
                DeviceType = DeviceType.Unknown,
                Description = "Device discovered via mDNS A record"
            };

            device.DiscoveryMethods.Add(DiscoveryMethod.mDNS);
            device.DiscoveryData["RecordType"] = "A";
            device.Capabilities.Add("mDNS");

            return device;
        }

        private DiscoveredDevice? CreateDeviceFromService(MdnsRecord record, IPEndPoint remoteEndPoint)
        {
            var serviceName = record.Name.ToLowerInvariant();
            var deviceType = DetermineDeviceType(serviceName);

            var device = new DiscoveredDevice(remoteEndPoint.Address, remoteEndPoint.Port)
            {
                Name = $"{deviceType} Device ({remoteEndPoint.Address})",
                UniqueId = $"mdns:service:{remoteEndPoint.Address}",
                DeviceType = deviceType,
                Description = $"Device providing {serviceName}"
            };

            device.DiscoveryMethods.Add(DiscoveryMethod.mDNS);
            device.DiscoveryData["ServiceType"] = serviceName;
            device.Capabilities.Add("mDNS");

            AddServiceCapabilities(device, serviceName);

            return device;
        }

        private DiscoveredDevice? ProcessQuestions(List<MdnsRecord> questions, IPEndPoint remoteEndPoint)
        {
            var serviceQueries = questions
                .Where(q => q.Type == MdnsRecordType.PTR)
                .Select(q => q.Name.ToLowerInvariant())
                .ToList();

            if (!serviceQueries.Any()) return null;

            var deviceType = DetermineDeviceTypeFromQueries(serviceQueries);
            var deviceName = DetermineDeviceName(serviceQueries, remoteEndPoint.Address);

            var device = new DiscoveredDevice(remoteEndPoint.Address, remoteEndPoint.Port)
            {
                Name = deviceName,
                UniqueId = $"mdns:query:{remoteEndPoint.Address}",
                DeviceType = deviceType,
                Description = "Device discovered via mDNS queries"
            };

            device.DiscoveryMethods.Add(DiscoveryMethod.mDNS);
            device.DiscoveryData["QueryBasedDiscovery"] = true;
            device.DiscoveryData["QueriedServices"] = string.Join(", ", serviceQueries);
            device.Capabilities.Add("mDNS");

            foreach (var query in serviceQueries)
            {
                AddServiceCapabilities(device, query);
            }

            return device;
        }

        private DeviceType DetermineDeviceType(string serviceName)
        {
            return serviceName switch
            {
                var s when s.Contains("onvif") || s.Contains("camera") => DeviceType.Camera,
                var s when s.Contains("printer") || s.Contains("ipp") => DeviceType.Printer,
                var s when s.Contains("airplay") || s.Contains("raop") => DeviceType.StreamingDevice,
                var s when s.Contains("chromecast") || s.Contains("googlecast") => DeviceType.StreamingDevice,
                var s when s.Contains("ssh") || s.Contains("telnet") => DeviceType.NetworkDevice,
                var s when s.Contains("upnp") || s.Contains("dlna") => DeviceType.MediaServer,
                _ => DeviceType.Unknown
            };
        }

        private DeviceType DetermineDeviceTypeFromQueries(List<string> queries)
        {
            if (queries.Any(q => q.Contains("onvif") || q.Contains("camera"))) return DeviceType.Camera;
            if (queries.Any(q => q.Contains("airplay") || q.Contains("raop"))) return DeviceType.StreamingDevice;
            if (queries.Any(q => q.Contains("printer"))) return DeviceType.Printer;
            if (queries.Any(q => q.Contains("ssh") || q.Contains("telnet"))) return DeviceType.NetworkDevice;
            return DeviceType.Unknown;
        }

        private string DetermineDeviceName(List<string> queries, IPAddress ip)
        {
            if (queries.Any(q => q.Contains("airplay"))) return $"AirPlay Device ({ip})";
            if (queries.Any(q => q.Contains("chromecast"))) return $"Chromecast ({ip})";
            if (queries.Any(q => q.Contains("printer"))) return $"Printer ({ip})";
            if (queries.Any(q => q.Contains("camera"))) return $"Camera ({ip})";
            return $"mDNS Device ({ip})";
        }

        private void AddServiceCapabilities(DiscoveredDevice device, string serviceName)
        {
            if (serviceName.Contains("http")) device.Capabilities.Add("HTTP");
            if (serviceName.Contains("https")) device.Capabilities.Add("HTTPS");
            if (serviceName.Contains("airplay")) device.Capabilities.Add("AirPlay");
            if (serviceName.Contains("onvif")) device.Capabilities.Add("ONVIF");
            if (serviceName.Contains("ssh")) device.Capabilities.Add("SSH");
            if (serviceName.Contains("printer")) device.Capabilities.Add("Printing");
        }

        private string ExtractHostname(string fullName)
        {
            if (string.IsNullOrEmpty(fullName)) return "Unknown";

            var parts = fullName.Split('.');
            return parts.Length > 0 ? parts[0] : "Unknown";
        }
    }
}