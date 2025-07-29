namespace wpfhikip.Discovery.Core
{
    /// <summary>
    /// Event arguments for when a device is discovered
    /// </summary>
    public class DeviceDiscoveredEventArgs : EventArgs
    {
        public DiscoveredDevice Device { get; }
        public string DiscoveryMethod { get; }
        public DateTime Timestamp { get; }

        public DeviceDiscoveredEventArgs(DiscoveredDevice device, string discoveryMethod = "")
        {
            Device = device;
            DiscoveryMethod = discoveryMethod;
            Timestamp = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Event arguments for discovery progress updates
    /// </summary>
    public class DiscoveryProgressEventArgs : EventArgs
    {
        public string DiscoveryMethod { get; }
        public int Progress { get; }
        public int Total { get; }
        public string CurrentTarget { get; }
        public string Status { get; }

        public DiscoveryProgressEventArgs(string discoveryMethod, int progress, int total, string currentTarget = "", string status = "")
        {
            DiscoveryMethod = discoveryMethod;
            Progress = progress;
            Total = total;
            CurrentTarget = currentTarget;
            Status = status;
        }

        public double ProgressPercentage => Total > 0 ? (double)Progress / Total * 100 : 0;
    }

    /// <summary>
    /// Event arguments for discovery errors
    /// </summary>
    public class DiscoveryErrorEventArgs : EventArgs
    {
        public string DiscoveryMethod { get; }
        public Exception Exception { get; }
        public string ErrorMessage { get; }
        public DateTime Timestamp { get; }

        public DiscoveryErrorEventArgs(string discoveryMethod, Exception exception)
        {
            DiscoveryMethod = discoveryMethod;
            Exception = exception;
            ErrorMessage = exception.Message;
            Timestamp = DateTime.UtcNow;
        }

        public DiscoveryErrorEventArgs(string discoveryMethod, string errorMessage)
        {
            DiscoveryMethod = discoveryMethod;
            ErrorMessage = errorMessage;
            Exception = new Exception(errorMessage);
            Timestamp = DateTime.UtcNow;
        }
    }
}