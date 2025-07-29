using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using wpfhikip.Discovery.Core;

namespace wpfhikip.Discovery.Models
{
    /// <summary>
    /// Represents a discovered device with the methods that found it
    /// </summary>
    public class DiscoveredDeviceWithMethods : INotifyPropertyChanged
    {
        private DiscoveredDevice _device;

        /// <summary>
        /// The discovered device
        /// </summary>
        public DiscoveredDevice Device
        {
            get => _device;
            set => SetProperty(ref _device, value);
        }

        /// <summary>
        /// Discovery methods that found this device
        /// </summary>
        public ObservableCollection<DiscoveryMethod> DiscoveryMethods { get; } = new();

        /// <summary>
        /// Display name for the device
        /// </summary>
        public string DisplayName => Device?.DisplayName ?? "Unknown Device";

        /// <summary>
        /// IP address as string
        /// </summary>
        public string IPAddress => Device?.IPAddress?.ToString() ?? "Unknown";

        /// <summary>
        /// Device type
        /// </summary>
        public string DeviceType => Device?.DeviceType.ToString() ?? "Unknown";

        /// <summary>
        /// Comma-separated list of discovery methods
        /// </summary>
        public string MethodsString => string.Join(", ", DiscoveryMethods.Select(m => m.GetDescription()));

        /// <summary>
        /// Number of discovery methods that found this device
        /// </summary>
        public int MethodCount => DiscoveryMethods.Count;

        public DiscoveredDeviceWithMethods(DiscoveredDevice device)
        {
            _device = device;

            // Initialize with device's discovery methods
            foreach (var method in device.DiscoveryMethods)
            {
                DiscoveryMethods.Add(method);
            }

            DiscoveryMethods.CollectionChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(MethodsString));
                OnPropertyChanged(nameof(MethodCount));
            };
        }

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