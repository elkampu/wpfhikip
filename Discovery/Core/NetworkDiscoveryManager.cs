using System.Collections.Concurrent;
using wpfhikip.Discovery.Protocols.Ssdp;
using wpfhikip.Discovery.Protocols.WsDiscovery;
using wpfhikip.Discovery.Protocols.Mdns;
using wpfhikip.Discovery.Protocols.Arp;
using wpfhikip.Discovery.Protocols.Icmp;
using wpfhikip.Discovery.Models;

namespace wpfhikip.Discovery.Core
{
    public class NetworkDiscoveryManager : IDisposable
    {
        private readonly List<INetworkDiscoveryService> _discoveryServices;
        private readonly ConcurrentDictionary<string, DiscoveredDevice> _discoveredDevices;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private bool _disposed = false;

        public NetworkDiscoveryManager()
        {
            _discoveredDevices = new ConcurrentDictionary<string, DiscoveredDevice>();
            _cancellationTokenSource = new CancellationTokenSource();
            _discoveryServices = new List<INetworkDiscoveryService>
            {
                new SsdpDiscoveryService(),
                new WsDiscoveryService(),
                new MdnsDiscoveryService(),
                new ArpDiscoveryService(),
                new IcmpDiscoveryService(),
                // Add more services as implemented
            };

            // Subscribe to device discovery events from all services
            foreach (var service in _discoveryServices)
            {
                service.DeviceDiscovered += OnDeviceDiscovered;
                service.ProgressChanged += OnProgressChanged;
            }
        }

        /// <summary>
        /// Discovers all devices using all available discovery methods
        /// </summary>
        public async Task<IEnumerable<DiscoveredDevice>> DiscoverAllDevicesAsync(
            CancellationToken cancellationToken = default)
        {
            using var combinedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _cancellationTokenSource.Token);

            var tasks = _discoveryServices.Select(service =>
                DiscoverWithService(service, combinedTokenSource.Token));

            await Task.WhenAll(tasks);

            return _discoveredDevices.Values.ToList();
        }

        /// <summary>
        /// Discovers devices on a specific network segment
        /// </summary>
        public async Task<IEnumerable<DiscoveredDevice>> DiscoverDevicesAsync(
            string networkSegment,
            CancellationToken cancellationToken = default)
        {
            using var combinedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, _cancellationTokenSource.Token);

            var tasks = _discoveryServices.Select(service =>
                DiscoverWithServiceAndSegment(service, networkSegment, combinedTokenSource.Token));

            await Task.WhenAll(tasks);

