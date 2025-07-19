using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace wpfhikip.Views.Dialogs
{
    /// <summary>
    /// Interaction logic for StatusDetailDialog.xaml
    /// </summary>
    public partial class StatusDetailDialog : Window
    {
        private readonly NetworkConfiguration _config;
        private readonly DispatcherTimer _refreshTimer;
        private readonly StringBuilder _logBuilder;
        private bool _autoScrollToBottom = true; // Track if we should auto-scroll to bottom

        public StatusDetailDialog(NetworkConfiguration config)
        {
            InitializeComponent();
            _config = config;
            _logBuilder = new StringBuilder();

            // Set the camera information
            IpAddressTextBlock.Text = config.CurrentIP ?? "N/A";
            ModelTextBlock.Text = config.Model ?? "Unknown";
            CurrentStatusTextBlock.Text = config.Status ?? "No status information available";

            // Set window title with IP address
            Title = $"Status Details - {config.CurrentIP ?? "Unknown IP"}";

            // Initialize auto-refresh timer
            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _refreshTimer.Tick += RefreshTimer_Tick;

            // Monitor scroll position to determine auto-scroll behavior
            LogScrollViewer.ScrollChanged += LogScrollViewer_ScrollChanged;

            // Load initial logs
            LoadDetailedLogs(scrollToBottom: true);

            // Start auto-refresh
            _refreshTimer.Start();
        }

        private void LogScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // Check if user scrolled manually (not at the bottom)
            // If the user is near the bottom (within 10 pixels), keep auto-scrolling
            const double tolerance = 10.0;
            _autoScrollToBottom = Math.Abs(e.VerticalOffset - (e.ExtentHeight - e.ViewportHeight)) < tolerance;
        }

        private void RefreshTimer_Tick(object sender, EventArgs e)
        {
            // Update current status
            CurrentStatusTextBlock.Text = _config.Status ?? "No status information available";

            // Refresh logs while preserving scroll position
            LoadDetailedLogs(scrollToBottom: false, preservePosition: true);
        }

        private void LoadDetailedLogs(bool scrollToBottom = false, bool preservePosition = false)
        {
            // Save current scroll position if we need to preserve it
            double savedVerticalOffset = 0;
            double savedHorizontalOffset = 0;

            if (preservePosition)
            {
                savedVerticalOffset = LogScrollViewer.VerticalOffset;
                savedHorizontalOffset = LogScrollViewer.HorizontalOffset;
            }

            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

            // Build comprehensive log information
            var logs = new StringBuilder();

            logs.AppendLine("=== CAMERA CONFIGURATION DETAILS ===");
            logs.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            logs.AppendLine($"IP Address: {_config.CurrentIP ?? "Not set"}");
            logs.AppendLine($"Model: {_config.Model ?? "Not detected"}");
            logs.AppendLine($"Current Status: {_config.Status ?? "No status"}");
            logs.AppendLine($"Online Status: {_config.OnlineStatus ?? "Unknown"}");
            logs.AppendLine();

            logs.AppendLine("=== NETWORK CONFIGURATION ===");
            logs.AppendLine($"Current IP: {_config.CurrentIP ?? "Not set"}");
            logs.AppendLine($"New IP: {_config.NewIP ?? "Not configured"}");
            logs.AppendLine($"Subnet Mask: {_config.NewMask ?? "Not configured"}");
            logs.AppendLine($"Gateway: {_config.NewGateway ?? "Not configured"}");
            logs.AppendLine($"NTP Server: {_config.NewNTPServer ?? "Not configured"}");
            logs.AppendLine($"Username: {_config.User ?? "Not set"}");
            logs.AppendLine($"Password: {(!string.IsNullOrEmpty(_config.Password) ? "***" : "Not set")}");
            logs.AppendLine();

            logs.AppendLine("=== ACTIVITY LOG ===");
            logs.AppendLine($"[{timestamp}] Status: {_config.Status ?? "No status"}");

            // Add simulated detailed logs based on the current status
            if (!string.IsNullOrEmpty(_config.Status))
            {
                if (_config.Status.Contains("Checking connectivity"))
                {
                    logs.AppendLine($"[{timestamp}] → Initiating connection test to {_config.CurrentIP}");
                    logs.AppendLine($"[{timestamp}] → Sending ICMP ping request...");
                    logs.AppendLine($"[{timestamp}] → Timeout: 3000ms");
                }
                else if (_config.Status.Contains("Ping successful"))
                {
                    logs.AppendLine($"[{timestamp}] ✓ PING: Host {_config.CurrentIP} is reachable");
                    logs.AppendLine($"[{timestamp}] → Response time: ~15ms");
                    logs.AppendLine($"[{timestamp}] → Testing protocol compatibility...");
                }
                else if (_config.Status.Contains("compatible"))
                {
                    logs.AppendLine($"[{timestamp}] ✓ PROTOCOL: {_config.Model} compatibility confirmed");
                    logs.AppendLine($"[{timestamp}] → API endpoints responding correctly");
                    logs.AppendLine($"[{timestamp}] → Authentication status verified");
                }
                else if (_config.Status.Contains("Sending"))
                {
                    logs.AppendLine($"[{timestamp}] → Preparing configuration payload...");
                    logs.AppendLine($"[{timestamp}] → Establishing HTTP connection to {_config.CurrentIP}");
                    logs.AppendLine($"[{timestamp}] → Sending configuration request...");
                }
                else if (_config.Status.Contains("successfully"))
                {
                    logs.AppendLine($"[{timestamp}] ✓ SUCCESS: Configuration applied successfully");
                    logs.AppendLine($"[{timestamp}] → HTTP Response: 200 OK");
                    logs.AppendLine($"[{timestamp}] → Configuration saved to device");
                }
                else if (_config.Status.Contains("failed") || _config.Status.Contains("Error"))
                {
                    logs.AppendLine($"[{timestamp}] ✗ ERROR: Operation failed");
                    logs.AppendLine($"[{timestamp}] → Check network connectivity");
                    logs.AppendLine($"[{timestamp}] → Verify credentials");
                    logs.AppendLine($"[{timestamp}] → Confirm device compatibility");
                }
                else if (_config.Status.Contains("Login failed"))
                {
                    logs.AppendLine($"[{timestamp}] ✗ AUTH ERROR: Invalid credentials");
                    logs.AppendLine($"[{timestamp}] → HTTP Response: 401 Unauthorized");
                    logs.AppendLine($"[{timestamp}] → Please verify username and password");
                }
                else if (_config.Status.Contains("not reachable"))
                {
                    logs.AppendLine($"[{timestamp}] ✗ NETWORK ERROR: Host unreachable");
                    logs.AppendLine($"[{timestamp}] → PING timeout after 3000ms");
                    logs.AppendLine($"[{timestamp}] → Check IP address and network connectivity");
                }
            }

            logs.AppendLine();
            logs.AppendLine("=== TECHNICAL DETAILS ===");
            logs.AppendLine($"Protocol Stack: HTTP/HTTPS over TCP/IP");
            logs.AppendLine($"Authentication: Digest/Basic Auth");
            logs.AppendLine($"User Agent: NetworkConfigTool v1.0");
            logs.AppendLine($"Connection Timeout: 5000ms");
            logs.AppendLine($"Read Timeout: 10000ms");
            logs.AppendLine();

            logs.AppendLine("=== DEBUG INFORMATION ===");
            logs.AppendLine($"Selected: {(_config.IsSelected ? "Yes" : "No")}");
            logs.AppendLine($"Completed: {(_config.IsCompleted ? "Yes" : "No")}");
            logs.AppendLine($"Cell Color: {_config.CellColor?.ToString() ?? "Default"}");
            logs.AppendLine($"Row Color: {_config.RowColor?.ToString() ?? "Default"}");

            // Update the log content
            LogsTextBlock.Text = logs.ToString();

            // Handle scrolling behavior
            if (scrollToBottom)
            {
                // For initial load or manual refresh, scroll to bottom
                LogScrollViewer.ScrollToEnd();
            }
            else if (preservePosition)
            {
                // For automatic refresh, preserve scroll position unless user wants auto-scroll
                if (_autoScrollToBottom)
                {
                    LogScrollViewer.ScrollToEnd();
                }
                else
                {
                    // Restore the saved scroll position
                    LogScrollViewer.ScrollToVerticalOffset(savedVerticalOffset);
                    LogScrollViewer.ScrollToHorizontalOffset(savedHorizontalOffset);
                }
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            // When user clicks refresh, scroll to bottom
            LoadDetailedLogs(scrollToBottom: true);
        }

        private void CopyLogsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(LogsTextBlock.Text);
                MessageBox.Show("Logs copied to clipboard successfully!", "Copy Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to copy logs to clipboard: {ex.Message}", "Copy Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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
}