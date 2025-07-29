using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

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

            // Add focus handling for the entire control
            this.GotFocus += IpAddressControl_GotFocus;
            this.MouseLeftButtonDown += IpAddressControl_MouseLeftButtonDown;
            this.Focusable = true; // Make the control itself focusable
        }

        /// <summary>
        /// Focuses on the first octet and selects all text
        /// </summary>
        public void FocusFirstOctet()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                Octet1.Focus();
                Octet1.SelectAll();
            }));
        }

        private void IpAddressControl_GotFocus(object sender, RoutedEventArgs e)
        {
            // If no specific textbox has focus, focus the first one
            if (!Octet1.IsFocused && !Octet2.IsFocused && !Octet3.IsFocused && !Octet4.IsFocused)
            {
                FocusFirstOctet();
            }
        }

        private void IpAddressControl_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // If user clicks on the control but not on a specific textbox, focus the first one
            if (!Octet1.IsFocused && !Octet2.IsFocused && !Octet3.IsFocused && !Octet4.IsFocused)
            {
                FocusFirstOctet();
                e.Handled = true;
            }
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

            Octet1.PreviewKeyDown += TextBox_PreviewKeyDown;
            Octet2.PreviewKeyDown += TextBox_PreviewKeyDown;
            Octet3.PreviewKeyDown += TextBox_PreviewKeyDown;
            Octet4.PreviewKeyDown += TextBox_PreviewKeyDown;

            Octet1.KeyDown += TextBox_KeyDown;
            Octet2.KeyDown += TextBox_KeyDown;
            Octet3.KeyDown += TextBox_KeyDown;
            Octet4.KeyDown += TextBox_KeyDown;

            Octet1.GotFocus += TextBox_GotFocus;
            Octet2.GotFocus += TextBox_GotFocus;
            Octet3.GotFocus += TextBox_GotFocus;
            Octet4.GotFocus += TextBox_GotFocus;

            Octet1.LostFocus += TextBox_LostFocus;
            Octet2.LostFocus += TextBox_LostFocus;
            Octet3.LostFocus += TextBox_LostFocus;
            Octet4.LostFocus += TextBox_LostFocus;
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

            // Allow the input - we'll validate and correct in TextChanged event
        }

        private void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var textBox = sender as TextBox;

            // Handle backspace in empty field
            if (e.Key == Key.Back && string.IsNullOrEmpty(textBox.Text))
            {
                MoveToPreviousTextBox(textBox);
                e.Handled = true;
            }
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = sender as TextBox;
            var text = textBox.Text;

            // Validate and correct the value if it's > 255
            if (int.TryParse(text, out int value) && value > 255)
            {
                // Use Dispatcher to avoid recursion issues with TextChanged
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    textBox.Text = "255";
                    textBox.SelectAll();
                }));
                return;
            }

            // Check if we should auto-move to next field
            if (ShouldMoveToNextField(text))
            {
                MoveToNextTextBox(textBox);
            }

            UpdateIpAddressFromTextBoxes();
        }

        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var textBox = sender as TextBox;
            var text = textBox.Text;

            // Final validation when losing focus
            if (!string.IsNullOrEmpty(text) && int.TryParse(text, out int value) && value > 255)
            {
                textBox.Text = "255";
                UpdateIpAddressFromTextBoxes();
            }
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

                case Key.Tab:
                    // Let normal tab handling work
                    break;

                case Key.Enter:
                    // Move to next field on Enter
                    MoveToNextTextBox(textBox);
                    e.Handled = true;
                    break;
            }
        }

        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            var textBox = sender as TextBox;
            // Use Dispatcher to ensure text is selected after the focus event completes
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                textBox.SelectAll();
            }));
        }

        private bool ShouldMoveToNextField(string text)
        {
            if (string.IsNullOrEmpty(text) || !int.TryParse(text, out int value))
                return false;

            // Move if it's a 3-digit valid octet
            if (text.Length == 3 && IsValidOctet(text))
                return true;

            // Move if it's a 2-digit number that cannot accept another digit and still be valid
            // This happens when the value is 26-99 (adding another digit would exceed 255)
            if (text.Length == 2 && value >= 26)
                return true;

            return false;
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

            if (nextTextBox != null)
            {
                nextTextBox.Focus();
                nextTextBox.SelectAll();
            }
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