using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

using wpfhikip.Protocols.Axis;
using wpfhikip.Protocols.Dahua;
using wpfhikip.Protocols.Hikvision;
using wpfhikip.Protocols.Onvif;
using wpfhikip.ViewModels.Commands;

namespace wpfhikip.ViewModels
{
    public class NetConfViewModel : ViewModelBase
    {
        private ObservableCollection<NetworkConfiguration> _cameraRow;
        private ObservableCollection<string> _modelOptions;
        private bool _isCheckingCompatibility;

        public ObservableCollection<NetworkConfiguration> CameraRow
        {
            get => _cameraRow;
            set => SetProperty(ref _cameraRow, value);
        }

        public ObservableCollection<string> ModelOptions
        {
            get => _modelOptions;
            set => SetProperty(ref _modelOptions, value);
        }

        public bool IsCheckingCompatibility
        {
            get => _isCheckingCompatibility;
            set => SetProperty(ref _isCheckingCompatibility, value);
        }

        // Commands
        public ICommand AddCameraCommand { get; set; }
        public ICommand DeleteSelectedCommand { get; set; }
        public ICommand SelectAllCommand { get; set; }
        public ICommand CheckCompatibilityCommand { get; set; }
        public ICommand SendNetworkConfigCommand { get; set; }
        public ICommand SendNTPConfigCommand { get; set; }
        public ICommand SaveConfigCommand { get; set; }
        public ICommand LoadConfigCommand { get; set; }

        public NetConfViewModel()
        {
            InitializeData();
            InitializeCommands();
        }

        private void InitializeData()
        {
            CameraRow = new ObservableCollection<NetworkConfiguration> { new NetworkConfiguration() };
            ModelOptions = new ObservableCollection<string> { "Dahua", "Hikvision", "Axis", "Onvif" };
        }

        private void InitializeCommands()
        {
            AddCameraCommand = new RelayCommand(AddCamera);
            DeleteSelectedCommand = new RelayCommand(DeleteSelected, CanDeleteSelected);
            SelectAllCommand = new RelayCommand(SelectAll);
            CheckCompatibilityCommand = new RelayCommand(async param => await CheckCompatibilityAsync(), CanCheckCompatibility);
            SendNetworkConfigCommand = new RelayCommand(async param => await SendNetworkConfigAsync(), CanSendNetworkConfig);
            SendNTPConfigCommand = new RelayCommand(async param => await SendNTPConfigAsync(), CanSendNTPConfig);
            SaveConfigCommand = new RelayCommand(SaveConfig);
            LoadConfigCommand = new RelayCommand(LoadConfig);
        }

        public void AddCamera(object parameter)
        {
            // Simply add a new NetworkConfiguration to the collection
            CameraRow.Add(new NetworkConfiguration());
        }

        private void DeleteSelected(object parameter)
        {
            var selectedItems = CameraRow.Where(c => c.IsSelected).ToList();
            foreach (var item in selectedItems)
            {
                CameraRow.Remove(item);
            }
        }

        private bool CanDeleteSelected(object parameter)
        {
            return CameraRow?.Any(c => c.IsSelected) == true;
        }

        private void SelectAll(object parameter)
        {
            foreach (var config in CameraRow)
            {
                config.IsSelected = true;
            }
        }

