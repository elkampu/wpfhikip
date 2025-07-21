using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace wpfhikip.Models
{
    public class CameraSettings : INotifyPropertyChanged
    {
        private string? _ipAddress;
        private string? _subnetMask;
        private string? _defaultGateway;
        private string? _dns1;
        private string? _dns2;
        private bool _ntpEnabled;
        private bool _ntpSynced;
        private string? _ntpServer;
        private string? _timeZone;
        private string? _dstEnabled;
        private string? _dstSettings;

        public string? IPAddress
        {
            get => _ipAddress;
            set => SetProperty(ref _ipAddress, value);
        }

        public string? SubnetMask
        {
            get => _subnetMask;
            set => SetProperty(ref _subnetMask, value);
        }

        public string? DefaultGateway
        {
            get => _defaultGateway;
            set => SetProperty(ref _defaultGateway, value);
        }

        public string? DNS1
        {
            get => _dns1;
            set => SetProperty(ref _dns1, value);
        }

        public string? DNS2
        {
            get => _dns2;
            set => SetProperty(ref _dns2, value);
        }

        public bool NTPEnabled
        {
            get => _ntpEnabled;
            set => SetProperty(ref _ntpEnabled, value);
        }

        public bool NTPSynced
        {
            get => _ntpSynced;
            set => SetProperty(ref _ntpSynced, value);
        }

        public string? NTPServer
        {
            get => _ntpServer;
            set => SetProperty(ref _ntpServer, value);
        }

        public string? TimeZone
        {
            get => _timeZone;
            set => SetProperty(ref _timeZone, value);
        }

        public string? DSTEnabled
        {
            get => _dstEnabled;
            set => SetProperty(ref _dstEnabled, value);
        }

        public string? DSTSettings
        {
            get => _dstSettings;
            set => SetProperty(ref _dstSettings, value);
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
    }
}