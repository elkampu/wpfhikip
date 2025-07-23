using System;
using System.Collections.Generic;

using wpfhikip.Discovery.Models;

namespace wpfhikip.Discovery.Core
{
    /// <summary>
    /// Represents the result of a network discovery operation
    /// </summary>
    public class DiscoveryResult
    {
        /// <summary>
        /// Whether the discovery operation was successful
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Devices discovered during the operation
        /// </summary>
        public List<DiscoveredDevice> Devices { get; set; } = new();

        /// <summary>
        /// Discovery method used
        /// </summary>
        public DiscoveryMethod Method { get; set; }

        /// <summary>
        /// Time when discovery started
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// Time when discovery completed
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// Duration of the discovery operation
        /// </summary>
        public TimeSpan Duration => EndTime - StartTime;

        /// <summary>
        /// Network segment that was scanned
        /// </summary>
        public string? NetworkSegment { get; set; }

        /// <summary>
        /// Error message if discovery failed
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Exception that occurred during discovery, if any
        /// </summary>
        public Exception? Exception { get; set; }

        /// <summary>
        /// Additional metadata about the discovery operation
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new();

        /// <summary>
        /// Number of devices discovered
        /// </summary>
        public int DeviceCount => Devices.Count;

        /// <summary>
        /// Creates a successful discovery result
        /// </summary>
        public static DiscoveryResult CreateSuccess(DiscoveryMethod method, IEnumerable<DiscoveredDevice> devices, DateTime startTime)
        {
            return new DiscoveryResult
            {
                Success = true,
                Method = method,
                Devices = new List<DiscoveredDevice>(devices),
                StartTime = startTime,
                EndTime = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Creates a failed discovery result
        /// </summary>
        public static DiscoveryResult CreateFailure(DiscoveryMethod method, string errorMessage, DateTime startTime, Exception? exception = null)
        {
            return new DiscoveryResult
            {
                Success = false,
                Method = method,
                ErrorMessage = errorMessage,
                Exception = exception,
                StartTime = startTime,
                EndTime = DateTime.UtcNow
            };
        }

        public override string ToString()
        {
            return Success
                ? $"{Method}: {DeviceCount} devices found in {Duration.TotalSeconds:F1}s"
                : $"{Method}: Failed - {ErrorMessage}";
        }
    }
}