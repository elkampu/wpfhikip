using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows.Input;
using System.Windows.Threading;

using wpfhikip.Models;
using wpfhikip.ViewModels.Commands;

namespace wpfhikip.ViewModels
{
    public class StatusDetailDialogViewModel : ViewModelBase, IDisposable
    {
        private readonly Camera _camera;
        private readonly DispatcherTimer _refreshTimer;
        private readonly StringBuilder _activityLogText;
        private int _lastLogCount = 0;

        // Backing fields
        private string _windowTitle = "Protocol Status";
        private string _ipAddress = "N/A";
        private string _port = "N/A";
        private string _liveStatus = "No status information";
        private string _copyButtonText = "Copy Logs";
        private bool _copyButtonEnabled = true;
        private string _summaryIpAddress = "N/A";
        private string _summaryPort = "N/A";
        private string _summaryProtocol = "N/A";
        private string _summaryUsername = "Not set";
        private string _summaryLogCount = "0";
        private string _summaryLastUpdated = "Never";

        // Properties
        public string WindowTitle
        {
            get => _windowTitle;
            set => SetProperty(ref _windowTitle, value);
        }

        public string IpAddress
        {
            get => _ipAddress;
            set => SetProperty(ref _ipAddress, value);
        }

        public string Port
        {
            get => _port;
            set => SetProperty(ref _port, value);
        }

        public string LiveStatus
        {
            get => _liveStatus;
            set => SetProperty(ref _liveStatus, value);
        }

        public string CopyButtonText
        {
            get => _copyButtonText;
            set => SetProperty(ref _copyButtonText, value);
        }

        public bool CopyButtonEnabled
        {
            get => _copyButtonEnabled;
            set => SetProperty(ref _copyButtonEnabled, value);
        }

        // Summary Properties
        public string SummaryIpAddress
        {
            get => _summaryIpAddress;
            set => SetProperty(ref _summaryIpAddress, value);
        }

        public string SummaryPort
        {
            get => _summaryPort;
            set => SetProperty(ref _summaryPort, value);
        }

        public string SummaryProtocol
        {
            get => _summaryProtocol;
            set => SetProperty(ref _summaryProtocol, value);
        }

        public string SummaryUsername
        {
            get => _summaryUsername;
            set => SetProperty(ref _summaryUsername, value);
        }

        public string SummaryLogCount
        {
            get => _summaryLogCount;
            set => SetProperty(ref _summaryLogCount, value);
        }

        public string SummaryLastUpdated
        {
            get => _summaryLastUpdated;
            set => SetProperty(ref _summaryLastUpdated, value);
        }

        // Collections
        public ObservableCollection<ProtocolGroupViewModel> ProtocolGroups { get; }
        public ObservableCollection<ProtocolStatusCardViewModel> ProtocolStatusCards { get; }

        // Commands
        public ICommand CopyLogsCommand { get; }
        public ICommand CloseCommand { get; }

        // Events
        public event Action<string> RequestClipboardCopy;
        public event Action RequestClose;

