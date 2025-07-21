using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;

namespace wpfhikip.Models
{
    public class Camera : INotifyPropertyChanged
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
        private readonly ConcurrentQueue<ProtocolLogEntry> _protocolLogs = new();

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

        public string? MACAddress
        {
            get => _macAddress;
            set => SetProperty(ref _macAddress, value);
        }

        // UI-related properties for DataGrid binding
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
                SetProperty(ref _status, value);
                OnPropertyChanged(nameof(ShortStatus));
            }
        }

        public string? ShortStatus
        {
            get
            {
                if (string.IsNullOrEmpty(_status))
                    return _status;

                return _status.Length > 10 ? _status.Substring(0, 7) + "..." : _status;
            }
        }

        public string? OnlineStatus
        {
            get => _onlineStatus;
            set => SetProperty(ref _onlineStatus, value);
        }

        public Brush? RowColor
        {
            get => _rowColor;
            set => SetProperty(ref _rowColor, value);
        }

        public Brush? CellColor
        {
            get => _cellColor;
            set => SetProperty(ref _cellColor, value);
        }

        public FontWeight CellFontWeight
        {
            get => _cellFontWeight;
            set => SetProperty(ref _cellFontWeight, value);
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

        // Protocol logs for real-time monitoring
        public ConcurrentQueue<ProtocolLogEntry> ProtocolLogs => _protocolLogs;

        // Helper properties for easier access to connection settings
        public string? CurrentIP
        {
            get => Connection.IPAddress;
            set
            {
                if (Connection.IPAddress != value)
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
                if (Connection.Port != value)
                {
                    Connection.Port = value;
                    OnPropertyChanged();
                }
            }
        }

        public string? Username
        {
            get => Connection.Username;
            set
            {
                if (Connection.Username != value)
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
                if (Connection.Password != value)
                {
                    Connection.Password = value;
                    OnPropertyChanged();
                }
            }
        }

        // Helper properties for easier access to settings
        public string? NewIP
        {
            get => Settings.IPAddress;
            set
            {
                if (Settings.IPAddress != value)
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
                if (Settings.SubnetMask != value)
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
                if (Settings.DefaultGateway != value)
                {
                    Settings.DefaultGateway = value;
                    OnPropertyChanged();
                }
            }
        }

        public string? NewNTPServer
        {
            get => Settings.NTPServer;
            set
            {
                if (Settings.NTPServer != value)
                {
                    Settings.NTPServer = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Gets the effective port to use for connections.
        /// Returns the custom port if specified, otherwise returns the default port for the protocol.
        /// </summary>
        public int EffectivePort
        {
            get
            {
                // If custom port is specified and valid, use it
                if (!string.IsNullOrEmpty(Port) && int.TryParse(Port, out int port) && port > 0 && port <= 65535)
                {
                    return port;
                }

                // Return default port based on protocol
                return GetDefaultPortForProtocol(Protocol);
            }
        }

        /// <summary>
        /// Gets the default port for a specific camera protocol
        /// </summary>
        private static int GetDefaultPortForProtocol(CameraProtocol protocol)
        {
            return protocol switch
            {
                CameraProtocol.Hikvision => 80,
                CameraProtocol.Dahua => 80,
                CameraProtocol.Axis => 80,
                CameraProtocol.Onvif => 80,
                CameraProtocol.Bosch => 80,
                CameraProtocol.Hanwha => 80,
                _ => 80 // Default HTTP port
            };
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

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }

    /// <summary>
    /// Represents a single protocol checking log entry
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