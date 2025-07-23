using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

using wpfhikip.Models;

// Add this alias to avoid namespace conflicts
using ModelsProtocolLogEntry = wpfhikip.Models.ProtocolLogEntry;

namespace wpfhikip.Views.Dialogs
{
    /// <summary>
    /// Interaction logic for StatusDetailDialog.xaml
    /// </summary>
    public partial class StatusDetailDialog : Window
    {
        private readonly Camera _camera;
        private readonly DispatcherTimer _refreshTimer;
        private readonly StringBuilder _activityLog;
        private bool _autoScrollToBottom = true;
        private int _lastLogCount = 0;
        private Dictionary<string, ProtocolStatusCard> _protocolCards = new();

        public StatusDetailDialog(Camera camera)
        {
            InitializeComponent();
            _camera = camera;
            _activityLog = new StringBuilder();

            // Set the connection information
            IpAddressTextBlock.Text = camera.CurrentIP ?? "N/A";
            PortTextBlock.Text = GetPortDisplay(camera);
            LiveStatusTextBlock.Text = camera.Status ?? "No status information";

            // Set window title with IP address
            Title = $"Protocol Status - {camera.CurrentIP ?? "Unknown IP"}";

            // Initialize protocol status cards
            InitializeProtocolStatusCards();

            // Initialize auto-refresh timer for live updates
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250) // Update 4 times per second for real-time feel
            };
            _refreshTimer.Tick += RefreshTimer_Tick;

            // Monitor scroll position for auto-scroll behavior
            StatusScrollViewer.ScrollChanged += (s, e) =>
            {
                const double tolerance = 10.0;
                _autoScrollToBottom = Math.Abs(e.VerticalOffset - (e.ExtentHeight - e.ViewportHeight)) < tolerance;
            };

            // Initialize activity log
            UpdateActivityLog();