            return _discoveredDevices.Values
                .Where(d => IsInNetworkSegment(d, networkSegment))
                .ToList();
        }

        /// <summary>
        /// Discovers devices using a specific discovery method
        /// </summary>
        public async Task<DiscoveryResult> DiscoverWithMethodAsync(
            DiscoveryMethod method,
            string? networkSegment = null,
            CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            var service = _discoveryServices.FirstOrDefault(s => GetDiscoveryMethod(s) == method);

            if (service == null)
            {
                return DiscoveryResult.CreateFailure(method, "Discovery service not available", startTime);
            }

            try
            {
                using var combinedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken, _cancellationTokenSource.Token);

                IEnumerable<DiscoveredDevice> devices;

                if (!string.IsNullOrEmpty(networkSegment))
                {
                    devices = await service.DiscoverDevicesAsync(networkSegment, combinedTokenSource.Token);
                }
                else
                {
                    devices = await service.DiscoverDevicesAsync(combinedTokenSource.Token);
                }

                // Add to main collection
                foreach (var device in devices)
                {
                    _discoveredDevices.AddOrUpdate(
                        device.UniqueId,
                        device,
                        (key, existing) => MergeDeviceInfo(existing, device));
                }

                return DiscoveryResult.CreateSuccess(method, devices, startTime);
            }
            catch (Exception ex)
            {
                return DiscoveryResult.CreateFailure(method, ex.Message, startTime, ex);
            }
        }

        /// <summary>
        /// Gets all currently discovered devices
        /// </summary>
        public IEnumerable<DiscoveredDevice> GetDiscoveredDevices()
        {
            return _discoveredDevices.Values.ToList();
        }

        /// <summary>
        /// Clears all discovered devices
        /// </summary>
        public void ClearDiscoveredDevices()
        {
            _discoveredDevices.Clear();
        }

        /// <summary>
        /// Cancels all running discovery operations
        /// </summary>
        public void CancelDiscovery()
        {
            _cancellationTokenSource.Cancel();
        }

        private async Task DiscoverWithService(
            INetworkDiscoveryService service,
            CancellationToken cancellationToken)
        {
            try
            {
                var devices = await service.DiscoverDevicesAsync(cancellationToken);

                foreach (var device in devices)
                {
                    _discoveredDevices.AddOrUpdate(
                        device.UniqueId,
                        device,
                        (key, existing) => MergeDeviceInfo(existing, device));
                }
            }
            catch (Exception ex)
            {
                OnDiscoveryError?.Invoke(this, new DiscoveryErrorEventArgs(service.ServiceName, ex));
            }
        }

        private async Task DiscoverWithServiceAndSegment(
            INetworkDiscoveryService service,
            string networkSegment,
            CancellationToken cancellationToken)
        {
            try
            {
                var devices = await service.DiscoverDevicesAsync(networkSegment, cancellationToken);

                foreach (var device in devices)
                {
                    _discoveredDevices.AddOrUpdate(
                        device.UniqueId,
                        device,
                        (key, existing) => MergeDeviceInfo(existing, device));
                }
            }
            catch (Exception ex)
            {
                OnDiscoveryError?.Invoke(this, new DiscoveryErrorEventArgs(service.ServiceName, ex));
            }
        }

        /// <summary>
        /// Merges device information from two DiscoveredDevice instances
        /// </summary>
        private DiscoveredDevice MergeDeviceInfo(DiscoveredDevice existing, DiscoveredDevice newDevice)
        {
            existing.UpdateFrom(newDevice);
            return existing;
        }

        /// <summary>
        /// Checks if a device belongs to a specific network segment
        /// </summary>
        private bool IsInNetworkSegment(DiscoveredDevice device, string networkSegment)
        {
            if (device.IPAddress == null || string.IsNullOrEmpty(networkSegment))
                return false;

            // Simple implementation - can be enhanced for proper CIDR matching
            return device.IPAddress.ToString().StartsWith(networkSegment.Split('/')[0].Substring(0, networkSegment.LastIndexOf('.')));
        }

        /// <summary>
        /// Maps discovery service to discovery method enum
        /// </summary>
        private DiscoveryMethod GetDiscoveryMethod(INetworkDiscoveryService service)
        {
            return service.ServiceName.ToLower() switch
            {
                "ssdp/upnp" => DiscoveryMethod.SSDP,
                "ws-discovery" => DiscoveryMethod.WSDiscovery,
                "mdns/bonjour" => DiscoveryMethod.mDNS,
                "arp" => DiscoveryMethod.ARP,
                "icmp" => DiscoveryMethod.ICMP,
                "snmp" => DiscoveryMethod.SNMP,
                "port scan" => DiscoveryMethod.PortScan,
                _ => DiscoveryMethod.Unknown
            };
        }

        private void OnDeviceDiscovered(object? sender, DeviceDiscoveredEventArgs e)
        {
            DeviceDiscovered?.Invoke(this, e);
        }

        private void OnProgressChanged(object? sender, DiscoveryProgressEventArgs e)
        {
            ProgressChanged?.Invoke(this, e);
        }

        public event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;
        public event EventHandler<DiscoveryProgressEventArgs>? ProgressChanged;
        public event EventHandler<DiscoveryErrorEventArgs>? OnDiscoveryError;

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
                    _cancellationTokenSource.Cancel();
                    _cancellationTokenSource.Dispose();

                    // Unsubscribe from events
                    foreach (var service in _discoveryServices)
                    {
                        service.DeviceDiscovered -= OnDeviceDiscovered;
                        service.ProgressChanged -= OnProgressChanged;

                        if (service is IDisposable disposableService)
                        {
                            disposableService.Dispose();
                        }
                    }

                    _discoveryServices.Clear();
                }

                _disposed = true;
            }
        }
    }
}