using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace wpfhikip.Controls
{
    /// <summary>
    /// Interaction logic for IpAddressControl.xaml
    /// </summary>
    public partial class IpAddressControl : UserControl
    {
        public static readonly DependencyProperty IpAddressProperty =
            DependencyProperty.Register(nameof(IpAddress), typeof(string), typeof(IpAddressControl),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnIpAddressChanged));

        public string IpAddress
        {
            get => (string)GetValue(IpAddressProperty);
            set => SetValue(IpAddressProperty, value);
        }

        public IpAddressControl()
        {
            InitializeComponent();
            SetupEventHandlers();
        }

        private static void OnIpAddressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is IpAddressControl control)
            {
                control.UpdateTextBoxesFromIpAddress();
            }
        }

        private void SetupEventHandlers()
        {
            // Add event handlers for all text boxes
            Octet1.TextChanged += TextBox_TextChanged;
            Octet2.TextChanged += TextBox_TextChanged;
            Octet3.TextChanged += TextBox_TextChanged;
            Octet4.TextChanged += TextBox_TextChanged;

            Octet1.PreviewTextInput += TextBox_PreviewTextInput;
            Octet2.PreviewTextInput += TextBox_PreviewTextInput;
            Octet3.PreviewTextInput += TextBox_PreviewTextInput;
            Octet4.PreviewTextInput += TextBox_PreviewTextInput;

            Octet1.KeyDown += TextBox_KeyDown;
            Octet2.KeyDown += TextBox_KeyDown;
            Octet3.KeyDown += TextBox_KeyDown;
            Octet4.KeyDown += TextBox_KeyDown;

            Octet1.GotFocus += TextBox_GotFocus;
            Octet2.GotFocus += TextBox_GotFocus;
            Octet3.GotFocus += TextBox_GotFocus;
            Octet4.GotFocus += TextBox_GotFocus;
        }

        private void TextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Only allow numeric input and dots
            if (e.Text == ".")
            {
                MoveToNextTextBox(sender as TextBox);
                e.Handled = true;
                return;
            }

            // Allow only numbers
            if (!IsNumeric(e.Text))
            {
                e.Handled = true;
                return;
            }

            // Check if adding this character would exceed 3 digits or make value > 255
            var textBox = sender as TextBox;
            var currentText = textBox.Text;
            var newText = currentText.Insert(textBox.CaretIndex, e.Text);

            if (newText.Length > 3 || !IsValidOctet(newText))
            {
                e.Handled = true;
            }
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = sender as TextBox;
            var text = textBox.Text;

            // Auto-move to next field when 3 digits are entered
            if (text.Length == 3 && IsValidOctet(text))
            {
                MoveToNextTextBox(textBox);
            }

            UpdateIpAddressFromTextBoxes();
        }

        private void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            var textBox = sender as TextBox;

            // Handle navigation keys
            switch (e.Key)
            {
                case Key.Right:
                    if (textBox.CaretIndex == textBox.Text.Length)
                    {
                        MoveToNextTextBox(textBox);
                        e.Handled = true;
                    }
                    break;

                case Key.Left:
                    if (textBox.CaretIndex == 0)
                    {
                        MoveToPreviousTextBox(textBox);
                        e.Handled = true;
                    }
                    break;

                case Key.Back:
                    if (textBox.CaretIndex == 0 && textBox.Text.Length == 0)
                    {
                        MoveToPreviousTextBox(textBox);
                        e.Handled = true;
                    }
                    break;

                case Key.Tab:
                    // Let normal tab handling work
                    break;
            }
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            var textBox = sender as TextBox;
            textBox.SelectAll();
        }

        private void MoveToNextTextBox(TextBox currentTextBox)
        {
            TextBox nextTextBox = currentTextBox.Name switch
            {
                nameof(Octet1) => Octet2,
                nameof(Octet2) => Octet3,
                nameof(Octet3) => Octet4,
                _ => null
            };

            nextTextBox?.Focus();
        }

        private void MoveToPreviousTextBox(TextBox currentTextBox)
        {
            TextBox previousTextBox = currentTextBox.Name switch
            {
                nameof(Octet2) => Octet1,
                nameof(Octet3) => Octet2,
                nameof(Octet4) => Octet3,
                _ => null
            };

            if (previousTextBox != null)
            {
                previousTextBox.Focus();
                previousTextBox.CaretIndex = previousTextBox.Text.Length;
            }
        }

        private void UpdateIpAddressFromTextBoxes()
        {
            var ip = $"{Octet1.Text}.{Octet2.Text}.{Octet3.Text}.{Octet4.Text}";

            // Only update if it's a valid format to avoid binding loops
            if (IsValidIpAddress(ip) || string.IsNullOrEmpty(Octet1.Text + Octet2.Text + Octet3.Text + Octet4.Text))
            {
                IpAddress = ip.Trim('.');
            }
        }

        private void UpdateTextBoxesFromIpAddress()
        {
            if (string.IsNullOrEmpty(IpAddress))
            {
                Octet1.Text = Octet2.Text = Octet3.Text = Octet4.Text = string.Empty;
                return;
            }

            var parts = IpAddress.Split('.');
            if (parts.Length == 4)
            {
                Octet1.Text = parts[0];
                Octet2.Text = parts[1];
                Octet3.Text = parts[2];
                Octet4.Text = parts[3];
            }
        }

        private static bool IsNumeric(string text)
        {
            return Regex.IsMatch(text, @"^\d+$");
        }

        private static bool IsValidOctet(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;

            if (int.TryParse(text, out int value))
            {
                return value >= 0 && value <= 255;
            }
            return false;
        }

        private static bool IsValidIpAddress(string ip)
        {
            var parts = ip.Split('.');
            if (parts.Length != 4) return false;

            foreach (var part in parts)
            {
                if (!IsValidOctet(part) || string.IsNullOrEmpty(part))
                    return false;
            }
            return true;
        }
    }
}