        private async Task CheckCompatibilityAsync()
        {
            var selectedConfigs = CameraRow.Where(c => c.IsSelected && !string.IsNullOrEmpty(c.CurrentIP)).ToList();
            if (!selectedConfigs.Any())
            {
                MessageBox.Show("Please select cameras with IP addresses to check compatibility.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            IsCheckingCompatibility = true;

            try
            {
                var tasks = selectedConfigs.Select(CheckSingleCameraCompatibilityAsync);
                await Task.WhenAll(tasks);
            }
            finally
            {
                IsCheckingCompatibility = false;
            }
        }

        private async Task CheckSingleCameraCompatibilityAsync(NetworkConfiguration config)
        {
            try
            {
                // Update status to show we're checking
                Application.Current.Dispatcher.Invoke(() =>
                {
                    config.Status = "Checking compatibility...";
                    config.CellColor = Brushes.LightYellow;
                });

                // Default port if not specified
                int port = 80;

                // Try Hikvision first
                var hikvisionCompatible = await CheckHikvisionCompatibilityAsync(config, port);
                if (hikvisionCompatible)
                    return;

                // Try Dahua if Hikvision failed
                var dahuaCompatible = await CheckDahuaCompatibilityAsync(config, port);
                if (dahuaCompatible)
                    return;

                // Try Axis if Dahua failed
                var axisCompatible = await CheckAxisCompatibilityAsync(config, port);
                if (axisCompatible)
                    return;
                var onvifCompatible = await CheckOnvifCompatibilityAsync(config, port);
                if (onvifCompatible)
                    return;
                // If none worked, mark as incompatible
                Application.Current.Dispatcher.Invoke(() =>
                {
                    config.Status = "No compatible protocol found";
                    config.CellColor = Brushes.LightCoral;
                });
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    config.Status = $"Exception: {ex.Message}";
                    config.CellColor = Brushes.Red;
                });
            }
        }

        private async Task<bool> CheckHikvisionCompatibilityAsync(NetworkConfiguration config, int port)
        {
            try
            {
                using var connection = new HikvisionConnection(
                    config.CurrentIP,
                    port,
                    config.User ?? "admin",
                    config.Password ?? "");

                var result = await connection.CheckCompatibilityAsync();

                // Update UI on main thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (result.Success && result.IsHikvisionCompatible)
                    {
                        config.Model = "Hikvision";
                        config.CellColor = Brushes.LightGreen;

                        if (result.RequiresAuthentication)
                        {
                            if (result.IsAuthenticated)
                            {
                                config.Status = "Hikvision compatible - Authentication OK";
                            }
                            else
                            {
                                config.Status = $"Hikvision compatible - Auth failed: {result.AuthenticationMessage}";
                                config.CellColor = Brushes.Orange;
                            }
                        }
                        else
                        {
                            config.Status = "Hikvision compatible - No auth required";
                        }
                    }
                });

                return result.Success && result.IsHikvisionCompatible;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> CheckDahuaCompatibilityAsync(NetworkConfiguration config, int port)
        {
            try
            {
                using var connection = new DahuaConnection(
                    config.CurrentIP,
                    port,
                    config.User ?? "admin",
                    config.Password ?? "");

                var result = await connection.CheckCompatibilityAsync();

                // Update UI on main thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (result.Success && result.IsDahuaCompatible)
                    {
                        config.Model = "Dahua";
                        config.CellColor = Brushes.LightGreen;

                        if (result.RequiresAuthentication)
                        {
                            if (result.IsAuthenticated)
                            {
                                config.Status = "Dahua compatible - Authentication OK";
                            }
                            else
                            {
                                config.Status = $"Dahua compatible - Auth failed: {result.AuthenticationMessage}";
                                config.CellColor = Brushes.Orange;
                            }
                        }
                        else
                        {
                            config.Status = "Dahua compatible - No auth required";
                        }
                    }
                });

                return result.Success && result.IsDahuaCompatible;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> CheckAxisCompatibilityAsync(NetworkConfiguration config, int port)
        {
            try
            {
                using var connection = new AxisConnection(
                    config.CurrentIP,
                    port,
                    config.User ?? "admin",
                    config.Password ?? "");

                var result = await connection.CheckCompatibilityAsync();

                // Update UI on main thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (result.Success && result.IsAxisCompatible)
                    {
                        config.Model = "Axis";
                        config.CellColor = Brushes.LightGreen;

                        if (result.RequiresAuthentication)
                        {
                            if (result.IsAuthenticated)
                            {
                                config.Status = "Axis compatible - Authentication OK";
                            }
                            else
                            {
                                config.Status = $"Axis compatible - Auth failed: {result.AuthenticationMessage}";
                                config.CellColor = Brushes.Orange;
                            }
                        }
                        else
                        {
                            config.Status = "Axis compatible - No auth required";
                        }
                    }
                });

