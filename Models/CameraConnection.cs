namespace wpfhikip.Models
{
    public class CameraConnection : BaseNotifyPropertyChanged
    {
        private string? _ipAddress;
        private string? _port;
        private string? _username;
        private string? _password;

        public string? IPAddress
        {
            get => _ipAddress;
            set => SetProperty(ref _ipAddress, value);
        }

        public string? Port
        {
            get => _port;
            set => SetProperty(ref _port, value);
        }

        public string? Username
        {
            get => _username;
            set => SetProperty(ref _username, value);
        }

        public string? Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }
    }
}