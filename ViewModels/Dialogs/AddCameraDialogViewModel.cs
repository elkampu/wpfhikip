using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using wpfhikip.ViewModels.Commands;

namespace wpfhikip.ViewModels.Dialogs
{
    public class AddCameraRangeDialogViewModel : ViewModelBase
    {
        private string _ipAddress;
        private string _pingResult;
        private bool _isLoginVisible;
        private string _username;
        private string _password;
        private readonly DispatcherTimer _typingTimer;

        public string IpAddress
        {
            get => _ipAddress;
            set
            {
                if (SetProperty(ref _ipAddress, value))
                {
                    if (ValidateIPv4(IpAddress))
                    {
                        _typingTimer.Stop();
                        _typingTimer.Start();
                    }
                    else
                    {
                        PingResult = "Invalid IP address format.";
                        IsLoginVisible = false;
                    }
                }
            }
        }

        public string PingResult
        {
            get => _pingResult;
            set => SetProperty(ref _pingResult, value);
        }

        public bool IsLoginVisible
        {
            get => _isLoginVisible;
            set => SetProperty(ref _isLoginVisible, value);
        }

        public string Username
        {
            get => _username;
            set => SetProperty(ref _username, value);
        }

        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        public ICommand LoginCommand { get; }

        public AddCameraRangeDialogViewModel()
        {
            _typingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _typingTimer.Tick += async (s, e) => await CheckReachability();

            LoginCommand = new RelayCommand(ExecuteLogin);
        }

        private async Task CheckReachability()
        {
            _typingTimer.Stop();

            if (string.IsNullOrWhiteSpace(IpAddress))
            {
                PingResult = "Please enter a valid IP address.";
                IsLoginVisible = false;
                return;
            }

            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(IpAddress, 1000);
                if (reply.Status == IPStatus.Success)
                {
                    PingResult = "Camera is reachable.";
                    IsLoginVisible = true;
                }
                else
                {
                    PingResult = "Camera is not reachable.";
                    IsLoginVisible = false;
                }
            }
            catch (Exception ex)
            {
                PingResult = $"Error: {ex.Message}";
                IsLoginVisible = false;
            }
        }
        public bool ValidateIPv4(string ipString)
        {
            if (String.IsNullOrWhiteSpace(ipString))
            {
                return false;
            }

            string[] splitValues = ipString.Split('.');
            if (splitValues.Length != 4)
            {
                return false;
            }

            byte tempForParsing;

            return splitValues.All(r => byte.TryParse(r, out tempForParsing));
        }
        private void ExecuteLogin(object parameter)
        {
            // Lógica para manejar el inicio de sesión
        }
    }
}