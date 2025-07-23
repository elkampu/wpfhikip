using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;

using wpfhikip.Discovery.Models;

namespace wpfhikip.Discovery.Core
{
    /// <summary>
    /// Represents a device discovered through network scanning
    /// </summary>
    public class DiscoveredDevice : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _manufacturer = string.Empty;
        private string _model = string.Empty;
        private string _firmwareVersion = string.Empty;
        private string _serialNumber = string.Empty;
        private string _macAddress = string.Empty;
        private DeviceType _deviceType = DeviceType.Unknown;
        private DateTime _lastSeen = DateTime.UtcNow;
        private bool _isOnline = true;
        private string _description = string.Empty;

        /// <summary>
        /// Unique identifier for the device (typically IP address or MAC address)
        /// </summary>
        public string UniqueId { get; set; } = string.Empty;

        /// <summary>
        /// Primary IP address of the device
        /// </summary>
        public IPAddress? IPAddress { get; set; }

        /// <summary>
        /// All known IP addresses for this device
        /// </summary>
        public List<IPAddress> IPAddresses { get; set; } = new();

        /// <summary>
        /// Primary port where the device was discovered
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// All discovered ports for this device
        /// </summary>
        public List<int> Ports { get; set; } = new();

        /// <summary>
        /// Device name or hostname
        /// </summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        /// <summary>
        /// Device manufacturer
        /// </summary>
        public string Manufacturer
        {
            get => _manufacturer;
            set => SetProperty(ref _manufacturer, value);
        }

        /// <summary>
        /// Device model
        /// </summary>
        public string Model
        {
            get => _model;
            set => SetProperty(ref _model, value);
        }

        /// <summary>
        /// Device firmware version
        /// </summary>
        public string FirmwareVersion
        {
            get => _firmwareVersion;
            set => SetProperty(ref _firmwareVersion, value);
        }

        /// <summary>
        /// Device serial number
        /// </summary>
        public string SerialNumber
        {
            get => _serialNumber;
            set => SetProperty(ref _serialNumber, value);
        }

        /// <summary>
        /// Device MAC address
        /// </summary>
        public string MACAddress
        {
            get => _macAddress;
            set => SetProperty(ref _macAddress, value);
        }

        /// <summary>
        /// Type of device (camera, router, printer, etc.)
        /// </summary>
        public DeviceType DeviceType
        {
            get => _deviceType;
            set => SetProperty(ref _deviceType, value);
        }

        /// <summary>
        /// Device description or additional information
        /// </summary>
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        /// <summary>
        /// When this device was last seen/discovered
        /// </summary>
        public DateTime LastSeen
        {
            get => _lastSeen;
            set => SetProperty(ref _lastSeen, value);
        }

        /// <summary>
        /// Whether the device is currently online/reachable
        /// </summary>
        public bool IsOnline
        {
            get => _isOnline;
            set => SetProperty(ref _isOnline, value);
        }

        /// <summary>
        /// Discovery methods that found this device
        /// </summary>
        public HashSet<DiscoveryMethod> DiscoveryMethods { get; set; } = new();

        /// <summary>
        /// Raw discovery data from different protocols
        /// </summary>
        public Dictionary<string, object> DiscoveryData { get; set; } = new();

        /// <summary>
        /// Services discovered on this device
        /// </summary>
        public Dictionary<string, DeviceService> Services { get; set; } = new();

        /// <summary>
        /// Device capabilities (supported protocols, features)
        /// </summary>
        public HashSet<string> Capabilities { get; set; } = new();

        /// <summary>
        /// Network segment this device belongs to
        /// </summary>
        public string NetworkSegment => IPAddress?.ToString().Substring(0, IPAddress.ToString().LastIndexOf('.')) + ".0/24" ?? "Unknown";

        /// <summary>
        /// Display name for UI
        /// </summary>
        public string DisplayName => !string.IsNullOrEmpty(Name) ? Name : IPAddress?.ToString() ?? UniqueId;

        /// <summary>
        /// Creates a basic DiscoveredDevice with IP address
        /// </summary>
        public DiscoveredDevice(IPAddress ipAddress)
        {
            IPAddress = ipAddress;
            UniqueId = ipAddress.ToString();
            IPAddresses.Add(ipAddress);
        }

        /// <summary>
        /// Creates a DiscoveredDevice with IP address and port
        /// </summary>
        public DiscoveredDevice(IPAddress ipAddress, int port) : this(ipAddress)
        {
            Port = port;
            if (port > 0 && !Ports.Contains(port))
                Ports.Add(port);
        }

        /// <summary>
        /// Parameterless constructor for serialization
        /// </summary>
        public DiscoveredDevice() { }

        /// <summary>
        /// Updates device information from another discovery result
        /// </summary>
        public void UpdateFrom(DiscoveredDevice other)
        {
            LastSeen = DateTime.UtcNow;

            // Update basic info if not already set or if other has better info
            if (string.IsNullOrEmpty(Name) && !string.IsNullOrEmpty(other.Name))
                Name = other.Name;

            if (string.IsNullOrEmpty(Manufacturer) && !string.IsNullOrEmpty(other.Manufacturer))
                Manufacturer = other.Manufacturer;

            if (string.IsNullOrEmpty(Model) && !string.IsNullOrEmpty(other.Model))
                Model = other.Model;

            if (string.IsNullOrEmpty(FirmwareVersion) && !string.IsNullOrEmpty(other.FirmwareVersion))
                FirmwareVersion = other.FirmwareVersion;

            if (string.IsNullOrEmpty(SerialNumber) && !string.IsNullOrEmpty(other.SerialNumber))
                SerialNumber = other.SerialNumber;

            if (string.IsNullOrEmpty(MACAddress) && !string.IsNullOrEmpty(other.MACAddress))
                MACAddress = other.MACAddress;

            if (DeviceType == DeviceType.Unknown && other.DeviceType != DeviceType.Unknown)
                DeviceType = other.DeviceType;

            if (string.IsNullOrEmpty(Description) && !string.IsNullOrEmpty(other.Description))
                Description = other.Description;

            // Merge collections
            foreach (var ip in other.IPAddresses)
            {
                if (!IPAddresses.Contains(ip))
                    IPAddresses.Add(ip);
            }

            foreach (var port in other.Ports)
            {
                if (!Ports.Contains(port))
                    Ports.Add(port);
            }

            foreach (var method in other.DiscoveryMethods)
            {
                DiscoveryMethods.Add(method);
            }

            foreach (var capability in other.Capabilities)
            {
                Capabilities.Add(capability);
            }

            // Merge discovery data
            foreach (var kvp in other.DiscoveryData)
            {
                DiscoveryData[kvp.Key] = kvp.Value;
            }

            // Merge services
            foreach (var kvp in other.Services)
            {
                Services[kvp.Key] = kvp.Value;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        public override string ToString()
        {
            return $"{DisplayName} ({IPAddress}) - {DeviceType}";
        }
    }

    /// <summary>
    /// Represents a service available on a discovered device
    /// </summary>
    public class DeviceService
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int Port { get; set; }
        public string Protocol { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public Dictionary<string, string> Properties { get; set; } = new();
    }
}