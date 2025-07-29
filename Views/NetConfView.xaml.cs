using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

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

                // Check if this is one of the IP Address columns
                if (columnHeader == "Current IP" || columnHeader == "New IP" ||
                    columnHeader == "Mask" || columnHeader == "Gateway" || columnHeader == "NTP")
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
                var cellInfo = dataGrid.CurrentCell;
                if (cellInfo.Column != null)
                {
                    var content = cellInfo.Column.GetCellContent(cellInfo.Item);
                    if (content is TextBlock textBlock)
                    {
                        Clipboard.SetText(textBlock.Text);
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

        private void PasteToSelectedCell()
        {
            if (dataGrid.CurrentCell.Item != null && Clipboard.ContainsText())
            {
                var clipboardText = Clipboard.GetText();
                var column = dataGrid.CurrentCell.Column;
                var row = (Camera)dataGrid.CurrentItem;
                var property = typeof(Camera).GetProperty(column.SortMemberPath);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(row, clipboardText);
                }
            }
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