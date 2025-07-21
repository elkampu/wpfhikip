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

using wpfhikip.Models;
using wpfhikip.Views.Dialogs;

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