        public StatusDetailDialogViewModel(Camera camera)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));
            _activityLogText = new StringBuilder();

            // Initialize collections
            ProtocolGroups = new ObservableCollection<ProtocolGroupViewModel>();
            ProtocolStatusCards = new ObservableCollection<ProtocolStatusCardViewModel>();

            // Initialize commands
            CopyLogsCommand = new RelayCommand(CopyLogs);
            CloseCommand = new RelayCommand(Close);

            // Initialize properties
            InitializeProperties();

            // Initialize protocol status cards
            InitializeProtocolStatusCards();

            // Initialize timer
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _refreshTimer.Tick += RefreshTimer_Tick;

            // Initial update
            UpdateActivityLog();

            // Start timer
            _refreshTimer.Start();
        }

        private void InitializeProperties()
        {
            WindowTitle = $"Protocol Status - {_camera.CurrentIP ?? "Unknown IP"}";
            IpAddress = _camera.CurrentIP ?? "N/A";
            Port = GetPortDisplay(_camera);
            LiveStatus = _camera.Status ?? "No status information";
        }

        private void InitializeProtocolStatusCards()
        {
            // Get protocol test order based on camera settings
            var protocolTestOrder = GetProtocolTestOrder();

            // Create status cards for each protocol
            foreach (var protocol in protocolTestOrder)
            {
                var card = new ProtocolStatusCardViewModel(protocol);
                ProtocolStatusCards.Add(card);
            }
        }

        private List<string> GetProtocolTestOrder()
        {
            if (_camera.Protocol == CameraProtocol.Auto)
            {
                return new List<string> { "Hikvision", "Dahua", "Axis", "ONVIF" };
            }
            else
            {
                var selectedProtocol = _camera.Protocol.ToString();
                var protocolTestOrder = new List<string> { selectedProtocol };

                // Add other protocols after the selected one
                var allProtocols = new[] { "Hikvision", "Dahua", "Axis", "ONVIF" };
                protocolTestOrder.AddRange(allProtocols.Where(p => p != selectedProtocol));

                return protocolTestOrder;
            }
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            // Update live status
            LiveStatus = _camera.Status ?? "No status information";

            // Update port if it changed
            Port = GetPortDisplay(_camera);

            // Update protocol status cards
            UpdateProtocolStatusCards();

            // Update activity log if new entries are available
            if (_camera.ProtocolLogs.Count != _lastLogCount)
            {
                UpdateActivityLog();
                _lastLogCount = _camera.ProtocolLogs.Count;
            }
        }

        private void UpdateProtocolStatusCards()
        {
            var logs = _camera.ProtocolLogs.ToArray();
            var currentStatus = _camera.Status ?? "";

            // Update each protocol status card based on logs
            var protocolGroups = logs
                .Where(l => ShouldIncludeLogEntry(l))
                .GroupBy(l => NormalizeProtocolName(l.Protocol));

            foreach (var card in ProtocolStatusCards)
            {
                var group = protocolGroups.FirstOrDefault(g => g.Key == card.ProtocolName);
                if (group == null)
                {
                    card.SetStatus(ProtocolTestStatus.Pending, "Waiting...");
                    continue;
                }

                var allLogs = group.ToArray();
                var latestLog = group.OrderByDescending(l => l.Timestamp).First();

                // Determine status based on logs
                ProtocolTestStatus status;
                string statusText;

                if (allLogs.Any(l => l.Step.Contains("Protocol Found") && l.Level == ProtocolLogLevel.Success))
                {
                    status = ProtocolTestStatus.Success;
                    statusText = "Compatible";
                }
                else if (allLogs.Any(l => l.Step.Contains("compatible") && l.Level == ProtocolLogLevel.Success))
                {
                    status = ProtocolTestStatus.Success;
                    statusText = "Compatible";
                }
                else if (allLogs.Any(l => l.Step.Contains("Protocol Failed") && l.Level == ProtocolLogLevel.Error))
                {
                    status = ProtocolTestStatus.Failed;
                    statusText = "Failed";
                }
                else if (allLogs.Any(l => l.Level == ProtocolLogLevel.Error))
                {
                    status = ProtocolTestStatus.Failed;
                    statusText = "Failed";
                }
                else if (allLogs.Any(l => l.Level == ProtocolLogLevel.Warning))
                {
                    status = ProtocolTestStatus.Warning;
                    statusText = "Warning";
                }
                else if (latestLog.Step.Contains("Starting") || latestLog.Step.Contains("Testing"))
                {
                    status = ProtocolTestStatus.Testing;
                    statusText = "Testing...";
                }
                else
                {
                    status = ProtocolTestStatus.Pending;
                    statusText = "Pending";
                }

                card.SetStatus(status, statusText);
            }
        }

        private void UpdateActivityLog()
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");

            // Clear and rebuild protocol groups
            ProtocolGroups.Clear();

            // Show detailed protocol logs (excluding system logs)
            var logs = _camera.ProtocolLogs.ToArray();
            if (logs.Length > 0)
            {
                // Group logs by protocol for better readability and normalize protocol names
                var protocolGroups = logs
                    .Where(l => ShouldIncludeLogEntry(l))
                    .GroupBy(l => NormalizeProtocolName(l.Protocol))
                    .Where(g => !IsSystemProtocol(g.Key))
                    .OrderBy(g => GetProtocolTestOrder().IndexOf(g.Key) >= 0 ? GetProtocolTestOrder().IndexOf(g.Key) : int.MaxValue)
                    .ThenBy(g => g.Key);

                foreach (var protocolGroup in protocolGroups)
                {
                    var viewModel = new ProtocolGroupViewModel
                    {
                        ProtocolName = protocolGroup.Key.ToUpper(),
                        LogEntries = new ObservableCollection<ProtocolLogEntryViewModel>(
                            protocolGroup.OrderBy(l => l.Timestamp)
                                        .Select(l => new ProtocolLogEntryViewModel(l))
                        )
                    };
                    ProtocolGroups.Add(viewModel);
                }
            }

            // Update connection summary
            UpdateConnectionSummary(timestamp, logs);

            // Build text for clipboard operations
            BuildActivityLogText();
        }

        private void UpdateConnectionSummary(string timestamp, ProtocolLogEntry[] logs)
        {
            SummaryIpAddress = _camera.CurrentIP ?? "N/A";
            SummaryPort = $"{_camera.EffectivePort} {GetPortType(_camera)}";
            SummaryProtocol = _camera.Protocol.ToString();
            SummaryUsername = _camera.Username ?? "Not set";
            SummaryLogCount = logs.Where(l => ShouldIncludeLogEntry(l) && !IsSystemProtocol(NormalizeProtocolName(l.Protocol))).Count().ToString();
            SummaryLastUpdated = timestamp;
        }

        private void BuildActivityLogText()
        {
            var activity = new StringBuilder();

            if (ProtocolGroups.Any())
            {
                activity.AppendLine("╔═══════════════════════════════════════════════════════════════╗");
                activity.AppendLine("║                  DETAILED PROTOCOL ACTIVITY                  ║");
                activity.AppendLine("╚═══════════════════════════════════════════════════════════════╝");
                activity.AppendLine();

                foreach (var protocolGroup in ProtocolGroups)
                {
                    activity.AppendLine($"┌─ {protocolGroup.ProtocolName} PROTOCOL {new string('─', Math.Max(0, 50 - protocolGroup.ProtocolName.Length))}");

                    foreach (var log in protocolGroup.LogEntries)
                    {
                        var icon = GetLogIcon(log.Level);
                        activity.AppendLine($"│ [{log.FormattedTimestamp}] {icon} {log.Step}");

                        if (!string.IsNullOrEmpty(log.Details))
                        {
                            activity.AppendLine($"│    └─ {log.Details}");
                        }
                    }
                    activity.AppendLine($"└{new string('─', 65)}");
                    activity.AppendLine();
                }
            }
            else
            {
                activity.AppendLine("╔═══════════════════════════════════════════════════════════════╗");
                activity.AppendLine("║                     PROTOCOLS TO TEST                        ║");
                activity.AppendLine("╚═══════════════════════════════════════════════════════════════╝");
                activity.AppendLine();

                foreach (var protocol in GetProtocolTestOrder())
                {
                    activity.AppendLine($"  ◦ {protocol}");
                }
                activity.AppendLine();
            }

            // Add connection summary
            activity.AppendLine("╔═══════════════════════════════════════════════════════════════╗");
            activity.AppendLine("║                    CONNECTION SUMMARY                        ║");
            activity.AppendLine("╚═══════════════════════════════════════════════════════════════╝");
            activity.AppendLine();
            activity.AppendLine($"  IP Address ........: {SummaryIpAddress}");
            activity.AppendLine($"  Port ..............: {SummaryPort}");
            activity.AppendLine($"  Selected Protocol .: {SummaryProtocol}");
            activity.AppendLine($"  Username ..........: {SummaryUsername}");
            activity.AppendLine($"  Total Log Entries .: {SummaryLogCount}");
            activity.AppendLine($"  Last Updated ......: {SummaryLastUpdated}");

            _activityLogText.Clear();
            _activityLogText.Append(activity.ToString());
        }

        private void CopyLogs(object parameter)
        {
            if (_activityLogText.Length > 0)
            {
                RequestClipboardCopy?.Invoke(_activityLogText.ToString());

                // Visual feedback
                CopyButtonText = "Copied!";
                CopyButtonEnabled = false;

                // Reset after 2 seconds
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (s, e) =>
                {
                    CopyButtonText = "Copy Logs";
                    CopyButtonEnabled = true;
                    timer.Stop();
                };
                timer.Start();
            }
        }

        private void Close(object parameter)
        {
            RequestClose?.Invoke();
        }

        // Helper methods
        private string GetPortDisplay(Camera camera)
        {
            var effectivePort = camera.EffectivePort;
            var customPort = camera.Port;

            if (!string.IsNullOrEmpty(customPort) && int.TryParse(customPort, out _))
            {
                return $"{effectivePort} (Custom)";
            }
            else
            {
                var protocolDisplay = camera.Protocol == CameraProtocol.Auto ? "Auto" : camera.Protocol.ToString();
                return $"{effectivePort} ({protocolDisplay})";
            }
        }

        private string GetPortType(Camera camera)
        {
            if (!string.IsNullOrEmpty(camera.Port) && int.TryParse(camera.Port, out _))
            {
                return "(Custom)";
            }
            return "(Default)";
        }

        private string GetLogIcon(ProtocolLogLevel level)
        {
            return level switch
            {
                ProtocolLogLevel.Success => "✓",
                ProtocolLogLevel.Warning => "⚠",
                ProtocolLogLevel.Error => "✗",
                ProtocolLogLevel.Info => "→",
                _ => "•"
            };
        }

        private bool IsSystemProtocol(string protocolName)
        {
            return protocolName?.ToLower() is "system" or "network" or "unknown";
        }

        private string NormalizeProtocolName(string protocolName)
        {
            return protocolName?.ToLower() switch
            {
                "onvif" => "ONVIF",
                "hikvision" => "Hikvision",
                "dahua" => "Dahua",
                "axis" => "Axis",
                "bosch" => "Bosch",
                "hanwha" => "Hanwha",
                "system" => "System",
                "network" => "Network",
                _ => protocolName ?? "Unknown"
            };
        }

        private bool ShouldIncludeLogEntry(ProtocolLogEntry log)
        {
            if (string.IsNullOrEmpty(log.Step))
                return false;

            var protocol = NormalizeProtocolName(log.Protocol);

            if (IsSystemProtocol(protocol))
                return false;

            if (log.Step.ToLower().Contains("step protocol") && string.IsNullOrEmpty(log.Details))
                return false;

            if (log.Step.ToLower().Contains("systep protocol"))
                return false;

            return true;
        }

        public void Dispose()
        {
            _refreshTimer?.Stop();
        }
    }

    // Enums and ViewModels
    public enum ProtocolTestStatus
    {
        Pending,
        Testing,
        Success,
        Failed,
        Warning
    }

    public class ProtocolGroupViewModel
    {
        public string ProtocolName { get; set; } = string.Empty;
        public ObservableCollection<ProtocolLogEntryViewModel> LogEntries { get; set; } = new();
    }

    public class ProtocolLogEntryViewModel
    {
        public DateTime Timestamp { get; set; }
        public string Protocol { get; set; } = string.Empty;
        public string Step { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
        public ProtocolLogLevel Level { get; set; }
        public string IpAddress { get; set; } = string.Empty;
        public int Port { get; set; }

        public string FormattedTimestamp => Timestamp.ToString("HH:mm:ss.fff");

        public ProtocolLogEntryViewModel() { }

        public ProtocolLogEntryViewModel(ProtocolLogEntry logEntry)
        {
            Timestamp = logEntry.Timestamp;
            Protocol = logEntry.Protocol;
            Step = logEntry.Step;
            Details = logEntry.Details;
            Level = logEntry.Level;
            IpAddress = logEntry.IpAddress;
            Port = logEntry.Port;
        }
    }

    public class ProtocolStatusCardViewModel : ViewModelBase
    {
        private ProtocolTestStatus _status;
        private string _statusText = string.Empty;
        private string _statusColor = "#FF808080";
        private string _borderColor = "#FF505050";

        public string ProtocolName { get; }

        public ProtocolTestStatus Status
        {
            get => _status;
            private set => SetProperty(ref _status, value);
        }

        public string StatusText
        {
            get => _statusText;
            private set => SetProperty(ref _statusText, value);
        }

        public string StatusColor
        {
            get => _statusColor;
            private set => SetProperty(ref _statusColor, value);
        }

        public string BorderColor
        {
            get => _borderColor;
            private set => SetProperty(ref _borderColor, value);
        }

        public ProtocolStatusCardViewModel(string protocolName)
        {
            ProtocolName = protocolName;
            SetStatus(ProtocolTestStatus.Pending, "Waiting...");
        }

        public void SetStatus(ProtocolTestStatus status, string statusText)
        {
            Status = status;
            StatusText = statusText;

            switch (status)
            {
                case ProtocolTestStatus.Pending:
                    StatusColor = "#FF808080"; // Gray
                    BorderColor = "#FF505050";
                    break;

                case ProtocolTestStatus.Testing:
                    StatusColor = "#FFFFC107"; // Yellow
                    BorderColor = "#FFFFC107";
                    break;

                case ProtocolTestStatus.Success:
                    StatusColor = "#FF28A745"; // Green
                    BorderColor = "#FF28A745";
                    break;

                case ProtocolTestStatus.Failed:
                    StatusColor = "#FFDC3545"; // Red
                    BorderColor = "#FFDC3545";
                    break;

                case ProtocolTestStatus.Warning:
                    StatusColor = "#FFFF851B"; // Orange
                    BorderColor = "#FFFF851B";
                    break;
            }
        }
    }
}