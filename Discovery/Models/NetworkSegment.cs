using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;

using wpfhikip.Discovery.Core;

namespace wpfhikip.Discovery.Models
{
    /// <summary>
    /// Represents a network segment (subnet) for discovery
    /// </summary>
    public class NetworkSegment : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        private string _description = string.Empty;

        /// <summary>
        /// Network address in CIDR notation (e.g., "192.168.1.0/24")
        /// </summary>
        public string Network { get; set; } = string.Empty;

        /// <summary>
        /// Network interface description
        /// </summary>
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        /// <summary>
        /// Whether this network segment is selected for scanning
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        /// <summary>
        /// Network interface ID
        /// </summary>
        public string InterfaceId { get; set; } = string.Empty;

        /// <summary>
        /// Network interface name
        /// </summary>
        public string InterfaceName { get; set; } = string.Empty;

        /// <summary>
        /// Network interface type
        /// </summary>
        public string InterfaceType { get; set; } = string.Empty;

        /// <summary>
        /// Interface speed in bits per second
        /// </summary>
        public long Speed { get; set; }

        /// <summary>
        /// MAC address of the interface
        /// </summary>
        public string MacAddress { get; set; } = string.Empty;

        /// <summary>
        /// Network address information
        /// </summary>
        public NetworkAddressInfo? AddressInfo { get; set; }

        /// <summary>
        /// Estimated number of hosts in this segment
        /// </summary>
        public int EstimatedHostCount
        {
            get
            {
                if (AddressInfo == null) return 0;
                var hostBits = 32 - AddressInfo.PrefixLength;
                return Math.Max(0, (int)Math.Pow(2, hostBits) - 2); // Subtract network and broadcast
            }
        }

        /// <summary>
        /// Display name combining network and description
        /// </summary>
        public string DisplayName => $"{Network} - {Description}";

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

        public override string ToString()
        {
            return DisplayName;
        }
    }
}