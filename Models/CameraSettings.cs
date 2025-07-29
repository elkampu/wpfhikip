namespace wpfhikip.Models
{
    public class CameraSettings : BaseNotifyPropertyChanged
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
    }
}