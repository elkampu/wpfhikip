using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace wpfhikip.Discovery.Models
{
    /// <summary>
    /// Represents a discovery method item for UI binding
    /// </summary>
    public class DiscoveryMethodItem : INotifyPropertyChanged
    {
        private DiscoveryMethod _method;
        private bool _isSelected;
        private bool _isEnabled = true;
        private bool _isRunning;
        private string _progress = string.Empty;
        private string _categoryName = string.Empty;

        /// <summary>
        /// The discovery method
        /// </summary>
        public DiscoveryMethod Method
        {
            get => _method;
            set => SetProperty(ref _method, value);
        }

        /// <summary>
        /// Display name for the method
        /// </summary>
        public string Name => Method.GetDescription();

        /// <summary>
        /// Category name for the method
        /// </summary>
        public string CategoryName
        {
            get => _categoryName;
            set => SetProperty(ref _categoryName, value);
        }

        /// <summary>
        /// Whether this method is selected for discovery
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        /// <summary>
        /// Whether this method is available/enabled
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        /// <summary>
        /// Whether this method is currently running
        /// </summary>
        public bool IsRunning
        {
            get => _isRunning;
            set => SetProperty(ref _isRunning, value);
        }

        /// <summary>
        /// Current progress/status text
        /// </summary>
        public string Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        /// <summary>
        /// Whether this method requires a network range to be specified
        /// </summary>
        public bool RequiresNetworkRange => Method switch
        {
            DiscoveryMethod.ICMP or
            DiscoveryMethod.PortScan or
            DiscoveryMethod.SNMP or
            DiscoveryMethod.HTTP or
            DiscoveryMethod.SSH or
            DiscoveryMethod.Telnet => true,
            _ => false
        };

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}