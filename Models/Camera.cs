using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;

namespace wpfhikip.Models
{
    public class Camera : BaseNotifyPropertyChanged
    {
        private bool _isSelected;
        private string? _manufacturer;
        private string? _model;
        private string? _firmware;
        private string? _serialNumber;
        private string? _macAddress;
        private string? _status;
        private string? _onlineStatus;
        private Brush? _rowColor;
        private Brush? _cellColor;
        private FontWeight _cellFontWeight;
        private bool _isCompleted;
        private CameraProtocol _protocol;
        private bool _isCompatible;
        private bool _isAuthenticated;
        private bool _requiresAuthentication;

        private readonly ConcurrentQueue<ProtocolLogEntry> _protocolLogs = new();

        // Basic properties
        public CameraProtocol Protocol
        {
            get => _protocol;
            set => SetProperty(ref _protocol, value);
        }
        public string? Manufacturer
        {
            get => _manufacturer;
            set => SetProperty(ref _manufacturer, value);
        }

        public string? Model
        {
            get => _model;
            set => SetProperty(ref _model, value);
        }

        public string? Firmware
        {
            get => _firmware;
            set => SetProperty(ref _firmware, value);
        }

        public string? SerialNumber
        {
            get => _serialNumber;
            set => SetProperty(ref _serialNumber, value);
        }

        public string? MacAddress
        {
            get => _macAddress;
            set => SetProperty(ref _macAddress, value);
        }


        /// <summary>
        /// Indicates if the camera is compatible with any protocol
        /// </summary>
        public bool IsCompatible
        {
            get => _isCompatible;
            set
            {
                if (SetProperty(ref _isCompatible, value))
                    OnPropertyChanged(nameof(CanShowCameraInfo));
            }
        }

        /// <summary>
        /// Indicates if the camera requires authentication
        /// </summary>
        public bool RequiresAuthentication
        {
            get => _requiresAuthentication;
            set
            {
                if (SetProperty(ref _requiresAuthentication, value))
                    OnPropertyChanged(nameof(CanShowCameraInfo));
            }
        }

        /// <summary>
        /// Indicates if authentication was successful (only relevant if RequiresAuthentication is true)
        /// </summary>
        public bool IsAuthenticated
        {
            get => _isAuthenticated;
            set
            {
                if (SetProperty(ref _isAuthenticated, value))
                    OnPropertyChanged(nameof(CanShowCameraInfo));
            }
        }

        /// <summary>
        /// Computed property to determine if camera info button should be enabled
        /// </summary>
        public bool CanShowCameraInfo => IsCompatible && (!RequiresAuthentication || IsAuthenticated);
        // UI-related properties
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public string? Status
        {
            get => _status;
            set
            {
                if (SetProperty(ref _status, value))
                    OnPropertyChanged(nameof(ShortStatus));
            }
        }

        // Optimized computed property
        public string? ShortStatus => string.IsNullOrEmpty(_status)
            ? _status
            : _status.Length > 10 ? string.Concat(_status.AsSpan(0, 7), "...") : _status;



        public Brush? CellColor
        {
            get => _cellColor;
            set => SetProperty(ref _cellColor, value);
        }


        public bool IsCompleted
        {
            get => _isCompleted;
            set => SetProperty(ref _isCompleted, value);
        }

        // Navigation properties
        public CameraConnection Connection { get; set; } = new();
        public CameraSettings Settings { get; set; } = new();
        public CameraVideoStream VideoStream { get; set; } = new();

        // Protocol logs
        public ConcurrentQueue<ProtocolLogEntry> ProtocolLogs => _protocolLogs;

        // Helper properties with optimized implementations
        public string? CurrentIP
        {
            get => Connection.IPAddress;
            set
            {
                if (!string.Equals(Connection.IPAddress, value, StringComparison.Ordinal))
                {
                    Connection.IPAddress = value;
                    OnPropertyChanged();
                }
            }
        }

        public string? Port
        {
            get => Connection.Port;
            set
            {
                if (!string.Equals(Connection.Port, value, StringComparison.Ordinal))
                {
                    Connection.Port = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(EffectivePort));
                }
            }
        }

