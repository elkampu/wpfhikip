using System.Collections.Concurrent;

using wpfhikip.Discovery.Core;
using wpfhikip.Discovery.Models;

namespace wpfhikip.Discovery.Protocols.Mdns
{
    /// <summary>
    /// Fixed mDNS discovery service with improved resource management
    /// </summary>
    public class MdnsDiscoveryService : INetworkDiscoveryService, IDisposable
    {
        public string ServiceName => "mDNS/Bonjour";
        public TimeSpan DefaultTimeout => TimeSpan.FromSeconds(15);

        private MdnsNetworkManager? _networkManager;
        private readonly MdnsQuerySender _querySender = new();
        private readonly MdnsResponseListener _responseListener = new();
        private readonly SemaphoreSlim _operationSemaphore = new(1, 1);

        private CancellationTokenSource? _currentCancellation;
        private volatile bool _disposed;

        public event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;
        public event EventHandler<DiscoveryProgressEventArgs>? ProgressChanged;

        public MdnsDiscoveryService()
        {
            _responseListener.DeviceDiscovered += OnDeviceDiscovered;
        }

        public async Task<IEnumerable<DiscoveredDevice>> DiscoverDevicesAsync(CancellationToken cancellationToken = default)
        {
            return await DiscoverDevicesAsync(null, cancellationToken);
        }

        public async Task<IEnumerable<DiscoveredDevice>> DiscoverDevicesAsync(string? networkSegment, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MdnsDiscoveryService));

            if (!await _operationSemaphore.WaitAsync(100, cancellationToken))
                throw new InvalidOperationException("mDNS discovery already in progress");

            List<Task>? listeningTasks = null;
            try
            {
                _currentCancellation?.Cancel();
                _currentCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                var devices = new ConcurrentDictionary<string, DiscoveredDevice>();

                ReportProgress(0, "Initializing mDNS discovery");

                // Create fresh network manager for each discovery
                _networkManager = new MdnsNetworkManager();

                // Initialize network with proper setup
                await _networkManager.InitializeAsync(_currentCancellation.Token);
                if (!_networkManager.Clients.Any() || !_networkManager.Listeners.Any())
                {
                    ReportProgress(100, "No network interfaces available for mDNS");
                    return Array.Empty<DiscoveredDevice>();
                }

                // Get local IPs for filtering
                var localIPs = GetLocalIPAddresses();
                System.Diagnostics.Debug.WriteLine($"mDNS: Local IPs: {string.Join(", ", localIPs)}");

                ReportProgress(20, "Starting mDNS listeners");

                // Start listening on dedicated listener clients
                listeningTasks = await _responseListener.StartListeningAsync(
                    _networkManager, devices, networkSegment, _currentCancellation.Token);

                ReportProgress(40, "Sending mDNS discovery queries");

                // Send targeted mDNS queries
                await SendTargetedQueriesAsync(_currentCancellation.Token);

                ReportProgress(60, "Waiting for mDNS responses");

                // Wait for responses with better monitoring
                await WaitForResponsesAsync(devices, localIPs, _currentCancellation.Token);

                ReportProgress(100, $"mDNS discovery complete - {devices.Count} total responses");

                // Filter out our own responses and return external devices only
                var externalDevices = devices.Values
                    .Where(d => d.IPAddress != null && !localIPs.Contains(d.IPAddress.ToString()))
                    .OrderBy(d => d.IPAddress?.ToString())
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"mDNS: Found {externalDevices.Count} external devices after filtering");

