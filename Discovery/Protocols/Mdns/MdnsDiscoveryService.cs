using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

using wpfhikip.Discovery.Core;
using wpfhikip.Discovery.Models;

namespace wpfhikip.Discovery.Protocols.Mdns
{
    /// <summary>
    /// RFC 6762/6763 compliant mDNS discovery service - local queries, global listening
    /// </summary>
    public class MdnsDiscoveryService : INetworkDiscoveryService, IDisposable
    {
        public string ServiceName => "mDNS/Bonjour";
        public TimeSpan DefaultTimeout => TimeSpan.FromMinutes(5); // Extended timeout for comprehensive discovery

        private readonly MdnsNetworkManager _networkManager = new();
        private readonly MdnsQueryEngine _queryEngine = new();
        private readonly MdnsResponseProcessor _responseProcessor = new();
        private readonly MdnsCache _cache = new();
        private readonly SemaphoreSlim _operationSemaphore = new(1, 1);

        private CancellationTokenSource? _discoveryCancel;
        private volatile bool _disposed;
        private Timer? _continuousTimer;

        public event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;
        public event EventHandler<DiscoveryProgressEventArgs>? ProgressChanged;

        public MdnsDiscoveryService()
        {
            _responseProcessor.DeviceDiscovered += OnDeviceDiscovered;
            _cache.ServiceExpired += OnServiceExpired;

            // Initialize network change monitoring
            NetworkChange.NetworkAddressChanged += OnNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        }

        public async Task<IEnumerable<DiscoveredDevice>> DiscoverDevicesAsync(CancellationToken cancellationToken = default)
        {
            return await DiscoverDevicesAsync(null, cancellationToken);
        }

        public async Task<IEnumerable<DiscoveredDevice>> DiscoverDevicesAsync(string? networkSegment, CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MdnsDiscoveryService));

            if (!await _operationSemaphore.WaitAsync(1000, cancellationToken))
                throw new InvalidOperationException("mDNS discovery already in progress");