        public string? Username
        {
            get => Connection.Username;
            set
            {
                if (!string.Equals(Connection.Username, value, StringComparison.Ordinal))
                {
                    Connection.Username = value;
                    OnPropertyChanged();
                }
            }
        }

        public string? Password
        {
            get => Connection.Password;
            set
            {
                if (!string.Equals(Connection.Password, value, StringComparison.Ordinal))
                {
                    Connection.Password = value;
                    OnPropertyChanged();
                }
            }
        }

        // Settings helper properties
        public string? NewIP
        {
            get => Settings.IPAddress;
            set
            {
                if (!string.Equals(Settings.IPAddress, value, StringComparison.Ordinal))
                {
                    Settings.IPAddress = value;
                    OnPropertyChanged();
                }
            }
        }

        public string? NewMask
        {
            get => Settings.SubnetMask;
            set
            {
                if (!string.Equals(Settings.SubnetMask, value, StringComparison.Ordinal))
                {
                    Settings.SubnetMask = value;
                    OnPropertyChanged();
                }
            }
        }

        public string? NewGateway
        {
            get => Settings.DefaultGateway;
            set
            {
                if (!string.Equals(Settings.DefaultGateway, value, StringComparison.Ordinal))
                {
                    Settings.DefaultGateway = value;
                    OnPropertyChanged();
                }
            }
        }

        public string? NewDNS1
        {
            get => Settings.DNS1;
            set
            {
                if (!string.Equals(Settings.DNS1, value, StringComparison.Ordinal))
                {
                    Settings.DNS1 = value;
                    OnPropertyChanged();
                }
            }
        }

        public string? NewDNS2
        {
            get => Settings.DNS2;
            set
            {
                if (!string.Equals(Settings.DNS2, value, StringComparison.Ordinal))
                {
                    Settings.DNS2 = value;
                    OnPropertyChanged();
                }
            }
        }

        public string? NewNTPServer
        {
            get => Settings.NTPServer;
            set
            {
                if (!string.Equals(Settings.NTPServer, value, StringComparison.Ordinal))
                {
                    Settings.NTPServer = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets the effective port to use for connections.
        /// </summary>
        public int EffectivePort
        {
            get
            {
                if (!string.IsNullOrEmpty(Port) &&
                    int.TryParse(Port, out int port) &&
                    port is > 0 and <= 65535)
                {
                    return port;
                }
                return ProtocolDefaults.GetDefaultPort(Protocol);
            }
        }

        /// <summary>
        /// Adds a protocol log entry for real-time monitoring
        /// </summary>
        public void AddProtocolLog(string protocol, string step, string details, ProtocolLogLevel level = ProtocolLogLevel.Info)
        {
            var logEntry = new ProtocolLogEntry
            {
                Timestamp = DateTime.Now,
                Protocol = protocol,
                Step = step,
                Details = details,
                Level = level,
                IpAddress = CurrentIP ?? "N/A",
                Port = EffectivePort
            };

            _protocolLogs.Enqueue(logEntry);

            // Keep only last 100 entries to prevent memory issues
            while (_protocolLogs.Count > 100)
            {
                _protocolLogs.TryDequeue(out _);
            }

            OnPropertyChanged(nameof(ProtocolLogs));
        }

        /// <summary>
        /// Clears all protocol logs
        /// </summary>
        public void ClearProtocolLogs()
        {
            while (_protocolLogs.TryDequeue(out _)) { }
            OnPropertyChanged(nameof(ProtocolLogs));
        }
    }

    /// <summary>
    /// Represents a single protocol checking log entry
    /// </summary>
    public sealed record ProtocolLogEntry
    {
        public DateTime Timestamp { get; init; }
        public string Protocol { get; init; } = string.Empty;
        public string Step { get; init; } = string.Empty;
        public string Details { get; init; } = string.Empty;
        public ProtocolLogLevel Level { get; init; }
        public string IpAddress { get; init; } = string.Empty;
        public int Port { get; init; }

        public string FormattedTimestamp => Timestamp.ToString("HH:mm:ss.fff");

        public string LogIcon => Level switch
        {
            ProtocolLogLevel.Info => "→",
            ProtocolLogLevel.Success => "✓",
            ProtocolLogLevel.Warning => "⚠",
            ProtocolLogLevel.Error => "✗",
            _ => "•"
        };
    }
}