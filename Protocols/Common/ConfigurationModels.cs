namespace wpfhikip.Protocols.Common
{
    /// <summary>
    /// Network configuration data for camera protocols
    /// </summary>
    public sealed record NetworkConfiguration
    {
        public string? IPAddress { get; init; }
        public string? SubnetMask { get; init; }
        public string? DefaultGateway { get; init; }
        public string? DNS1 { get; init; }
        public string? DNS2 { get; init; }

        public bool IsValid => !string.IsNullOrWhiteSpace(IPAddress);
    }

    /// <summary>
    /// NTP configuration data for camera protocols
    /// </summary>
    public sealed record NTPConfiguration
    {
        public string NTPServer { get; init; } = string.Empty;
        public string? TimeZone { get; init; }
        public bool EnableNTP { get; init; } = true;

        public bool IsValid => !string.IsNullOrWhiteSpace(NTPServer);
    }
}