using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

using wpfhikip.Discovery.Core;

namespace wpfhikip.Discovery.Models
{
    /// <summary>
    /// Groups discovery results by discovery method
    /// </summary>
    public class DiscoveryResultsByMethod : INotifyPropertyChanged
    {
        private int _deviceCount;
        private bool _isExpanded = true;

        /// <summary>
        /// The discovery method
        /// </summary>
        public DiscoveryMethod Method { get; set; }

        /// <summary>
        /// Display name for the method
        /// </summary>
        public string MethodName => Method.GetDescription();

        /// <summary>
        /// Devices discovered by this method
        /// </summary>
        public ObservableCollection<DiscoveredDevice> Devices { get; } = new();

        /// <summary>
        /// Number of devices discovered by this method
        /// </summary>
        public int DeviceCount
        {
            get => _deviceCount;
            set => SetProperty(ref _deviceCount, value);
        }

        /// <summary>
        /// Whether this group is expanded in the UI
        /// </summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        public DiscoveryResultsByMethod()
        {
            Devices.CollectionChanged += (s, e) => DeviceCount = Devices.Count;
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