            // Start live monitoring
            _refreshTimer.Start();
        }

        private void InitializeProtocolStatusCards()
        {
            var protocols = new[] { "Hikvision", "Dahua", "Axis", "ONVIF" };

            foreach (var protocol in protocols)
            {
                var card = new ProtocolStatusCard(protocol);
                _protocolCards[protocol] = card;
                ProtocolStatusGrid.Children.Add(card);
            }
        }

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

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            // Update live status
            LiveStatusTextBlock.Text = _camera.Status ?? "No status information";

            // Update port if it changed
            PortTextBlock.Text = GetPortDisplay(_camera);

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

            // Reset all cards to default state
            foreach (var card in _protocolCards.Values)
            {
                card.SetStatus(ProtocolTestStatus.Pending, "Waiting...");
            }

            // Update based on current activity
            if (currentStatus.Contains("Testing") && currentStatus.Contains("protocol"))
            {
                var testingProtocol = ExtractProtocolFromStatus(currentStatus);
                if (_protocolCards.ContainsKey(testingProtocol))
                {
                    _protocolCards[testingProtocol].SetStatus(ProtocolTestStatus.Testing, "Testing...");
                }
            }

            // Update based on logs - this is the main logic that determines compatibility
            var protocolGroups = logs
                .Where(l => ShouldIncludeLogEntry(l))
                .GroupBy(l => NormalizeProtocolName(l.Protocol));

            foreach (var group in protocolGroups)
            {
                var protocol = group.Key;
                if (!_protocolCards.ContainsKey(protocol))
                    continue;

                var latestLog = group.OrderByDescending(l => l.Timestamp).First();
                var allLogs = group.ToArray();

                // Determine status based on logs - FIXED: Only show success if there's actual success
                ProtocolTestStatus status;
                string statusText;

                // Check for successful compatibility confirmation
                if (allLogs.Any(l => l.Step.Contains("Protocol Found") && l.Level == wpfhikip.Models.ProtocolLogLevel.Success))
                {
                    status = ProtocolTestStatus.Success;
                    statusText = "Compatible";
                }
                // Check for explicit compatibility mentions with success level
                else if (allLogs.Any(l => l.Step.Contains("compatible") && l.Level == wpfhikip.Models.ProtocolLogLevel.Success))
                {
                    status = ProtocolTestStatus.Success;
                    statusText = "Compatible";
                }
                // Check for protocol failures
                else if (allLogs.Any(l => l.Step.Contains("Protocol Failed") && l.Level == wpfhikip.Models.ProtocolLogLevel.Error))
                {
                    status = ProtocolTestStatus.Failed;
                    statusText = "Failed";
                }
                // Check for other errors
                else if (allLogs.Any(l => l.Level == wpfhikip.Models.ProtocolLogLevel.Error))
                {
                    status = ProtocolTestStatus.Failed;
                    statusText = "Failed";
                }
                // Check for warnings
                else if (allLogs.Any(l => l.Level == wpfhikip.Models.ProtocolLogLevel.Warning))
                {
                    status = ProtocolTestStatus.Warning;
                    statusText = "Warning";
                }
                // Check if testing is in progress
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

                _protocolCards[protocol].SetStatus(status, statusText);
            }

            // REMOVED: The problematic logic that always showed detected protocol as compatible
            // This was causing the issue where any selected protocol would show as "Detected & Compatible"
            // even when it actually failed the compatibility test
        }

        private void UpdateActivityLog()
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var currentStatus = _camera.Status ?? "No status";
            var ipAddress = _camera.CurrentIP ?? "N/A";
            var port = _camera.EffectivePort;

            // Build comprehensive live activity information
            var activity = new StringBuilder();


            // Show detailed protocol logs (excluding system logs)
            var logs = _camera.ProtocolLogs.ToArray();
            if (logs.Length > 0)
            {
                activity.AppendLine("=== DETAILED PROTOCOL ACTIVITY ===");

                // Group logs by protocol for better readability and normalize protocol names
                var protocolGroups = logs
                    .Where(l => ShouldIncludeLogEntry(l))
                    .GroupBy(l => NormalizeProtocolName(l.Protocol))
                    .Where(g => !IsSystemProtocol(g.Key))
                    .OrderBy(g => g.Key);

                foreach (var protocolGroup in protocolGroups)
                {
                    activity.AppendLine($"--- {protocolGroup.Key.ToUpper()} PROTOCOL ---");

                    foreach (var log in protocolGroup.OrderBy(l => l.Timestamp))
                    {
                        activity.AppendLine($"[{log.FormattedTimestamp}] {log.LogIcon} {log.Step}");
                        if (!string.IsNullOrEmpty(log.Details))
                        {
                            activity.AppendLine($"    └─ {log.Details}");
                        }
                    }
                    activity.AppendLine();
                }
            }
            else
            {

                // Show what protocols will be tested
                activity.AppendLine("--- PROTOCOLS TO TEST ---");
                var protocolsToTest = GetProtocolsToTest();
                foreach (var protocol in protocolsToTest)
                {
                    activity.AppendLine($"• {protocol}");
                }
                activity.AppendLine();
            }


            // Add connection summary
            activity.AppendLine();
            activity.AppendLine("=== CONNECTION SUMMARY ===");
            activity.AppendLine($"IP Address: {ipAddress}");
            activity.AppendLine($"Port: {port} {GetPortType(_camera)}");
            activity.AppendLine($"Selected Protocol: {_camera.Protocol}");
            activity.AppendLine($"Username: {_camera.Username ?? "Not set"}");
            activity.AppendLine($"Total Log Entries: {logs.Where(l => ShouldIncludeLogEntry(l) && !IsSystemProtocol(NormalizeProtocolName(l.Protocol))).Count()}");

            // Store the formatted text for clipboard operations
            _activityLog.Clear();
            _activityLog.Append(activity.ToString());

            // Update the display
            StatusActivityTextBox.Text = activity.ToString();

            // Auto-scroll to bottom if user hasn't manually scrolled
            if (_autoScrollToBottom)
            {
                StatusScrollViewer.ScrollToEnd();
            }
        }

        /// <summary>
        /// Determines if a protocol is a system protocol that should be excluded
        /// </summary>
        private bool IsSystemProtocol(string protocolName)
        {
            return protocolName?.ToLower() is "system" or "network" or "unknown";
        }

        /// <summary>
        /// Normalizes protocol names to prevent duplication (e.g., "Onvif" and "ONVIF" become "ONVIF")
        /// </summary>
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

        /// <summary>
        /// Determines if a log entry should be included in the display
        /// </summary>
        private bool ShouldIncludeLogEntry(ModelsProtocolLogEntry log)
        {
            // Filter out unwanted log entries
            if (string.IsNullOrEmpty(log.Step))
                return false;

            var protocol = NormalizeProtocolName(log.Protocol);

            // Exclude system protocol logs completely
            if (IsSystemProtocol(protocol))
                return false;

            // Exclude logs that just mention "step protocol" without useful information
            if (log.Step.ToLower().Contains("step protocol") && string.IsNullOrEmpty(log.Details))
                return false;

            // Exclude system protocol references
            if (log.Step.ToLower().Contains("systep protocol"))
                return false;

            return true;
        }

        private string[] GetProtocolsToTest()
        {
            var allProtocols = new[] { "Hikvision", "Dahua", "Axis", "ONVIF" };

            // If Auto is selected, show all protocols
            if (_camera.Protocol == CameraProtocol.Auto)
            {
                return allProtocols;
            }

            // If a specific protocol is selected, prioritize it
            var selectedProtocol = _camera.Protocol.ToString();
            if (!string.IsNullOrEmpty(selectedProtocol) && allProtocols.Contains(selectedProtocol))
            {
                var prioritized = new[] { selectedProtocol }
                    .Concat(allProtocols.Where(p => p != selectedProtocol))
                    .ToArray();
                return prioritized;
            }

            return allProtocols;
        }

        
        private string ExtractProtocolFromStatus(string status)
        {
            if (status.Contains("Hikvision")) return "Hikvision";
            if (status.Contains("Dahua")) return "Dahua";
            if (status.Contains("Axis")) return "Axis";
            if (status.Contains("ONVIF") || status.Contains("Onvif")) return "ONVIF";
            return "Unknown";
        }

        private string GetPortType(Camera camera)
        {
            if (!string.IsNullOrEmpty(camera.Port) && int.TryParse(camera.Port, out _))
            {
                return "(Custom)";
            }
            return "(Default)";
        }

        private string GetAuthStatus()
        {
            var status = _camera.Status ?? "";
            if (status.Contains("Authentication OK") || status.Contains("Auth OK"))
                return "Authenticated";
            if (status.Contains("Auth failed") || status.Contains("Login failed"))
                return "Failed";
            if (status.Contains("No auth required"))
                return "Not Required";
            return "Pending";
        }

        private void CopyLogsButton_Click(object sender, RoutedEventArgs e)
        {
            CopyLogsToClipboard(sender as Button);
        }

        private void CopyAllLogsButton_Click(object sender, RoutedEventArgs e)
        {
            CopyLogsToClipboard(sender as Button);
        }

        private void CopyLogsToClipboard(Button clickedButton)
        {
            try
            {
                if (_activityLog.Length > 0)
                {
                    Clipboard.SetText(_activityLog.ToString());

                    // Visual feedback
                    if (clickedButton != null)
                    {
                        var originalContent = clickedButton.Content;
                        clickedButton.Content = "Copied!";
                        clickedButton.IsEnabled = false;

                        // Reset after 2 seconds
                        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                        timer.Tick += (s, e) =>
                        {
                            clickedButton.Content = originalContent;
                            clickedButton.IsEnabled = true;
                            timer.Stop();
                        };
                        timer.Start();
                    }
                }
                else
                {
                    MessageBox.Show("No logs available to copy.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to copy logs to clipboard: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _refreshTimer?.Stop();
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _refreshTimer?.Stop();
            base.OnClosed(e);
        }
    }

    /// <summary>
    /// Represents the status of a protocol test
    /// </summary>
    public enum ProtocolTestStatus
    {
        Pending,
        Testing,
        Success,
        Failed,
        Warning
    }

    /// <summary>
    /// Custom control for displaying protocol status
    /// </summary>
    public class ProtocolStatusCard : Border
    {
        private readonly TextBlock _protocolNameBlock;
        private readonly TextBlock _statusBlock;
        private readonly Ellipse _statusIndicator;

        public string ProtocolName { get; }

        public ProtocolStatusCard(string protocolName)
        {
            ProtocolName = protocolName;

            // Apply base styling
            Background = new SolidColorBrush(Color.FromRgb(56, 56, 56));
            CornerRadius = new CornerRadius(6);
            Padding = new Thickness(12);
            Margin = new Thickness(5);
            BorderThickness = new Thickness(2);
            BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80));

            // Create content
            var stackPanel = new StackPanel();

            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };

            _statusIndicator = new Ellipse
            {
                Width = 12,
                Height = 12,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            _protocolNameBlock = new TextBlock
            {
                Text = protocolName,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center
            };

            headerPanel.Children.Add(_statusIndicator);
            headerPanel.Children.Add(_protocolNameBlock);

            _statusBlock = new TextBlock
            {
                Foreground = Brushes.LightGray,
                FontSize = 10,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(20, 2, 0, 0)
            };

            stackPanel.Children.Add(headerPanel);
            stackPanel.Children.Add(_statusBlock);

            Child = stackPanel;

            // Set initial status
            SetStatus(ProtocolTestStatus.Pending, "Waiting...");
        }

        public void SetStatus(ProtocolTestStatus status, string statusText)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _statusBlock.Text = statusText;

                switch (status)
                {
                    case ProtocolTestStatus.Pending:
                        _statusIndicator.Fill = new SolidColorBrush(Color.FromRgb(128, 128, 128)); // Gray
                        BorderBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80));
                        break;

                    case ProtocolTestStatus.Testing:
                        _statusIndicator.Fill = new SolidColorBrush(Color.FromRgb(255, 193, 7)); // Yellow
                        BorderBrush = new SolidColorBrush(Color.FromRgb(255, 193, 7));
                        break;

                    case ProtocolTestStatus.Success:
                        _statusIndicator.Fill = new SolidColorBrush(Color.FromRgb(40, 167, 69)); // Green
                        BorderBrush = new SolidColorBrush(Color.FromRgb(40, 167, 69));
                        break;

                    case ProtocolTestStatus.Failed:
                        _statusIndicator.Fill = new SolidColorBrush(Color.FromRgb(220, 53, 69)); // Red
                        BorderBrush = new SolidColorBrush(Color.FromRgb(220, 53, 69));
                        break;

                    case ProtocolTestStatus.Warning:
                        _statusIndicator.Fill = new SolidColorBrush(Color.FromRgb(255, 133, 27)); // Orange
                        BorderBrush = new SolidColorBrush(Color.FromRgb(255, 133, 27));
                        break;
                }
            });
        }
    }
}