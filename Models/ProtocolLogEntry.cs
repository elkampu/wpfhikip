namespace wpfhikip.Models
{
    /// <summary>
    /// Represents a protocol log entry for camera operations
    /// </summary>
    public class ProtocolLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Protocol { get; set; } = string.Empty;
        public string Step { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public ProtocolLogLevel Level { get; set; }
        public string IpAddress { get; set; } = string.Empty;
        public int Port { get; set; }
    }
}