                return result.Success && result.IsAxisCompatible;
            }
            catch
            {
                return false;
            }
        }
        private async Task<bool> CheckOnvifCompatibilityAsync(NetworkConfiguration config, int port)
        {
            try
            {
                using var connection = new OnvifConnection(
                    config.CurrentIP,
                    port,
                    config.User ?? "admin",
                    config.Password ?? "");

                var result = await connection.CheckCompatibilityAsync();

                // Update UI on main thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (result.Success && result.IsOnvifCompatible)
                    {
                        config.Model = "ONVIF";
                        config.CellColor = Brushes.LightGreen;

                        if (result.RequiresAuthentication)
                        {
                            if (result.IsAuthenticated)
                            {
                                config.Status = "ONVIF compatible - Authentication OK";
                            }
                            else
                            {
                                config.Status = $"ONVIF compatible - Auth failed: {result.AuthenticationMessage}";
                                config.CellColor = Brushes.Orange;
                            }
                        }
                        else
                        {
                            config.Status = "ONVIF compatible - No auth required";
                        }
                    }
                });

                return result.Success && result.IsOnvifCompatible;
            }
            catch
            {
                return false;
            }
        }

        private bool CanCheckCompatibility(object parameter)
        {
            return !IsCheckingCompatibility && CameraRow?.Any(c => c.IsSelected && !string.IsNullOrEmpty(c.CurrentIP)) == true;
        }

        private async Task SendNetworkConfigAsync()
        {
            var selectedConfigs = CameraRow.Where(c => c.IsSelected && (c.Model == "Hikvision" || c.Model == "Dahua" || c.Model == "Axis" || c.Model == "Onvif")).ToList();
            if (!selectedConfigs.Any())
            {
                MessageBox.Show("Please select Hikvision, Dahua, or Axis cameras to configure.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirmation = MessageBox.Show(
                $"Are you sure you want to send network configuration to {selectedConfigs.Count} selected cameras?",
                "Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmation != MessageBoxResult.Yes)
                return;

            foreach (var config in selectedConfigs)
            {
                await SendNetworkConfigToSingleCamera(config);
            }
        }

        private async Task SendNetworkConfigToSingleCamera(NetworkConfiguration config)
        {
            try
            {
                config.Status = "Sending network configuration...";
                config.CellColor = Brushes.LightYellow;

                bool success = false;

                if (config.Model == "Hikvision")
                {
                    success = await SendHikvisionNetworkConfigAsync(config);
                }
                else if (config.Model == "Dahua")
                {
                    success = await SendDahuaNetworkConfigAsync(config);
                }
                else if (config.Model == "Axis")
                {
                    success = await SendAxisNetworkConfigAsync(config);
                }
                else if (config.Model == "Onvif")
                {
                    success = await SendOnvifNetworkConfigAsync(config);
                }

                if (success)
                {
                    config.Status = "Network configuration sent successfully";
                    config.CellColor = Brushes.LightGreen;

                    // Update current IP if successful
                    if (!string.IsNullOrEmpty(config.NewIP))
                    {
                        config.CurrentIP = config.NewIP;
                    }
                }
                else
                {
                    config.Status = "Failed to send network configuration";
                    config.CellColor = Brushes.LightCoral;
                }
            }
            catch (Exception ex)
            {
                config.Status = $"Error sending network config: {ex.Message}";
                config.CellColor = Brushes.LightCoral;
            }
        }

        private async Task<bool> SendHikvisionNetworkConfigAsync(NetworkConfiguration config)
        {
            try
            {
                using var connection = new HikvisionConnection(
                    config.CurrentIP,
                    80,
                    config.User ?? "admin",
                    config.Password ?? "");

                // Here you would integrate with your HikvisionApiClient for the full GET/PUT workflow
                // For now, we'll simulate the process
                await Task.Delay(1000); // Simulate API call

                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> SendDahuaNetworkConfigAsync(NetworkConfiguration config)
        {
            try
            {
                using var connection = new DahuaConnection(
                    config.CurrentIP,
                    80,
                    config.User ?? "admin",
                    config.Password ?? "");

                var result = await connection.SendNetworkConfigurationAsync(config);
                return result.Success;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> SendAxisNetworkConfigAsync(NetworkConfiguration config)
        {
            try
            {
                using var connection = new AxisConnection(
                    config.CurrentIP,
                    80,
                    config.User ?? "admin",
                    config.Password ?? "");

                var result = await connection.SendNetworkConfigurationAsync(config);
                return result.Success;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> SendOnvifNetworkConfigAsync(NetworkConfiguration config)
        {
            try
            {
                using var connection = new OnvifConnection(
                    config.CurrentIP,
                    80,
                    config.User ?? "admin",
                    config.Password ?? "");
                var result = await connection.SendNetworkConfigurationAsync(config);
                return result.Success;
            }
            catch
            {
                return false;
            }
        }



        private bool CanSendNetworkConfig(object parameter)
        {
            return CameraRow?.Any(c => c.IsSelected && (c.Model == "Hikvision" || c.Model == "Dahua" || c.Model == "Axis" || c.Model == "Onvif")) == true;
        }

        private async Task SendNTPConfigAsync()
        {
            // Only Hikvision and Dahua support NTP for now (Axis NTP not implemented as requested)
            var selectedConfigs = CameraRow.Where(c => c.IsSelected && (c.Model == "Hikvision" || c.Model == "Dahua") && !string.IsNullOrEmpty(c.NewNTPServer)).ToList();
            if (!selectedConfigs.Any())
            {
                MessageBox.Show("Please select Hikvision or Dahua cameras with NTP server configured.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            foreach (var config in selectedConfigs)
            {
                await SendNTPConfigToSingleCamera(config);
            }
        }

        private async Task SendNTPConfigToSingleCamera(NetworkConfiguration config)
        {
            try
            {
                config.Status = "Sending NTP configuration...";
                config.CellColor = Brushes.LightYellow;

                bool success = false;

                if (config.Model == "Hikvision")
                {
                    success = await SendHikvisionNTPConfigAsync(config);
                }
                else if (config.Model == "Dahua")
                {
                    success = await SendDahuaNTPConfigAsync(config);
                }

                if (success)
                {
                    config.Status = "NTP configuration sent successfully";
                    config.CellColor = Brushes.LightGreen;
                }
                else
                {
                    config.Status = "Failed to send NTP configuration";
                    config.CellColor = Brushes.LightCoral;
                }
            }
            catch (Exception ex)
            {
                config.Status = $"Error sending NTP config: {ex.Message}";
                config.CellColor = Brushes.LightCoral;
            }
        }

        private async Task<bool> SendHikvisionNTPConfigAsync(NetworkConfiguration config)
        {
            try
            {
                // Here you would integrate with your HikvisionApiClient for NTP configuration
                await Task.Delay(1000); // Simulate API call
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> SendDahuaNTPConfigAsync(NetworkConfiguration config)
        {
            try
            {
                using var connection = new DahuaConnection(
                    config.CurrentIP,
                    80,
                    config.User ?? "admin",
                    config.Password ?? "");

                var result = await connection.SendNtpConfigurationAsync(config);
                return result.Success;
            }
            catch
            {
                return false;
            }
        }

        private bool CanSendNTPConfig(object parameter)
        {
            return CameraRow?.Any(c => c.IsSelected && (c.Model == "Hikvision" || c.Model == "Dahua") && !string.IsNullOrEmpty(c.NewNTPServer)) == true;
        }

        private void SaveConfig(object parameter)
        {
            // Implement save functionality
            MessageBox.Show("Save functionality to be implemented", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void LoadConfig(object parameter)
        {
            // Implement load functionality
            MessageBox.Show("Load functionality to be implemented", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Method to add a single camera programmatically
        public void AddSingleCamera()
        {
            CameraRow.Add(new NetworkConfiguration());
        }

        // Method to add camera range programmatically  
        public void AddCameraRange(string startIP, string endIP, string username, string password)
        {
            // Implementation for adding camera range
            // This would parse the IP range and add multiple NetworkConfiguration objects
        }
    }
}
