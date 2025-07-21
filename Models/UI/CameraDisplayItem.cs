using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows;

namespace wpfhikip.Models.UI
{
    /// <summary>
    /// UI representation of a camera for DataGrid display
    /// </summary>
    public class CameraDisplayItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        private string? _status;
        private string? _onlineStatus;
        private Brush? _rowColor;
        private Brush? _cellColor;
        private FontWeight _cellFontWeight = FontWeights.Normal;
        private bool _isCompleted;

        // Reference to the actual camera model
        public Camera Camera { get; set; } = new Camera();

        // UI-specific properties
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public string? Status
        {
            get => _status;
            set
            {
                SetProperty(ref _status, value);
                OnPropertyChanged(nameof(ShortStatus));
            }
        }

        /// <summary>
        /// Gets a shortened version of the status for display in the DataGrid
        /// </summary>
        public string? ShortStatus
        {
            get
            {
                if (string.IsNullOrEmpty(_status))
                    return _status;

                return _status.Length > 10 ? _status.Substring(0, 7) + "..." : _status;
            }
        }

        public string? OnlineStatus
        {
            get => _onlineStatus;
            set => SetProperty(ref _onlineStatus, value);
        }

        public Brush? RowColor
        {
            get => _rowColor;
            set => SetProperty(ref _rowColor, value);
        }

        public Brush? CellColor
        {
            get => _cellColor;
            set => SetProperty(ref _cellColor, value);
        }

        public FontWeight CellFontWeight
        {
            get => _cellFontWeight;
            set => SetProperty(ref _cellFontWeight, value);
        }

        public bool IsCompleted
        {
            get => _isCompleted;
            set => SetProperty(ref _isCompleted, value);
        }

        // Proxy properties for easier binding
        public CameraProtocol Protocol
        {
            get => Camera.Protocol;
            set
            {
                Camera.Protocol = value;
                OnPropertyChanged();
            }
        }

        public string? CurrentIP
        {
            get => Camera.CurrentIP;
            set
            {
                Camera.CurrentIP = value;
                OnPropertyChanged();
            }
        }

        public string? NewIP
        {
            get => Camera.NewIP;
            set
            {
                Camera.NewIP = value;
                OnPropertyChanged();
            }
        }

        public string? NewMask
        {
            get => Camera.NewMask;
            set
            {
                Camera.NewMask = value;
                OnPropertyChanged();
            }
        }

        public string? NewGateway
        {
            get => Camera.NewGateway;
            set
            {
                Camera.NewGateway = value;
                OnPropertyChanged();
            }
        }

        public string? NewNTPServer
        {
            get => Camera.NewNTPServer;
            set
            {
                Camera.NewNTPServer = value;
                OnPropertyChanged();
            }
        }

        public string? Username
        {
            get => Camera.Username;
            set
            {
                Camera.Username = value;
                OnPropertyChanged();
            }
        }

        public string? Password
        {
            get => Camera.Password;
            set
            {
                Camera.Password = value;
                OnPropertyChanged();
            }
        }

        public string? Port
        {
            get => Camera.Port;
            set
            {
                Camera.Port = value;
                OnPropertyChanged();
            }
        }

        public int EffectivePort => Camera.EffectivePort;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}