            try
            {
                // Cancel any previous discovery
                _discoveryCancel?.Cancel();
                _discoveryCancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                var discoveredDevices = new ConcurrentDictionary<string, DiscoveredDevice>();

                ReportProgress(5, "Initializing mDNS discovery (local queries, global listening for 5 minutes)");

                // Initialize network interfaces
                await _networkManager.InitializeAsync(_discoveryCancel.Token);
                if (_networkManager.ActiveInterfaces.Count == 0)
                {
                    ReportProgress(100, "No suitable network interfaces found for mDNS");
                    return Array.Empty<DiscoveredDevice>();
                }

                ReportProgress(10, $"Active on {_networkManager.ActiveInterfaces.Count} interfaces (listening globally for 5 minutes)");

                // Start response listeners with 5-minute duration
                var listeningTask = StartResponseListeners(discoveredDevices, networkSegment, _discoveryCancel.Token);

                ReportProgress(20, "Starting local subnet service discovery");

                // Execute local discovery strategy (fast initial queries)
                await ExecuteLocalDiscovery(_discoveryCancel.Token);

                ReportProgress(30, "Listening for responses from all subnets (5 minutes total)");

                // Wait for the 5-minute listening period to complete
                // Show progress updates during the listening period
                var listenDuration = TimeSpan.FromMinutes(5);
                var startTime = DateTime.UtcNow;
                var updateInterval = TimeSpan.FromSeconds(15); // Update progress every 15 seconds

                while (DateTime.UtcNow - startTime < listenDuration && !_discoveryCancel.Token.IsCancellationRequested)
                {
                    var elapsed = DateTime.UtcNow - startTime;
                    var remainingMinutes = (listenDuration - elapsed).TotalMinutes;
                    var progressPercent = 30 + (int)(60 * elapsed.TotalMinutes / listenDuration.TotalMinutes);

                    ReportProgress(progressPercent, $"Listening for devices - {remainingMinutes:F1} minutes remaining ({discoveredDevices.Count} found)");

                    try
                    {
                        await Task.Delay(updateInterval, _discoveryCancel.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                ReportProgress(90, "Processing discovery results");

                // Get final results from cache and direct discoveries
                var finalResults = await GetFinalResults(discoveredDevices, networkSegment);

                ReportProgress(100, $"mDNS discovery complete - {finalResults.Count} devices found after 5-minute scan");

                return finalResults;
            }
            catch (OperationCanceledException)
            {
                ReportProgress(100, "mDNS discovery cancelled");
                return Array.Empty<DiscoveredDevice>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"mDNS discovery error: {ex.Message}");
                ReportProgress(100, $"mDNS discovery failed: {ex.Message}");
                return Array.Empty<DiscoveredDevice>();
            }
            finally
            {
                _operationSemaphore.Release();

                // Cleanup in background
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(1000); // Allow final processing
                        _networkManager.StopListening();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Cleanup error: {ex.Message}");
                    }
                });
            }
        }

        private async Task StartResponseListeners(ConcurrentDictionary<string, DiscoveredDevice> devices, string? networkSegment, CancellationToken cancellationToken)
        {
            foreach (var networkInterface in _networkManager.ActiveInterfaces)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Use the overloaded method with 5-minute duration
                        await _networkManager.ListenForResponsesAsync(networkInterface, (response, endpoint) =>
                        {
                            ProcessMdnsResponse(response, endpoint, devices, networkSegment);
                        }, TimeSpan.FromMinutes(5), cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Listener error on {networkInterface.Name}: {ex.Message}");
                    }
                }, cancellationToken);
            }
        }

        private async Task ExecuteLocalDiscovery(CancellationToken cancellationToken)
        {
            var phases = MdnsConstants.GetServicesByPriority();
            var totalPhases = phases.Length;

            // Local burst strategy: send each phase twice with short delays
            for (int phaseIndex = 0; phaseIndex < phases.Length; phaseIndex++)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var phase = phases[phaseIndex];
                var phaseProgress = 20 + (10 * phaseIndex / totalPhases); // Reduced progress range since we have 5-minute listening

                ReportProgress(phaseProgress, $"Phase {phaseIndex + 1}/{totalPhases}: Discovering {GetPhaseDescription(phaseIndex)} (local subnet)");

                // Double burst for local coverage
                for (int burst = 0; burst < 2; burst++)
                {
                    await _queryEngine.SendQueriesAsync(_networkManager, phase, cancellationToken);

                    // Short delay between bursts
                    if (burst == 0)
                    {
                        await Task.Delay(150, cancellationToken);
                    }
                }

                // Standard delay between phases
                var phaseDelay = phaseIndex switch
                {
                    0 => 800, // Core services
                    1 => 800, // Security services
                    2 => 600,
                    _ => 400
                };

                try
                {
                    await Task.Delay(phaseDelay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            // Final local sweep
            ReportProgress(29, "Final local service enumeration sweep");
            await _queryEngine.SendFinalSweepAsync(_networkManager, cancellationToken);
        }

        private void ProcessMdnsResponse(byte[] data, IPEndPoint source, ConcurrentDictionary<string, DiscoveredDevice> devices, string? networkSegment)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"mDNS: Processing response from {source.Address} ({data.Length} bytes)");

                var message = MdnsMessage.Parse(data);
                if (message == null)
                {
                    System.Diagnostics.Debug.WriteLine($"mDNS: Failed to parse message from {source.Address}");
                    return;
                }

                // Log message structure
                System.Diagnostics.Debug.WriteLine($"mDNS: Message from {source.Address} - Questions: {message.Questions.Count}, Answers: {message.Answers.Count}, Authority: {message.Authority.Count}, Additional: {message.Additional.Count}");

                // Log if this is a cross-subnet response
                if (!IsLocalSubnet(source.Address))
                {
                    System.Diagnostics.Debug.WriteLine($"mDNS: Processing cross-subnet response from {source.Address}");
                }

                // Process all records in the response
                var allRecords = message.Answers
                    .Concat(message.Authority)
                    .Concat(message.Additional)
                    .ToList();

                if (allRecords.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"mDNS: Processing {allRecords.Count} answer/authority/additional records from {source.Address}");
                    var processedDevices = _responseProcessor.ProcessRecords(allRecords, source, networkSegment);

                    foreach (var device in processedDevices)
                    {
                        // Update cache
                        _cache.UpdateDevice(device);

                        // Add to results
                        devices.AddOrUpdate(device.UniqueId, device, (key, existing) =>
                        {
                            existing.UpdateFrom(device);
                            return existing;
                        });

                        // Fire event for real-time updates
                        DeviceDiscovered?.Invoke(this, new DeviceDiscoveredEventArgs(device, ServiceName));
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"mDNS: No answer/authority/additional records from {source.Address}");
                }

                // Also process questions to identify active queriers
                if (message.Questions.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"mDNS: Processing {message.Questions.Count} questions from {source.Address}");
                    var querierDevice = _responseProcessor.ProcessQuestions(message.Questions, source, networkSegment);
                    if (querierDevice != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"mDNS: Created querier device for {source.Address}: {querierDevice.Name} ({querierDevice.DeviceType})");

                        // Update cache
                        _cache.UpdateDevice(querierDevice);

                        // Add to results - use TryAdd to avoid overwriting existing devices
                        var wasAdded = devices.TryAdd(querierDevice.UniqueId, querierDevice);
                        if (!wasAdded)
                        {
                            // Update existing device
                            devices.AddOrUpdate(querierDevice.UniqueId, querierDevice, (key, existing) =>
                            {
                                existing.UpdateFrom(querierDevice);
                                return existing;
                            });
                        }

                        System.Diagnostics.Debug.WriteLine($"mDNS: Added querier device {source.Address} to discovered devices (total: {devices.Count})");

                        // Fire event for real-time updates
                        DeviceDiscovered?.Invoke(this, new DeviceDiscoveredEventArgs(querierDevice, ServiceName));
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"mDNS: No querier device created for {source.Address}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing mDNS response from {source}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private async Task<List<DiscoveredDevice>> GetFinalResults(ConcurrentDictionary<string, DiscoveredDevice> discoveredDevices, string? networkSegment)
        {
            System.Diagnostics.Debug.WriteLine($"mDNS: GetFinalResults - {discoveredDevices.Count} devices in concurrent dictionary");

            // Combine direct discoveries with cached results
            var allDevices = new Dictionary<string, DiscoveredDevice>();

            // Add directly discovered devices
            foreach (var device in discoveredDevices.Values)
            {
                allDevices[device.UniqueId] = device;
                System.Diagnostics.Debug.WriteLine($"mDNS: Added device {device.IPAddress} ({device.Name}) to all devices");
            }

            // Add cached devices that are still valid
            foreach (var cachedDevice in _cache.GetValidDevices())
            {
                if (!allDevices.ContainsKey(cachedDevice.UniqueId))
                {
                    allDevices[cachedDevice.UniqueId] = cachedDevice;
                    System.Diagnostics.Debug.WriteLine($"mDNS: Added cached device {cachedDevice.IPAddress} ({cachedDevice.Name})");
                }
                else
                {
                    allDevices[cachedDevice.UniqueId].UpdateFrom(cachedDevice);
                    System.Diagnostics.Debug.WriteLine($"mDNS: Updated device {cachedDevice.IPAddress} from cache");
                }
            }

            System.Diagnostics.Debug.WriteLine($"mDNS: Total devices before filtering: {allDevices.Count}");

            // Filter out local devices
            var localIPs = GetLocalIPAddresses();
            System.Diagnostics.Debug.WriteLine($"mDNS: Local IPs to filter: {string.Join(", ", localIPs)}");

            var externalDevices = allDevices.Values
                .Where(d => d.IPAddress != null && !localIPs.Contains(d.IPAddress.ToString()))
                .Where(d => d.IsOnline)
                .OrderBy(d => d.IPAddress?.ToString())
                .ToList();

            System.Diagnostics.Debug.WriteLine($"mDNS: Devices after filtering: {externalDevices.Count}");

            foreach (var device in externalDevices)
            {
                System.Diagnostics.Debug.WriteLine($"mDNS: Final device: {device.IPAddress} ({device.Name}) - {device.DeviceType}");
            }

            // Show what was filtered out
            var filteredDevices = allDevices.Values
                .Where(d => d.IPAddress != null && localIPs.Contains(d.IPAddress.ToString()))
                .ToList();

            if (filteredDevices.Any())
            {
                System.Diagnostics.Debug.WriteLine($"mDNS: Filtered out {filteredDevices.Count} local devices:");
                foreach (var device in filteredDevices)
                {
                    System.Diagnostics.Debug.WriteLine($"mDNS: Filtered: {device.IPAddress} ({device.Name}) - {device.DeviceType}");
                }
            }

            // Enhance devices with additional information
            foreach (var device in externalDevices)
            {
                await EnhanceDeviceInformation(device);
            }

            // Log cross-subnet discoveries
            var crossSubnetDevices = externalDevices.Where(d => !IsLocalSubnet(d.IPAddress)).ToList();
            if (crossSubnetDevices.Any())
            {
                System.Diagnostics.Debug.WriteLine($"mDNS: Found {crossSubnetDevices.Count} cross-subnet devices (passive discovery)");
            }

            return externalDevices;
        }

        private bool IsLocalSubnet(IPAddress? deviceIP)
        {
            if (deviceIP == null) return false;

            try
            {
                foreach (var networkInterface in _networkManager.ActiveInterfaces)
                {
                    var properties = networkInterface.GetIPProperties();
                    foreach (var unicast in properties.UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            var localNetwork = GetNetworkAddress(unicast.Address, unicast.IPv4Mask);
                            var deviceNetwork = GetNetworkAddress(deviceIP, unicast.IPv4Mask);

                            if (localNetwork != null && deviceNetwork != null && localNetwork.Equals(deviceNetwork))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking subnet for {deviceIP}: {ex.Message}");
            }

            return false;
        }

        private IPAddress? GetNetworkAddress(IPAddress ipAddress, IPAddress subnetMask)
        {
            try
            {
                var ipBytes = ipAddress.GetAddressBytes();
                var maskBytes = subnetMask.GetAddressBytes();
                var networkBytes = new byte[4];

                for (int i = 0; i < 4; i++)
                {
                    networkBytes[i] = (byte)(ipBytes[i] & maskBytes[i]);
                }

                return new IPAddress(networkBytes);
            }
            catch
            {
                return null;
            }
        }

        private async Task EnhanceDeviceInformation(DiscoveredDevice device)
        {
            try
            {
                // Try to get hostname if not already set
                if (string.IsNullOrEmpty(device.Name) && device.IPAddress != null)
                {
                    try
                    {
                        var hostEntry = await Dns.GetHostEntryAsync(device.IPAddress);
                        if (!string.IsNullOrEmpty(hostEntry.HostName))
                        {
                            device.Name = hostEntry.HostName;
                        }
                    }
                    catch
                    {
                        // Ignore DNS lookup failures
                    }
                }

                // Add mDNS to discovery methods
                device.DiscoveryMethods.Add(DiscoveryMethod.mDNS);

                // Mark cross-subnet devices (passive discovery)
                if (!IsLocalSubnet(device.IPAddress))
                {
                    device.Capabilities.Add("Cross-subnet (passive)");
                }

                // Update last seen
                device.LastSeen = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error enhancing device {device.IPAddress}: {ex.Message}");
            }
        }

        private HashSet<string> GetLocalIPAddresses()
        {
            var localIPs = new HashSet<string>();

            try
            {
                // Get from network interfaces
                foreach (var networkInterface in _networkManager.ActiveInterfaces)
                {
                    var properties = networkInterface.GetIPProperties();
                    foreach (var addr in properties.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            localIPs.Add(addr.Address.ToString());
                        }
                    }
                }

                // Add common local addresses
                localIPs.Add("127.0.0.1");
                localIPs.Add("0.0.0.0");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting local IPs: {ex.Message}");
            }

            return localIPs;
        }

        private void OnNetworkChanged(object? sender, EventArgs e)
        {
            // Reinitialize network interfaces when network changes
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2000); // Wait for network to stabilize
                    if (!_disposed)
                    {
                        await _networkManager.ReinitializeAsync();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error handling network change: {ex.Message}");
                }
            });
        }

        private void OnDeviceDiscovered(object? sender, DeviceDiscoveredEventArgs e)
        {
            DeviceDiscovered?.Invoke(this, e);
        }

        private void OnServiceExpired(object? sender, ServiceExpiredEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"mDNS service expired: {e.ServiceName} on {e.IPAddress}");
        }

        private void ReportProgress(int percent, string status)
        {
            ProgressChanged?.Invoke(this, new DiscoveryProgressEventArgs(ServiceName, percent, 100, "", status));
            System.Diagnostics.Debug.WriteLine($"mDNS Progress: {percent}% - {status}");
        }

        private static string GetPhaseDescription(int phaseIndex) => phaseIndex switch
        {
            0 => "core services",
            1 => "security & camera devices",
            2 => "network infrastructure",
            3 => "storage devices",
            4 => "media & streaming",
            5 => "printers & scanners",
            6 => "industrial & IoT",
            7 => "communication systems",
            8 => "development tools",
            9 => "gaming devices",
            10 => "generic services",
            _ => "additional services"
        };

        public void StartContinuousDiscovery()
        {
            _continuousTimer = new Timer(async _ =>
            {
                try
                {
                    if (!_disposed && _operationSemaphore.CurrentCount > 0)
                    {
                        await DiscoverDevicesAsync(CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Continuous discovery error: {ex.Message}");
                }
            }, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
        }

        public void StopContinuousDiscovery()
        {
            _continuousTimer?.Dispose();
            _continuousTimer = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _discoveryCancel?.Cancel();
            _continuousTimer?.Dispose();

            NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;

            _networkManager?.Dispose();
            _cache?.Dispose();
            _operationSemaphore?.Dispose();
            _discoveryCancel?.Dispose();
        }
    }
}