                return externalDevices;
            }
            finally
            {
                _operationSemaphore.Release();

                // Cleanup resources with proper timing
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Cancel current operations
                        _currentCancellation?.Cancel();

                        // Wait for listening tasks to complete with shorter timeout
                        if (listeningTasks != null)
                        {
                            try
                            {
                                await Task.WhenAll(listeningTasks).WaitAsync(TimeSpan.FromSeconds(1));
                            }
                            catch (OperationCanceledException)
                            {
                                // Expected during cancellation
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Warning: Error waiting for listening tasks: {ex.Message}");
                            }
                        }

                        // Small delay to allow final socket operations to complete
                        await Task.Delay(100);

                        // Dispose network manager
                        _networkManager?.Dispose();
                        _networkManager = null;

                        System.Diagnostics.Debug.WriteLine("mDNS: Cleanup completed");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error during mDNS cleanup: {ex.Message}");
                    }
                });
            }
        }

        private HashSet<string> GetLocalIPAddresses()
        {
            var localIPs = new HashSet<string>();

            // Add IPs from network manager clients
            if (_networkManager != null)
            {
                foreach (var client in _networkManager.Clients)
                {
                    try
                    {
                        if (client.Client.LocalEndPoint is System.Net.IPEndPoint localEP)
                        {
                            localIPs.Add(localEP.Address.ToString());
                        }
                    }
                    catch
                    {
                        // Ignore if client is disposed
                    }
                }
            }

            // Add common local IPs
            try
            {
                var interfaces = wpfhikip.Discovery.Core.NetworkUtils.GetLocalNetworkInterfaces();
                foreach (var iface in interfaces.Values)
                {
                    foreach (var addr in iface.IPv4Addresses)
                    {
                        localIPs.Add(addr.IPAddress.ToString());
                    }
                }
            }
            catch { }

            return localIPs;
        }

        private async Task SendTargetedQueriesAsync(CancellationToken cancellationToken)
        {
            if (_networkManager?.Clients.Any() != true) return;

            var primaryClient = _networkManager.Clients.First();

            try
            {
                // Phase 1: Universal service discovery (most likely to get responses)
                System.Diagnostics.Debug.WriteLine("mDNS: Phase 1 - Universal service discovery");
                await SendSingleQuery(primaryClient, "_services._dns-sd._udp.local.", cancellationToken);
                await Task.Delay(500, cancellationToken);

                // Phase 2: Common Apple/Bonjour services (very likely to respond)
                System.Diagnostics.Debug.WriteLine("mDNS: Phase 2 - Apple/Bonjour services");
                var appleServices = new[] {
                    "_airplay._tcp.local.",
                    "_raop._tcp.local.",
                    "_apple-mobdev2._tcp.local.",
                    "_companion-link._tcp.local."
                };
                await SendQueryBatch(primaryClient, appleServices, cancellationToken);
                await Task.Delay(1000, cancellationToken);

                // Phase 3: Common network services
                System.Diagnostics.Debug.WriteLine("mDNS: Phase 3 - Common network services");
                var commonServices = new[] {
                    "_http._tcp.local.",
                    "_https._tcp.local.",
                    "_printer._tcp.local.",
                    "_ssh._tcp.local."
                };
                await SendQueryBatch(primaryClient, commonServices, cancellationToken);
                await Task.Delay(1000, cancellationToken);

                // Phase 4: Security/Camera services
                System.Diagnostics.Debug.WriteLine("mDNS: Phase 4 - Security/Camera services");
                var securityServices = new[] {
                    "_onvif._tcp.local.",
                    "_camera._tcp.local.",
                    "_rtsp._tcp.local.",
                    "_axis-video._tcp.local."
                };
                await SendQueryBatch(primaryClient, securityServices, cancellationToken);
                await Task.Delay(1000, cancellationToken);

                // Phase 5: Broad discovery queries
                System.Diagnostics.Debug.WriteLine("mDNS: Phase 5 - Broad discovery");
                await SendSingleQuery(primaryClient, "_tcp.local.", cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during cancellation - suppress debug output
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in mDNS discovery: {ex.Message}");
            }
        }

        private async Task SendSingleQuery(System.Net.Sockets.UdpClient client, string service, CancellationToken cancellationToken)
        {
            try
            {
                var multicastEndpoint = new System.Net.IPEndPoint(
                    System.Net.IPAddress.Parse(MdnsConstants.MulticastAddress),
                    MdnsConstants.MulticastPort);

                var query = MdnsMessage.CreateQuery(service);
                var queryBytes = query.ToByteArray();

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                using var combined = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

                await client.SendAsync(queryBytes, multicastEndpoint).AsTask().WaitAsync(combined.Token);

                System.Diagnostics.Debug.WriteLine($"mDNS: Sent query for {service}");
            }
            catch (ObjectDisposedException)
            {
                // Client disposed - suppress debug output for expected case
            }
            catch (OperationCanceledException)
            {
                // Query cancelled - suppress debug output for expected case
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to send mDNS query for {service}: {ex.Message}");
            }
        }

        private async Task SendQueryBatch(System.Net.Sockets.UdpClient client, string[] services, CancellationToken cancellationToken)
        {
            try
            {
                var multicastEndpoint = new System.Net.IPEndPoint(
                    System.Net.IPAddress.Parse(MdnsConstants.MulticastAddress),
                    MdnsConstants.MulticastPort);

                var query = MdnsMessage.CreateQuery(services);
                var queryBytes = query.ToByteArray();

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                using var combined = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

                await client.SendAsync(queryBytes, multicastEndpoint).AsTask().WaitAsync(combined.Token);

                System.Diagnostics.Debug.WriteLine($"mDNS: Sent batch query for {services.Length} services");
            }
            catch (ObjectDisposedException)
            {
                // Client disposed - suppress debug output for expected case
            }
            catch (OperationCanceledException)
            {
                // Query cancelled - suppress debug output for expected case
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to send mDNS query batch: {ex.Message}");
            }
        }

        private async Task WaitForResponsesAsync(ConcurrentDictionary<string, DiscoveredDevice> devices, HashSet<string> localIPs, CancellationToken cancellationToken)
        {
            var startTime = DateTime.UtcNow;
            var lastExternalCount = 0;
            var noChangeCount = 0;
            var lastReportTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < DefaultTimeout && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(500, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                var totalCount = devices.Count;
                var externalCount = devices.Values.Count(d =>
                    d.IPAddress != null && !localIPs.Contains(d.IPAddress.ToString()));

                var elapsed = DateTime.UtcNow - startTime;
                var progress = 60 + (int)(40 * elapsed.TotalSeconds / DefaultTimeout.TotalSeconds);

                // Report progress every 2 seconds
                if (DateTime.UtcNow - lastReportTime > TimeSpan.FromSeconds(2))
                {
                    ReportProgress(Math.Min(progress, 99),
                        $"Total: {totalCount} responses, External: {externalCount} devices ({elapsed.TotalSeconds:F0}s)");
                    lastReportTime = DateTime.UtcNow;
                }

                // Track external devices for early termination
                if (externalCount == lastExternalCount)
                {
                    noChangeCount++;
                    if (noChangeCount >= 6 && elapsed.TotalSeconds > 8) // 3 seconds of no new external devices
                    {
                        System.Diagnostics.Debug.WriteLine($"mDNS: Early termination - no new external devices for 3 seconds");
                        break;
                    }
                }
                else
                {
                    noChangeCount = 0;
                    lastExternalCount = externalCount;
                    if (externalCount > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"mDNS: Found {externalCount} external devices so far");
                    }
                }
            }

            var finalTotal = devices.Count;
            var finalExternal = devices.Values.Count(d =>
                d.IPAddress != null && !localIPs.Contains(d.IPAddress.ToString()));

            System.Diagnostics.Debug.WriteLine($"mDNS: Final results - Total: {finalTotal}, External: {finalExternal}");
        }

        private void OnDeviceDiscovered(object? sender, DeviceDiscoveredEventArgs e)
        {
            DeviceDiscovered?.Invoke(this, e);
        }

        private void ReportProgress(int percent, string status)
        {
            ProgressChanged?.Invoke(this, new DiscoveryProgressEventArgs(ServiceName, percent, 100, "", status));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _currentCancellation?.Cancel();
            _networkManager?.Dispose();
            _operationSemaphore?.Dispose();
            _currentCancellation?.Dispose();
        }
    }
}