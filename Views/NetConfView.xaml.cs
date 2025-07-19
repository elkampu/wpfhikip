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

using wpfhikip.Controls;
using wpfhikip.Views.Dialogs;

namespace wpfhikip.Views
{
    /// <summary>
    /// Lógica de interacción para NetConfView.xaml
    /// </summary>
    public partial class NetConfView : Window
    {
        public NetConfView()
        {
            InitializeComponent();
        }

        private void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // COPY
            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                var cellInfo = dataGrid.CurrentCell;
                if (cellInfo.Column != null)
                {
                    var content = cellInfo.Column.GetCellContent(cellInfo.Item);
                    if (content is TextBlock textBlock)
                    {
                        Clipboard.SetText(textBlock.Text);
                        e.Handled = true;
                    }
                    else if (content is IpAddressControl ipControl)
                    {
                        Clipboard.SetText(ipControl.IpAddress ?? string.Empty);
                        e.Handled = true;
                    }
                }
            }
            // PASTE
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                PasteToSelectedCell();
                e.Handled = true;
            }
        }
        private void StatusTextBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Get the TextBlock that was clicked
            if (sender is TextBlock textBlock && textBlock.Tag is NetworkConfiguration config)
            {
                // Open the status detail dialog
                var statusDialog = new StatusDetailDialog(config)
                {
                    Owner = this
                };
                statusDialog.ShowDialog();
            }
        }

        private void PasteToSelectedCell()
        {
            if (dataGrid.CurrentCell.Item != null && Clipboard.ContainsText())
            {
                var clipboardText = Clipboard.GetText();
                var column = dataGrid.CurrentCell.Column;
                var row = (NetworkConfiguration)dataGrid.CurrentItem;

                // Handle template columns with IP controls
                if (column is DataGridTemplateColumn templateColumn)
                {
                    var headerText = templateColumn.Header?.ToString();
                    switch (headerText)
                    {
                        case "Current IP":
                            if (IsValidIpAddress(clipboardText))
                                row.CurrentIP = clipboardText;
                            break;
                        case "New IP":
                            if (IsValidIpAddress(clipboardText))
                                row.NewIP = clipboardText;
                            break;
                        case "New Mask":
                            if (IsValidIpAddress(clipboardText))
                                row.NewMask = clipboardText;
                            break;
                        case "New Gateway":
                            if (IsValidIpAddress(clipboardText))
                                row.NewGateway = clipboardText;
                            break;
                        case "New NTP Server":
                            if (IsValidIpAddress(clipboardText))
                                row.NewNTPServer = clipboardText;
                            break;
                    }
                }
                // Handle regular text columns
                else if (column is DataGridTextColumn textColumn)
                {
                    var property = typeof(NetworkConfiguration).GetProperty(textColumn.SortMemberPath);
                    if (property != null && property.CanWrite)
                    {
                        property.SetValue(row, clipboardText);
                    }
                }
            }
        }

        private bool IsValidIpAddress(string ip)
        {
            if (string.IsNullOrEmpty(ip)) return true; // Allow empty values

            var parts = ip.Split('.');
            if (parts.Length != 4) return false;

            foreach (var part in parts)
            {
                if (!int.TryParse(part, out int value) || value < 0 || value > 255)
                    return false;
            }
            return true;
        }
    }
}
