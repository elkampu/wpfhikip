using wpfhikip.Discovery.Protocols.PortScan;

namespace wpfhikip.Discovery.Core
{
    /// <summary>
    /// Base interface for all network discovery services
    /// </summary>
    public interface INetworkDiscoveryService
    {
        /// <summary>
        /// Name of the discovery service (e.g., "SSDP/UPnP", "WS-Discovery")
        /// </summary>
        string ServiceName { get; }

        /// <summary>
        /// Default timeout for discovery operations
        /// </summary>
        TimeSpan DefaultTimeout { get; }

        /// <summary>
        /// Discovers devices on all available network segments
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Collection of discovered devices</returns>
        Task<IEnumerable<DiscoveredDevice>> DiscoverDevicesAsync(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Discovers devices on a specific network segment
        /// </summary>
        /// <param name="networkSegment">Network segment to scan (e.g., "192.168.1.0/24")</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Collection of discovered devices</returns>
        Task<IEnumerable<DiscoveredDevice>> DiscoverDevicesAsync(
            string networkSegment,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Event fired when a device is discovered
        /// </summary>
        event EventHandler<DeviceDiscoveredEventArgs> DeviceDiscovered;

        /// <summary>
        /// Event fired when discovery progress changes
        /// </summary>
        event EventHandler<DiscoveryProgressEventArgs> ProgressChanged;
    }


    /// <summary>
    /// Extended interface for discovery services that support port scanning
    /// </summary>
    public interface IPortScanningService : INetworkDiscoveryService
    {
        /// <summary>
        /// Scans specific ports on a target
        /// </summary>
        /// <param name="target">Target IP address or hostname</param>
        /// <param name="ports">Ports to scan</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Port scan results</returns>
        Task<IEnumerable<PortScanResult>> ScanPortsAsync(
            string target,
            IEnumerable<int> ports,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Scans a range of ports on a target
        /// </summary>
        /// <param name="target">Target IP address or hostname</param>
        /// <param name="startPort">Starting port number</param>
        /// <param name="endPort">Ending port number</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Port scan results</returns>
        Task<IEnumerable<PortScanResult>> ScanPortRangeAsync(
            string target,
            int startPort,
            int endPort,
            CancellationToken cancellationToken = default);
    }
}