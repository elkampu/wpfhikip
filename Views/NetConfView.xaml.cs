using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Text.RegularExpressions;
using System.Net;

using wpfhikip.Models;
using wpfhikip.Views.Dialogs;
using wpfhikip.Controls;

namespace wpfhikip.Views
{
    /// <summary>
    /// Interaction logic for NetConfView.xaml
    /// </summary>
    public partial class NetConfView : Window
    {
        public NetConfView()
        {
            InitializeComponent();

            // Add event handler for when cells begin editing
            dataGrid.BeginningEdit += DataGrid_BeginningEdit;
        }

        private void DataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            // Check if the editing cell contains an IP Address control
            if (e.Column is DataGridTemplateColumn templateColumn)
            {
                var columnHeader = templateColumn.Header?.ToString();

                // Check if this is one of the IP Address columns (updated with correct headers)
                if (columnHeader == "Current IP" || columnHeader == "Target IP" ||
                    columnHeader == "Target Mask" || columnHeader == "Target Gateway" ||
                    columnHeader == "Target Primary DNS" || columnHeader == "Target Secondary DNS" || columnHeader == "NTP")
                {
                    // Use dispatcher to ensure the edit template is loaded before trying to find the control
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
                    {
                        var cellContent = e.Column.GetCellContent(e.Row.Item);
                        if (cellContent != null)
                        {
                            // Find the IpAddressControl within the cell
                            var ipControl = FindVisualChild<IpAddressControl>(cellContent);
                            if (ipControl != null)
                            {
                                // Focus the first octet
                                ipControl.FocusFirstOctet();
                            }
                        }
                    }));
                }
            }
        }

        // Add this method to the NetConfView class
        private void CameraInfoButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is Camera camera)
            {
                // Create and show the camera info dialog
                var cameraInfoDialog = new CameraInfoDialog(camera);
                cameraInfoDialog.Owner = this;
                cameraInfoDialog.ShowDialog();
            }
        }

        // Helper method to find a child control of a specific type
        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                {
                    return result;
                }

                var childResult = FindVisualChild<T>(child);
                if (childResult != null)
                {
                    return childResult;
                }
            }
            return null;
        }

        // COPY & PASTE
        private void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // COPY
            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                CopySelectedCell();
                e.Handled = true;
            }
            // PASTE
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                PasteToSelectedCell();
                e.Handled = true;
            }
        }

        private void CopySelectedCell()
        {
            var cellInfo = dataGrid.CurrentCell;
            if (cellInfo.Column == null || cellInfo.Item == null)
                return;

            string textToCopy = string.Empty;

            // Check if this is an IP address column
            if (cellInfo.Column is DataGridTemplateColumn templateColumn)
            {
                var columnHeader = templateColumn.Header?.ToString();
                if (IsIpAddressColumn(columnHeader))
                {
                    // Get the IP address value from the data model
                    var camera = (Camera)cellInfo.Item;
                    textToCopy = GetIpAddressFromColumn(columnHeader, camera);
                }
            }

            // If not an IP column or no IP value found, try to get text from the cell content
            if (string.IsNullOrEmpty(textToCopy))
            {
                var content = cellInfo.Column.GetCellContent(cellInfo.Item);
                if (content is TextBlock textBlock)
                {
                    textToCopy = textBlock.Text;
                }
                else if (content is ContentPresenter presenter)
                {
                    // Try to find TextBlock within ContentPresenter
                    var textBlockInPresenter = FindVisualChild<TextBlock>(presenter);
                    if (textBlockInPresenter != null)
                    {
                        textToCopy = textBlockInPresenter.Text;
                    }
                    else
                    {
                        // Try to find IpAddressControl within ContentPresenter
                        var ipControl = FindVisualChild<IpAddressControl>(presenter);
                        if (ipControl != null)
                        {
                            textToCopy = ipControl.IpAddress;
                        }
                    }
                }
            }

            // Copy to clipboard if we have text
            if (!string.IsNullOrEmpty(textToCopy))
            {
                Clipboard.SetText(textToCopy);
            }
        }

        private string GetIpAddressFromColumn(string columnHeader, Camera camera)
        {
            return columnHeader switch
            {
                "Current IP" => camera.CurrentIP,
                "Target IP" => camera.NewIP,  // Updated to match XAML
                "Target Mask" => camera.NewMask,  // Updated to match XAML
                "Target Gateway" => camera.NewGateway,  // Updated to match XAML
                "Target Primary DNS" => camera.NewDNS1,  // Updated to match XAML
                "Target Secondary DNS" => camera.NewDNS2,  // Updated to match XAML
                "NTP" => camera.NewNTPServer,
                _ => string.Empty
            };
        }

        private void PasteToSelectedCell()
        {
            if (dataGrid.CurrentCell.Item != null && Clipboard.ContainsText())
            {
                var clipboardText = Clipboard.GetText();
                var column = dataGrid.CurrentCell.Column;
                var row = (Camera)dataGrid.CurrentItem;

                // Check if this is an IP address column
                if (column is DataGridTemplateColumn templateColumn)
                {
                    var columnHeader = templateColumn.Header?.ToString();
                    if (IsIpAddressColumn(columnHeader))
                    {
                        PasteIpAddress(clipboardText, columnHeader, row);
                        return;
                    }
                }

                // Handle regular text columns
                var property = typeof(Camera).GetProperty(column.SortMemberPath);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(row, clipboardText);
                }
            }
        }

        private bool IsIpAddressColumn(string columnHeader)
        {
            return columnHeader == "Current IP" || columnHeader == "Target IP" ||
                   columnHeader == "Target Mask" || columnHeader == "Target Gateway" ||
                   columnHeader == "Target Primary DNS" || columnHeader == "Target Secondary DNS" || columnHeader == "NTP";
        }

        private void PasteIpAddress(string clipboardText, string columnHeader, Camera camera)
        {
            // Extract IP address from clipboard text
            var extractedIp = ExtractIpAddressFromText(clipboardText);

            if (string.IsNullOrEmpty(extractedIp))
            {
                ShowIpValidationError("Invalid IP address format in clipboard. Supported formats:\n" +
                                    "• xxx.xxx.xxx.xxx\n" +
                                    "• http://xxx.xxx.xxx.xxx/\n" +
                                    "• https://xxx.xxx.xxx.xxx/");
                return;
            }

            // Validate the extracted IP address
            if (!IsValidIpAddress(extractedIp))
            {
                ShowIpValidationError("The clipboard contains an invalid IP address.\nPlease ensure the IP address is in correct format (0-255 for each octet).");
                return;
            }

            // Set the IP address to the appropriate property (updated column names)
            switch (columnHeader)
            {
                case "Current IP":
                    camera.CurrentIP = extractedIp;
                    break;
                case "Target IP":  // Updated to match XAML
                    camera.NewIP = extractedIp;
                    break;
                case "Target Mask":  // Updated to match XAML
                    camera.NewMask = extractedIp;
                    break;
                case "Target Gateway":  // Updated to match XAML
                    camera.NewGateway = extractedIp;
                    break;
                case "Target Primary DNS":  // Updated to match XAML
                    camera.NewDNS1 = extractedIp;
                    break;
                case "Target Secondary DNS":  // Updated to match XAML
                    camera.NewDNS2 = extractedIp;
                    break;
                case "NTP":
                    camera.NewNTPServer = extractedIp;
                    break;
            }

            // If the cell is currently being edited, update the IpAddressControl as well
            var cellContent = dataGrid.CurrentCell.Column.GetCellContent(dataGrid.CurrentCell.Item);
            if (cellContent != null)
            {
                var ipControl = FindVisualChild<IpAddressControl>(cellContent);
                if (ipControl != null)
                {
                    ipControl.IpAddress = extractedIp;
                }
            }
        }

        private string ExtractIpAddressFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            text = text.Trim();

            // Pattern 1: Direct IP address (86.49.161.121)
            var directIpPattern = @"^(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})$";
            var directMatch = Regex.Match(text, directIpPattern);
            if (directMatch.Success)
            {
                return directMatch.Groups[1].Value;
            }

            // Pattern 2: HTTP URL (http://86.49.161.121/ or https://86.49.161.121/)
            var urlPattern = @"^https?://(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})/?.*$";
            var urlMatch = Regex.Match(text, urlPattern, RegexOptions.IgnoreCase);
            if (urlMatch.Success)
            {
                return urlMatch.Groups[1].Value;
            }

            // Pattern 3: Try to extract any IP-like pattern from the text
            var anyIpPattern = @"(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})";
            var anyMatch = Regex.Match(text, anyIpPattern);
            if (anyMatch.Success)
            {
                return anyMatch.Groups[1].Value;
            }

            return string.Empty;
        }

        private bool IsValidIpAddress(string ipAddress)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                return false;

            // Use IPAddress.TryParse for comprehensive validation
            if (IPAddress.TryParse(ipAddress, out var ip))
            {
                // Ensure it's IPv4 and not loopback/multicast/broadcast for practical use
                var bytes = ip.GetAddressBytes();
                if (bytes.Length == 4)
                {
                    // Additional validation for each octet to be between 0-255
                    // IPAddress.TryParse already handles this, but let's be explicit
                    return bytes.All(b => b >= 0 && b <= 255);
                }
            }

            return false;
        }

        private void ShowIpValidationError(string message)
        {
            MessageBox.Show(this, message, "Invalid IP Address", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // New event handler for status button clicks
        private void StatusButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is Camera camera)
            {
                // Create and show the status detail dialog
                var statusDialog = new StatusDetailDialog(camera);
                statusDialog.Owner = this;
                statusDialog.ShowDialog();
            }
        }

        // Keep the old method for backward compatibility if needed
        private void StatusTextBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TextBlock textBlock && textBlock.Tag is Camera camera)
            {
                // Create and show the status detail dialog
                var statusDialog = new StatusDetailDialog(camera);
                statusDialog.Owner = this;
                statusDialog.ShowDialog();
            }
        }
    }
}