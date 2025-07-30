using System.Net.Sockets;
using System.Net;

namespace wpfhikip.Discovery.Protocols.Mdns
{
    /// <summary>
    /// mDNS query engine - local subnet queries only
    /// </summary>
    internal class MdnsQueryEngine
    {
        private readonly Random _random = new();

        public async Task SendQueriesAsync(MdnsNetworkManager networkManager, string[] services, CancellationToken cancellationToken)
        {
            if (services.Length == 0) return;

            // RFC 6762: Use randomized timing to avoid synchronized queries
            var initialDelay = _random.Next(20, 120); // 20-120ms random delay
            await Task.Delay(initialDelay, cancellationToken);

            // Split services into batches to avoid oversized packets
            var batches = CreateServiceBatches(services, 10);

            foreach (var batch in batches)
            {
                try
                {
                    await networkManager.SendQueryAsync(batch, cancellationToken);
                    System.Diagnostics.Debug.WriteLine($"mDNS: Sent LOCAL query batch with {batch.Length} services");

                    // Small delay between batches
                    if (batch != batches.Last())
                    {
                        await Task.Delay(100, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"mDNS: Local batch send error: {ex.Message}");
                }
            }
        }

        public async Task SendFinalSweepAsync(MdnsNetworkManager networkManager, CancellationToken cancellationToken)
        {
            try
            {
                // Send broad service enumeration queries to LOCAL subnet only
                var sweepServices = new[]
                {
                    "_services._dns-sd._udp.local.",
                    "_tcp.local.",
                    "_udp.local."
                };

                await networkManager.SendQueryAsync(sweepServices, cancellationToken);
                System.Diagnostics.Debug.WriteLine("mDNS: Sent final LOCAL enumeration sweep");

                // Wait a bit then send targeted follow-ups
                await Task.Delay(1000, cancellationToken);

                // Send queries for common undetected services to LOCAL subnet only
                var followUpServices = new[]
                {
                    "_device-info._tcp.local.",
                    "_workstation._tcp.local.",
                    "_companion-link._tcp.local.",
                    "_sleep-proxy._udp.local."
                };

                await networkManager.SendQueryAsync(followUpServices, cancellationToken);
                System.Diagnostics.Debug.WriteLine("mDNS: Sent LOCAL follow-up queries");
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"mDNS: Local final sweep error: {ex.Message}");
            }
        }

        private static string[][] CreateServiceBatches(string[] services, int maxBatchSize)
        {
            var batches = new List<string[]>();

            for (int i = 0; i < services.Length; i += maxBatchSize)
            {
                var batchSize = Math.Min(maxBatchSize, services.Length - i);
                var batch = new string[batchSize];
                Array.Copy(services, i, batch, 0, batchSize);
                batches.Add(batch);
            }

            return batches.ToArray();
        }
    }
}