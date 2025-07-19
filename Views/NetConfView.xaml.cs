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
                var row = (NetworkConfiguration)dataGrid.CurrentItem;
                var property = typeof(NetworkConfiguration).GetProperty(column.SortMemberPath);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(row, clipboardText);
                }
            }
        }
    }
}
