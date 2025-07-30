using System.Net;
using System.Net.Sockets;

namespace wpfhikip.Discovery.Protocols.Mdns
{
    /// <summary>
    /// Properly configured mDNS network management with improved disposal
    /// </summary>
    internal class MdnsNetworkManager : IDisposable
    {
        private readonly List<UdpClient> _clients = new();
        private readonly List<UdpClient> _listeners = new();
        private readonly object _disposeLock = new();
        private volatile bool _disposed;

        public IReadOnlyList<UdpClient> Clients => _clients.AsReadOnly();
        public IReadOnlyList<UdpClient> Listeners => _listeners.AsReadOnly();

        /// <summary>
        /// Initialize UDP clients for mDNS with proper multicast setup
        /// </summary>
        public async Task InitializeAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MdnsNetworkManager));

            await Task.Run(() =>
            {
                // Create sending clients (bound to any port)
                CreateSendingClients();

                // Create listening clients (bound to mDNS port 5353)
                CreateListeningClients();

                System.Diagnostics.Debug.WriteLine($"mDNS: Created {_clients.Count} sending clients and {_listeners.Count} listening clients");
            }, cancellationToken);
        }

        private void CreateSendingClients()
        {
            try
            {
                // Primary sending client
                var client = new UdpClient();
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                client.Client.Bind(new IPEndPoint(IPAddress.Any, 0)); // Any available port for sending

                // Enable multicast
                var multicastAddr = IPAddress.Parse(MdnsConstants.MulticastAddress);
                client.JoinMulticastGroup(multicastAddr);
                client.MulticastLoopback = false; // Don't receive our own packets

                _clients.Add(client);
                System.Diagnostics.Debug.WriteLine($"mDNS: Created primary sending client on port {((IPEndPoint)client.Client.LocalEndPoint!).Port}");

                // Create interface-specific sending clients
                var interfaces = wpfhikip.Discovery.Core.NetworkUtils.GetLocalNetworkInterfaces();
                foreach (var interfaceInfo in interfaces.Values.Take(2)) // Limit to 2 additional interfaces
                {
                    foreach (var address in interfaceInfo.IPv4Addresses.Take(1))
                    {
                        CreateInterfaceSendingClient(address.IPAddress);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create sending clients: {ex.Message}");
            }
        }

        private void CreateListeningClients()
        {
            try
            {
                // Primary listening client bound to mDNS port
                var listener = new UdpClient();
                listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                listener.Client.Bind(new IPEndPoint(IPAddress.Any, MdnsConstants.MulticastPort));

                // Join multicast group
                var multicastAddr = IPAddress.Parse(MdnsConstants.MulticastAddress);
                listener.JoinMulticastGroup(multicastAddr);

                _listeners.Add(listener);
                System.Diagnostics.Debug.WriteLine($"mDNS: Created primary listening client on port {MdnsConstants.MulticastPort}");

                // Create interface-specific listeners
                var interfaces = wpfhikip.Discovery.Core.NetworkUtils.GetLocalNetworkInterfaces();
                foreach (var interfaceInfo in interfaces.Values.Take(2))
                {
                    foreach (var address in interfaceInfo.IPv4Addresses.Take(1))
                    {
                        CreateInterfaceListeningClient(address.IPAddress);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create listening clients: {ex.Message}");
            }
        }

        private void CreateInterfaceSendingClient(IPAddress localAddress)
        {
            try
            {
                var client = new UdpClient();
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                client.Client.Bind(new IPEndPoint(localAddress, 0)); // Any available port

                var multicastAddr = IPAddress.Parse(MdnsConstants.MulticastAddress);
                client.JoinMulticastGroup(multicastAddr, localAddress);
                client.MulticastLoopback = false;

                _clients.Add(client);
                System.Diagnostics.Debug.WriteLine($"mDNS: Created interface sending client for {localAddress}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create interface sending client for {localAddress}: {ex.Message}");
            }
        }

        private void CreateInterfaceListeningClient(IPAddress localAddress)
        {
            try
            {
                var listener = new UdpClient();
                listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                listener.Client.Bind(new IPEndPoint(localAddress, MdnsConstants.MulticastPort));

                var multicastAddr = IPAddress.Parse(MdnsConstants.MulticastAddress);
                listener.JoinMulticastGroup(multicastAddr, localAddress);

                _listeners.Add(listener);
                System.Diagnostics.Debug.WriteLine($"mDNS: Created interface listening client for {localAddress}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create interface listening client for {localAddress}: {ex.Message}");
            }
        }

        public void Dispose()
        {
            lock (_disposeLock)
            {
                if (_disposed) return;
                _disposed = true;

                System.Diagnostics.Debug.WriteLine("mDNS: Starting graceful disposal of network resources");

                // Dispose clients safely
                DisposeClients(_clients, "sending");
                DisposeClients(_listeners, "listening");

                _clients.Clear();
                _listeners.Clear();

                System.Diagnostics.Debug.WriteLine("mDNS: Network resource disposal complete");
            }
        }

        private static void DisposeClients(List<UdpClient> clients, string type)
        {
            foreach (var client in clients)
            {
                try
                {
                    // First try to close gracefully
                    client.Close();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Warning: Error closing {type} client: {ex.Message}");
                }

                try
                {
                    // Then dispose
                    client.Dispose();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Warning: Error disposing {type} client: {ex.Message}");
                }
            }
        }

        public bool IsDisposed => _disposed;
    }
}