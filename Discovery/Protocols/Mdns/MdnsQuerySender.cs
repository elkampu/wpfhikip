using System.Net;
using System.Net.Sockets;

namespace wpfhikip.Discovery.Protocols.Mdns
{
    /// <summary>
    /// Comprehensive mDNS query sender with adaptive strategy
    /// </summary>
    internal class MdnsQuerySender
    {
        private static readonly IPEndPoint MulticastEndpoint =
            new(IPAddress.Parse(MdnsConstants.MulticastAddress), MdnsConstants.MulticastPort);

        /// <summary>
        /// Send comprehensive discovery queries using phased approach
        /// </summary>
        public async Task SendDiscoveryAsync(IEnumerable<UdpClient> clients, CancellationToken cancellationToken)
        {
            var clientList = clients.ToList();
            if (!clientList.Any()) return;

            var primaryClient = clientList.First();

            try
            {
                // Get prioritized service phases
                var servicePhases = MdnsConstants.GetServicesByPriority();

                // Phase timing configuration
                var phaseDelays = new[]
                {
                    50,   // Core services - immediate
                    100,  // Security services - high priority
                    150,  // Network services
                    200,  // Storage services
                    250,  // Media services
                    300,  // Printer services
                    350,  // Industrial services
                    400,  // Communication services
                    450,  // Development services
                    500,  // Gaming services
                    600   // Generic services - lowest priority
                };

                // Send each phase with increasing delays
                for (int phase = 0; phase < servicePhases.Length && !cancellationToken.IsCancellationRequested; phase++)
                {
                    var services = servicePhases[phase];
                    var delay = phase < phaseDelays.Length ? phaseDelays[phase] : 600;

                    System.Diagnostics.Debug.WriteLine($"mDNS Phase {phase + 1}: Sending {services.Length} service queries");

                    await SendBatchAsync(primaryClient, services, delay, cancellationToken);

                    // Brief pause between phases to avoid overwhelming the network
                    if (phase < servicePhases.Length - 1)
                    {
                        await Task.Delay(50, cancellationToken);
                    }
                }

                // Final comprehensive sweep with reduced batch size
                System.Diagnostics.Debug.WriteLine("mDNS: Starting final comprehensive sweep");
                await SendFinalSweepAsync(primaryClient, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during cancellation
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in mDNS discovery: {ex.Message}");
            }
        }

        private async Task SendBatchAsync(UdpClient client, string[] services, int delayMs, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested || !services.Any()) return;

            try
            {
                // Adaptive batch sizing based on service count
                int batchSize = services.Length switch
                {
                    <= 10 => 3,      // Small sets - smaller batches
                    <= 20 => 4,      // Medium sets - standard batches
                    <= 30 => 5,      // Large sets - larger batches
                    _ => 6           // Very large sets - maximum batch size
                };

                for (int i = 0; i < services.Length; i += batchSize)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    var batch = services.Skip(i).Take(batchSize).ToArray();
                    await SendQueryBatchAsync(client, batch, cancellationToken);

                    // Progressive delay reduction for later batches
                    if (i + batchSize < services.Length)
                    {
                        var adjustedDelay = Math.Max(delayMs - (i / batchSize * 10), 25);
                        await Task.Delay(adjustedDelay, cancellationToken);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error sending batch: {ex.Message}");
            }
        }

        private async Task SendFinalSweepAsync(UdpClient client, CancellationToken cancellationToken)
        {
            try
            {
                // Send a few broad queries to catch any remaining devices
                var broadQueries = new[]
                {
                    "_services._dns-sd._udp.local.",
                    "_tcp.local.",
                    "_udp.local."
                };

                foreach (var query in broadQueries)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    await SendQueryBatchAsync(client, new[] { query }, cancellationToken);
                    await Task.Delay(200, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in final sweep: {ex.Message}");
            }
        }

        private async Task SendQueryBatchAsync(UdpClient client, string[] services, CancellationToken cancellationToken)
        {
            try
            {
                var query = MdnsMessage.CreateQuery(services);
                var queryBytes = query.ToByteArray();

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                using var combined = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

                // Convert ValueTask to Task before using WaitAsync
                await client.SendAsync(queryBytes, MulticastEndpoint).AsTask().WaitAsync(combined.Token);

                // Debug output for monitoring
                System.Diagnostics.Debug.WriteLine($"Sent mDNS query batch: {string.Join(", ", services.Take(3))}{(services.Length > 3 ? "..." : "")}");
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to send query batch: {ex.Message}");
            }
        }
    }
}