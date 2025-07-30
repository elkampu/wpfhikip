using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

using wpfhikip.Discovery.Core;

namespace wpfhikip.Discovery.Protocols.Mdns
{
    /// <summary>
    /// Properly configured mDNS response listener with improved error handling
    /// </summary>
    internal class MdnsResponseListener
    {
        private readonly MdnsResponseParser _parser = new();

        public event EventHandler<DeviceDiscoveredEventArgs>? DeviceDiscovered;

        /// <summary>
        /// Start listening for responses on dedicated listener clients
        /// </summary>
        public async Task<List<Task>> StartListeningAsync(
            MdnsNetworkManager networkManager,
            ConcurrentDictionary<string, DiscoveredDevice> devices,
            string? networkSegment,
            CancellationToken cancellationToken)
        {
            var tasks = new List<Task>();

            // Listen on dedicated listening clients (bound to port 5353)
            foreach (var listener in networkManager.Listeners)
            {
                var task = ListenAsync(listener, devices, networkSegment, cancellationToken, networkManager);
                tasks.Add(task);
            }

            System.Diagnostics.Debug.WriteLine($"mDNS: Started {tasks.Count} listening tasks");
            return tasks;
        }

        private async Task ListenAsync(
            UdpClient client,
            ConcurrentDictionary<string, DiscoveredDevice> devices,
            string? networkSegment,
            CancellationToken cancellationToken,
            MdnsNetworkManager networkManager)
        {
            var localEndpoint = "unknown";
            try
            {
                localEndpoint = client.Client.LocalEndPoint?.ToString() ?? "unknown";
                System.Diagnostics.Debug.WriteLine($"mDNS: Listening on {localEndpoint}");

                while (!cancellationToken.IsCancellationRequested && !networkManager.IsDisposed)
                {
                    try
                    {
                        // Check if the network manager is disposed before attempting receive
                        if (networkManager.IsDisposed)
                        {
                            System.Diagnostics.Debug.WriteLine($"mDNS: Stopping listener on {localEndpoint} - network manager disposed");
                            break;
                        }

                        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

                        var result = await client.ReceiveAsync().WaitAsync(combinedCts.Token);

                        System.Diagnostics.Debug.WriteLine($"mDNS: Received {result.Buffer.Length} bytes from {result.RemoteEndPoint}");

                        // Quick network filtering
                        if (!string.IsNullOrEmpty(networkSegment) &&
                            !wpfhikip.Discovery.Core.NetworkUtils.IsIPInSegment(result.RemoteEndPoint.Address, networkSegment))
                        {
                            continue;
                        }

                        // Process response in background with cancellation support
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await ProcessResponseAsync(result.Buffer, result.RemoteEndPoint, devices);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error processing mDNS response: {ex.Message}");
                            }
                        }, CancellationToken.None);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        System.Diagnostics.Debug.WriteLine($"mDNS: Listener on {localEndpoint} cancelled");
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        // Timeout - continue listening if not disposed (suppress debug output for timeout)
                        continue;
                    }
                    catch (ObjectDisposedException)
                    {
                        System.Diagnostics.Debug.WriteLine($"mDNS: Client on {localEndpoint} disposed - stopping listener");
                        break;
                    }
                    catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut)
                    {
                        // Timeout - continue (suppress debug output)
                        continue;
                    }
                    catch (SocketException ex) when (
                        ex.SocketErrorCode == SocketError.OperationAborted ||
                        ex.SocketErrorCode == SocketError.Interrupted ||
                        ex.SocketErrorCode == SocketError.InvalidArgument)
                    {
                        // Socket closed - expected during shutdown (suppress debug output)
                        break;
                    }
                    catch (SocketException ex) when (networkManager.IsDisposed)
                    {
                        // Socket errors during disposal are expected (suppress debug output)
                        break;
                    }
                    catch (SocketException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"mDNS socket error on {localEndpoint}: {ex.Message}");

                        // Wait a bit before retrying, but check cancellation
                        try
                        {
                            await Task.Delay(100, cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"mDNS listen error on {localEndpoint}: {ex.Message}");

                        // Wait a bit before retrying, but check cancellation
                        try
                        {
                            await Task.Delay(100, cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine($"mDNS: Listener on {localEndpoint} operation cancelled");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fatal mDNS listen error on {localEndpoint}: {ex.Message}");
            }
            finally
            {
                System.Diagnostics.Debug.WriteLine($"mDNS: Listener on {localEndpoint} stopped");
            }
        }

        private async Task ProcessResponseAsync(byte[] data, IPEndPoint remoteEndPoint, ConcurrentDictionary<string, DiscoveredDevice> devices)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"mDNS: Processing response from {remoteEndPoint}, {data.Length} bytes");
                System.Diagnostics.Debug.WriteLine($"mDNS: Response hex: {BitConverter.ToString(data.Take(Math.Min(64, data.Length)).ToArray())}");

                var discoveredDevices = _parser.ParseResponse(data, remoteEndPoint);

                System.Diagnostics.Debug.WriteLine($"mDNS: Parsed {discoveredDevices.Count} devices from response");

                foreach (var device in discoveredDevices)
                {
                    var key = device.IPAddress?.ToString() ?? device.UniqueId;

                    if (devices.TryAdd(key, device))
                    {
                        // New device - enhance and notify
                        System.Diagnostics.Debug.WriteLine($"mDNS: New device discovered: {device.Name} ({device.IPAddress})");
                        await EnhanceDeviceAsync(device);
                        DeviceDiscovered?.Invoke(this, new DeviceDiscoveredEventArgs(device, "mDNS"));
                    }
                    else if (devices.TryGetValue(key, out var existing))
                    {
                        // Update existing device
                        lock (existing)
                        {
                            existing.UpdateFrom(device);
                            existing.LastSeen = DateTime.UtcNow;
                        }
                        System.Diagnostics.Debug.WriteLine($"mDNS: Updated existing device: {device.Name} ({device.IPAddress})");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing mDNS response: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Response data: {BitConverter.ToString(data.Take(32).ToArray())}");
            }
        }

        private async Task EnhanceDeviceAsync(DiscoveredDevice device)
        {
            try
            {
                if (device.IPAddress != null && device.Name.StartsWith("Device-"))
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    var hostname = await wpfhikip.Discovery.Core.NetworkUtils.GetHostnameAsync(device.IPAddress);
                    if (!string.IsNullOrEmpty(hostname))
                    {
                        device.Name = hostname;
                        device.DiscoveryData["ResolvedHostname"] = hostname;
                    }
                }
            }
            catch
            {
                // Enhancement failed, but that's okay
            }
        }
    }
}