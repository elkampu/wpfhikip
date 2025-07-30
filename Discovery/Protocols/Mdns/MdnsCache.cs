using wpfhikip.Discovery.Core;
using wpfhikip.Discovery.Models;

namespace wpfhikip.Discovery.Protocols.Mdns
{
    /// <summary>
    /// RFC 6762 compliant mDNS cache with TTL management
    /// </summary>
    internal class MdnsCache : IDisposable
    {
        private readonly Dictionary<string, CachedDevice> _cache = new();
        private readonly Timer _cleanupTimer;
        private readonly object _lock = new();
        private volatile bool _disposed;

        public event EventHandler<ServiceExpiredEventArgs>? ServiceExpired;

        public MdnsCache()
        {
            // Cleanup expired entries every 30 seconds
            _cleanupTimer = new Timer(CleanupExpiredEntries, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }

        public void UpdateDevice(DiscoveredDevice device)
        {
            if (_disposed || device?.IPAddress == null) return;

            lock (_lock)
            {
                var key = device.IPAddress.ToString();
                var expiry = DateTime.UtcNow.AddMinutes(5); // Default 5-minute TTL

                if (_cache.TryGetValue(key, out var cached))
                {
                    cached.Device.UpdateFrom(device);
                    cached.LastSeen = DateTime.UtcNow;
                    cached.Expiry = expiry;
                }
                else
                {
                    _cache[key] = new CachedDevice
                    {
                        Device = device,
                        LastSeen = DateTime.UtcNow,
                        Expiry = expiry
                    };
                }
            }
        }

        public List<DiscoveredDevice> GetValidDevices()
        {
            if (_disposed) return new List<DiscoveredDevice>();

            lock (_lock)
            {
                var now = DateTime.UtcNow;
                return _cache.Values
                    .Where(cached => cached.Expiry > now)
                    .Select(cached => cached.Device)
                    .ToList();
            }
        }

        public DiscoveredDevice? GetDevice(string ipAddress)
        {
            if (_disposed) return null;

            lock (_lock)
            {
                if (_cache.TryGetValue(ipAddress, out var cached) && cached.Expiry > DateTime.UtcNow)
                {
                    return cached.Device;
                }
            }
            return null;
        }

        private void CleanupExpiredEntries(object? state)
        {
            if (_disposed) return;

            var expiredDevices = new List<CachedDevice>();

            lock (_lock)
            {
                var now = DateTime.UtcNow;
                var expiredKeys = _cache.Where(kvp => kvp.Value.Expiry <= now).Select(kvp => kvp.Key).ToList();

                foreach (var key in expiredKeys)
                {
                    if (_cache.TryGetValue(key, out var expired))
                    {
                        expiredDevices.Add(expired);
                        _cache.Remove(key);
                    }
                }
            }

            // Fire events for expired services
            foreach (var expired in expiredDevices)
            {
                try
                {
                    ServiceExpired?.Invoke(this, new ServiceExpiredEventArgs(
                        expired.Device.Name ?? "Unknown",
                        expired.Device.IPAddress?.ToString() ?? "Unknown"));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error firing service expired event: {ex.Message}");
                }
            }

            if (expiredDevices.Any())
            {
                System.Diagnostics.Debug.WriteLine($"mDNS Cache: Cleaned up {expiredDevices.Count} expired entries");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cleanupTimer?.Dispose();

            lock (_lock)
            {
                _cache.Clear();
            }
        }

        private class CachedDevice
        {
            public DiscoveredDevice Device { get; set; } = null!;
            public DateTime LastSeen { get; set; }
            public DateTime Expiry { get; set; }
        }
    }

    public class ServiceExpiredEventArgs : EventArgs
    {
        public string ServiceName { get; }
        public string IPAddress { get; }

        public ServiceExpiredEventArgs(string serviceName, string ipAddress)
        {
            ServiceName = serviceName;
            IPAddress = ipAddress;
        }
    }
}