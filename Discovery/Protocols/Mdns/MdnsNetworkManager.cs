using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace wpfhikip.Discovery.Protocols.Mdns
{
    /// <summary>
    /// mDNS network interface management - local queries only, listen to all responses
    /// </summary>
    internal class MdnsNetworkManager : IDisposable
    {
        private readonly List<UdpClient> _sendingClients = new();
        private readonly List<UdpClient> _listeningClients = new();
        private readonly Dictionary<int, NetworkInterface> _activeInterfaces = new();
        private readonly object _lock = new();
        private volatile bool _disposed;

        public IReadOnlyList<NetworkInterface> ActiveInterfaces => _activeInterfaces.Values.ToList();

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (_disposed) return;

                Cleanup();
                _activeInterfaces.Clear();
            }

            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(IsValidInterface)
                .ToList();

            System.Diagnostics.Debug.WriteLine($"mDNS: Found {interfaces.Count} valid network interfaces");

            foreach (var networkInterface in interfaces)
            {
                try
                {
                    await SetupInterface(networkInterface, cancellationToken);
                    _activeInterfaces[networkInterface.GetHashCode()] = networkInterface;
                    System.Diagnostics.Debug.WriteLine($"mDNS: Initialized interface {networkInterface.Name}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"mDNS: Failed to setup interface {networkInterface.Name}: {ex.Message}");
                }
            }

            System.Diagnostics.Debug.WriteLine($"mDNS: {_sendingClients.Count} sending clients, {_listeningClients.Count} listening clients (local queries only)");
        }

        public async Task ReinitializeAsync()
        {
            await InitializeAsync();
        }

        private static bool IsValidInterface(NetworkInterface networkInterface)
        {
            // Accept more interface types to ensure broader listening coverage
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
                return false;

            // Skip only loopback interfaces - allow tunnels and others for listening
            if (networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                return false;

            // Must have IPv4 addresses
            var properties = networkInterface.GetIPProperties();
            return properties.UnicastAddresses.Any(addr =>
                addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(addr.Address));
        }

        private async Task SetupInterface(NetworkInterface networkInterface, CancellationToken cancellationToken)
        {
            var properties = networkInterface.GetIPProperties();
            var ipv4Addresses = properties.UnicastAddresses
                .Where(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(addr => addr.Address)
                .ToList();

            foreach (var localAddress in ipv4Addresses)
            {
                try
                {
                    // Create sending client for LOCAL subnet queries only
                    var sendingClient = CreateLocalSendingClient(localAddress);
                    if (sendingClient != null)
                    {
                        _sendingClients.Add(sendingClient);
                    }

                    // Create listening client for ALL responses (including cross-subnet)
                    var listeningClient = CreateGlobalListeningClient(localAddress);
                    if (listeningClient != null)
                    {
                        _listeningClients.Add(listeningClient);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"mDNS: Error setting up {localAddress} on {networkInterface.Name}: {ex.Message}");
                }
            }
        }

        private UdpClient? CreateLocalSendingClient(IPAddress localAddress)
        {
            try
            {
                var client = new UdpClient(new IPEndPoint(localAddress, 0));

                // Standard multicast configuration for LOCAL subnet only
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                // Use standard TTL (1) to limit to local subnet
                client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 1);
                client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, localAddress.GetAddressBytes());

                // Enable multicast loopback for local reception
                client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastLoopback, true);

                return client;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"mDNS: Failed to create local sending client for {localAddress}: {ex.Message}");
                return null;
            }
        }

        private UdpClient? CreateGlobalListeningClient(IPAddress localAddress)
        {
            try
            {
                var client = new UdpClient();
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                // Bind to ANY to receive from ALL sources (including cross-subnet)
                client.Client.Bind(new IPEndPoint(IPAddress.Any, MdnsConstants.MulticastPort));

                // Join multicast group for this interface
                var multicastAddress = IPAddress.Parse(MdnsConstants.MulticastAddress);
                client.JoinMulticastGroup(multicastAddress, localAddress);

                return client;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"mDNS: Failed to create global listening client for {localAddress}: {ex.Message}");
                return null;
            }
        }

        public async Task SendQueryAsync(string[] services, CancellationToken cancellationToken)
        {
            if (_disposed || !_sendingClients.Any()) return;

            var query = MdnsMessage.CreateQuery(services);
            var queryBytes = query.ToByteArray();
            var multicastEndpoint = new IPEndPoint(IPAddress.Parse(MdnsConstants.MulticastAddress), MdnsConstants.MulticastPort);

            // Send multicast queries to LOCAL subnet only
            var sendTasks = _sendingClients.Select(async client =>
            {
                try
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

                    await client.SendAsync(queryBytes, multicastEndpoint).AsTask().WaitAsync(timeoutCts.Token);
                }
                catch (ObjectDisposedException)
                {
                    // Client disposed - ignore
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"mDNS: Local send error: {ex.Message}");
                }
            });

            await Task.WhenAll(sendTasks);
            System.Diagnostics.Debug.WriteLine($"mDNS: Sent local subnet queries only (TTL=1)");
        }

        public async Task SendUnicastQueryAsync(string[] services, IPAddress target, CancellationToken cancellationToken)
        {
            // Only allow unicast queries to LOCAL subnet addresses
            if (!IsLocalSubnet(target))
            {
                System.Diagnostics.Debug.WriteLine($"mDNS: Skipping cross-subnet unicast query to {target}");
                return;
            }

            var query = MdnsMessage.CreateQuery(services);
            var queryBytes = query.ToByteArray();
            var endpoint = new IPEndPoint(target, MdnsConstants.MulticastPort);

            foreach (var client in _sendingClients)
            {
                try
                {
                    await client.SendAsync(queryBytes, endpoint);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"mDNS: Local unicast send error to {target}: {ex.Message}");
                }
            }
        }

        private bool IsLocalSubnet(IPAddress targetAddress)
        {
            try
            {
                foreach (var networkInterface in _activeInterfaces.Values)
                {
                    var properties = networkInterface.GetIPProperties();
                    foreach (var unicast in properties.UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            var localNetwork = GetNetworkAddress(unicast.Address, unicast.IPv4Mask);
                            var targetNetwork = GetNetworkAddress(targetAddress, unicast.IPv4Mask);

                            if (localNetwork != null && targetNetwork != null && localNetwork.Equals(targetNetwork))
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking if {targetAddress} is local subnet: {ex.Message}");
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

        public async Task ListenForResponsesAsync(NetworkInterface networkInterface, Action<byte[], IPEndPoint> onResponse, CancellationToken cancellationToken)
        {
            await ListenForResponsesAsync(networkInterface, onResponse, TimeSpan.FromMinutes(5), cancellationToken);
        }

        public async Task ListenForResponsesAsync(NetworkInterface networkInterface, Action<byte[], IPEndPoint> onResponse, TimeSpan listenDuration, CancellationToken cancellationToken)
        {
            // Get all listening clients (not interface-specific since we bind to ANY)
            var relevantClients = _listeningClients.ToList();

            if (!relevantClients.Any())
            {
                System.Diagnostics.Debug.WriteLine($"mDNS: No listening clients available for {networkInterface.Name}");
                return;
            }

            // Create a timeout cancellation token that combines the provided cancellation token with the listen duration
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(listenDuration);

            var listenTasks = relevantClients.Select(client => ListenOnClientAsync(client, onResponse, timeoutCts.Token)).ToList();

            try
            {
                System.Diagnostics.Debug.WriteLine($"mDNS: Starting to listen for responses for {listenDuration.TotalMinutes:F1} minutes on {relevantClients.Count} clients");
                await Task.WhenAny(listenTasks);
            }
            catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                System.Diagnostics.Debug.WriteLine($"mDNS: Listen duration of {listenDuration.TotalMinutes:F1} minutes completed");
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("mDNS: Listening cancelled by user");
            }
        }

        private async Task ListenOnClientAsync(UdpClient client, Action<byte[], IPEndPoint> onResponse, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && !_disposed)
                {
                    var result = await client.ReceiveAsync().WaitAsync(cancellationToken);

                    // Log all received responses for debugging
                    System.Diagnostics.Debug.WriteLine($"mDNS: Received response from {result.RemoteEndPoint.Address} ({result.Buffer.Length} bytes)");

                    // Log cross-subnet responses for debugging
                    if (!IsLocalSubnet(result.RemoteEndPoint.Address))
                    {
                        System.Diagnostics.Debug.WriteLine($"mDNS: Processing cross-subnet response from {result.RemoteEndPoint.Address}");
                    }

                    onResponse(result.Buffer, result.RemoteEndPoint);
                }
            }
            catch (ObjectDisposedException)
            {
                // Client disposed - ignore
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"mDNS: Listen error: {ex.Message}");
            }
        }

        private static bool IsClientForInterface(UdpClient client, NetworkInterface networkInterface)
        {
            try
            {
                if (client.Client.LocalEndPoint is IPEndPoint localEP)
                {
                    var properties = networkInterface.GetIPProperties();
                    return properties.UnicastAddresses.Any(addr => addr.Address.Equals(localEP.Address));
                }
            }
            catch
            {
                // Ignore errors
            }
            return false;
        }

        public void StopListening()
        {
            lock (_lock)
            {
                Cleanup();
            }
        }

        private void Cleanup()
        {
            foreach (var client in _sendingClients.Concat(_listeningClients))
            {
                try
                {
                    client?.Close();
                    client?.Dispose();
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }

            _sendingClients.Clear();
            _listeningClients.Clear();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            lock (_lock)
            {
                Cleanup();
                _activeInterfaces.Clear();
            }
        }
    }
}