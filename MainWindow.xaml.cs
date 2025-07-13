using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.DirectoryServices.ActiveDirectory;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace wpfhikip
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        public ObservableCollection<NetworkConfiguration> Configurations { get; set; }
        public ObservableCollection<string> ModelOptions { get; set; }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            Configurations = new ObservableCollection<NetworkConfiguration> { new NetworkConfiguration() };
            ModelOptions = new ObservableCollection<string> { "Dahua", "Hikvision", "Axis" };
            //StartSearch();
            timer = new DispatcherTimer();
            InitializeTimer();

            //Task.Run(async () => await TestDeviceDiscoveryCancellation());
        }

        //public async Task<IEnumerable<Device>> StartSearch()
        //{
        //    var discovery = new Ssdp();
        //    var devices = await discovery.SearchUPnPDevicesAsync("MediaRenderer");
        //    foreach (var device in devices)
        //    {
        //        // Write device information to a text file
        //        string deviceInfo = $"Device: {device.ToString()}";
        //        System.IO.File.AppendAllText("devices.txt", deviceInfo);

        //    }
        //    return devices;
        //}


        private DispatcherTimer timer;

        private void InitializeTimer()
        {
            try
            {
                timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(5);
                timer.Tick += async (sender, e) => await TimerElapsedAsync(sender, e);
                timer.Start();
            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error initializing timer", ex);
            }
        }

        private async Task TimerElapsedAsync(object? sender, EventArgs e)
        {
            var tasks = Configurations.Select(config => CheckAndUpdateConnectivityAsync(config));
            await Task.WhenAll(tasks);
        }

        private async Task CheckAndUpdateConnectivityAsync(NetworkConfiguration config)
        {
            var isOnline = await CheckConnectivityAsync(config);
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (isOnline)
                {
                    config.OnlineStatus = "Online";
                    config.CellColor = Brushes.LightGreen;
                }
                else
                {
                    config.OnlineStatus = "Offline";
                    config.CellColor = Brushes.LightCoral;
                }
            });
        }
        private async Task<bool> CheckConnectivityAsync(NetworkConfiguration config)
        {
            if (string.IsNullOrEmpty(config.CurrentIP))
                return false;
            try
            {
                var addressess = await Dns.GetHostAddressesAsync(config.CurrentIP);
                if (addressess.Length > 0)
                {
                    using (var ping = new Ping())
                    {
                        var reply = await ping.SendPingAsync(addressess[0], 2000);
                        if (reply.Status == IPStatus.Success)
                            return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
        //public async Task TestDeviceDiscoveryCancellation()
        //{
        //    var ssdpDiscovery = new SsdpDiscovery();
        //    var cancellationTokenSource = new CancellationTokenSource();

        //    // Inicia la búsqueda de dispositivos UPnP con cancelación.
        //    var searchTask = ssdpDiscovery.SearchUPnPDevicesAsync("ssdp:all", 1, cancellationTokenSource.Token);

        //    // Espera un breve período de tiempo y luego cancela la búsqueda.
        //    await Task.Delay(1000); // Ajusta este tiempo según sea necesario.
        //    cancellationTokenSource.Cancel();

        //    try
        //    {
        //        // Intenta obtener el resultado de la tarea de búsqueda.
        //        var devices = await searchTask;
        //        Console.WriteLine($"Se encontraron {devices.Count()} dispositivos antes de la cancelación.");
        //    }
        //    catch (OperationCanceledException)
        //    {
        //        Console.WriteLine("La búsqueda de dispositivos fue cancelada exitosamente.");
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"Error durante la búsqueda de dispositivos: {ex.Message}");
        //    }
        //}
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private void AddRowButton_Click(object sender, RoutedEventArgs e)
        {
            Configurations.Add(new NetworkConfiguration());
        }

        private void DeleteSelectedRowsButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedConfigs = Configurations.Where(c => c.IsSelected).ToList();
            foreach (var config in selectedConfigs)
            {
                Configurations.Remove(config);
            }
        }

        private void ImportCSVFileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv",
                    Title = "Select a CSV file"
                };
                if (openFileDialog.ShowDialog() == true)
                {
                    string[] lines = System.IO.File.ReadAllLines(openFileDialog.FileName);
                    if (lines.Length != 0 && lines[0].Split(';').Length != 8)
                    {
                        MessageBox.Show("Incompatible data format", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    Configurations.Clear();
                    foreach (string line in lines)
                    {
                        string[] values = line.Split(';');
                        if (values.Length == 8)
                        {
                            Configurations.Add(new NetworkConfiguration
                            {
                                Model = values[0],
                                CurrentIP = values[1],
                                NewIP = values[2],
                                NewMask = values[3],
                                NewGateway = values[4],
                                NewNTPServer = values[5],
                                User = values[6],
                                Password = values[7]
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ShowErrorMessage("Error importing CSV file", ex);
            }
        }

        private async void SendNetworkConfigRequestButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedConfigs = Configurations.Where(c => c.IsSelected).ToList();
            if (selectedConfigs.Count == 0)
                return;

            StringBuilder requestBuilder = new StringBuilder();
            foreach (var config in selectedConfigs)
            {
                string request = GetNetworkConfigRequestString(config);
                requestBuilder.AppendLine(request);
            }

            bool confirmation = MessageBox.Show("Are you sure you want to send these requests?\n\n" + requestBuilder.ToString(), "Confirmation", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
            if (!confirmation)
                return;

            foreach (var config in selectedConfigs)
            {
                config.Status = "Sending request...";
                await SendNetworkConfigRequest(config);
            }
        }
        
        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var config in Configurations)
            {
                config.IsSelected = true;
            }
        }
        private string GetNetworkConfigRequestString(NetworkConfiguration config)
        {
            string request = string.Empty;
            if (config.Model == "Dahua")
            {
                request = $"http://{config.CurrentIP}/cgi-bin/configManager.cgi?action=setConfig&Network.eth0.IPAddress={config.NewIP}&Network.eth0.SubnetMask={config.NewMask}&Network.eth0.DefaultGateway={config.NewGateway}";
            }
            else if (config.Model == "Hikvision")
            {
                request = $"http://{config.CurrentIP}/ISAPI/System/Network/interfaces/1/ipAddress";
            }
            return request;
        }

        private async Task SendNetworkConfigRequest(NetworkConfiguration config)
        {
            try
            {

                // Dahua
                if (config.Model == "Dahua")
                {
                    string url = $"http://{config.CurrentIP}/cgi-bin/configManager.cgi?action=setConfig&Network.eth0.IPAddress={config.NewIP}&Network.eth0.SubnetMask={config.NewMask}&Network.eth0.DefaultGateway={config.NewGateway}";
                    var credCache = new CredentialCache();
                    credCache.Add(new Uri($"http://{config.CurrentIP}/"), "Digest", new NetworkCredential(config.User, config.Password));
                    using (HttpClient httpClient = new HttpClient(new HttpClientHandler { Credentials = credCache }))
                    {
                        var answer = await httpClient.GetAsync(new Uri($"http://{config.CurrentIP}/cgi-bin/configManager.cgi?action=getConfig&name=Network.eth0.IPAddress"));
                        if (answer.StatusCode == HttpStatusCode.OK)
                        {
                            config.Status = "Connection OK";
                            var response = await httpClient.GetAsync(new Uri(url));
                            if (response.StatusCode == HttpStatusCode.OK)
                            {
                                config.Status = "Network settings sent successfully";
                                config.CurrentIP = config.NewIP;
                            }
                            else
                            {
                                config.Status = "Error sending network settings";
                            }
                        }
                        else if (answer.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            config.Status = "Login failed";
                        }
                        else
                        {
                            config.Status = "Unknown connection error";
                        }
                    }
                }
                // Hikvision
                if (config.Model == "Hikvision")
                {
                    string networkSettingsUrl = $"http://{config.CurrentIP}/ISAPI/System/Network/interfaces/1/ipAddress";
                    string xmlNetworkSettingsContent = $@"<?xml version='1.0' encoding='UTF-8'?>
                                                        <IPAddress version='2.0' xmlns='http://www.hikvision.com/ver20/XMLSchema'>
                                                         <ipVersion>dual</ipVersion>
                                                         <addressingType>static</addressingType>
                                                         <ipAddress>{config.NewIP}</ipAddress>
                                                         <subnetMask>{config.NewMask}</subnetMask>
                                                         <ipv6Address>::</ipv6Address>
                                                         <bitMask>0</bitMask>
                                                         <DefaultGateway>
                                                             <ipAddress>{config.NewGateway}</ipAddress>
                                                             <ipv6Address>::</ipv6Address>
                                                         </DefaultGateway>
                                                         <PrimaryDNS>
                                                             <ipAddress>8.8.8.8</ipAddress>
                                                         </PrimaryDNS>
                                                         <SecondaryDNS>
                                                             <ipAddress>8.8.4.4</ipAddress>
                                                         </SecondaryDNS>
                                                         <Ipv6Mode>
                                                             <ipV6AddressingType>ra</ipV6AddressingType>
                                                             <ipv6AddressList>
                                                                 <v6Address>
                                                                     <id>1</id>
                                                                     <type>manual</type>
                                                                     <address>::</address>
                                                                     <bitMask>0</bitMask>
                                                                 </v6Address>
                                                             </ipv6AddressList>
                                                         </Ipv6Mode>
                                                        </IPAddress>";
                    string urlCameraReboot = $"http://{config.CurrentIP}/ISAPI/System/reboot";

                    var content = new StringContent(xmlNetworkSettingsContent, Encoding.UTF8, "application/xml");
                    var request = new HttpRequestMessage(HttpMethod.Put, networkSettingsUrl)
                    {
                        Content = content
                    };

                    var credCache = new CredentialCache();
                    credCache.Add(new Uri($"http://{config.CurrentIP}/"), "Digest", new NetworkCredential(config.User, config.Password));

                    using (HttpClient httpClient = new HttpClient(new HttpClientHandler { Credentials = credCache }))
                    {
                        var answer = await httpClient.GetAsync(new Uri($"http://{config.CurrentIP}/ISAPI/System/Network/interfaces/1/ipAddress"));
                        if (answer.StatusCode == HttpStatusCode.OK)
                        {
                            config.Status = "Connection OK";
                            var response = await httpClient.SendAsync(request);
                            if (response.StatusCode == HttpStatusCode.OK)
                            {
                                config.Status = "Network settings sent successfully, rebooting...";
                                var responseReboot = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Put, urlCameraReboot));
                                if (responseReboot.StatusCode == HttpStatusCode.OK)
                                {
                                    config.Status = "Rebooting...";
                                    config.CurrentIP = config.NewIP;
                                }
                                else
                                {
                                    config.Status = "Error rebooting";
                                }
                            }
                            else
                            {
                                config.Status = "Error sending network settings";
                            }
                        }
                        else if (answer.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            config.Status = "Login failed";
                        }
                        else
                        {
                            config.Status = "Unknown connection error";
                        }
                    }
                }
                // Axis
                if (config.Model == "Axis")
                {
                    string url = $"http://{config.CurrentIP}/axis-cgi/network_settings.cgi";
                    var prefixLength = MaskToCIDR(config.NewMask);
                    //string AxisGetNetworkInfoJSON = "{{\"apiVersion\": \"1.0\",\"context\": \"abc\",\"method\": \"getNetworkInfo\"}}";
                    string AxisSetNetworkInfoJSON = $"{{\"apiVersion\": \"1.0\",\"context\": \"abc\",\"method\": \"setIPv4AddressConfiguration\",\"params\":{{ \"deviceName\": \"eth0\",\"configurationMode\": \"static\",\"staticDefaultRouter\": \"{config.NewGateway}\",\"staticAddressConfigurations\":[{{\"address\": \"{config.NewIP}\",\"prefixLength\": {prefixLength}}}]}}}}";
                    var credCache = new NetworkCredential(config.User, config.Password);
                    using (HttpClient httpClient = new HttpClient(new HttpClientHandler { Credentials = credCache }))
                    {
                        var getNetworkInfoContent = new StringContent(AxisSetNetworkInfoJSON, Encoding.UTF8, "application/json");
                        var answer = await httpClient.PostAsync(new Uri($"http://{config.CurrentIP}/axis-cgi/network_settings.cgi"), getNetworkInfoContent);
                        if (answer.StatusCode == HttpStatusCode.OK)
                        {
                        //    config.Status = "Connection OK";
                        //    var response = await httpClient.GetAsync(new Uri(url));
                        //    if (response.StatusCode == HttpStatusCode.OK)
                        //    {
                                config.Status = "Network settings sent successfully";
                        //    }
                        //    else
                        //    {
                        //        config.Status = "Error sending network settings";
                        //    }
                        }
                        else if (answer.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            config.Status = "Login failed";
                        }
                        else
                        {
                            config.Status = "Unknown connection error";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                config.Status = "Error sending request";

            }
        }

        private void SendNTPServerButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedConfigs = Configurations.Where(c => c.IsSelected).ToList();
            if (selectedConfigs.Count == 0)
                return;

            //StringBuilder requestBuilder = new StringBuilder();
            //foreach (var config in selectedConfigs)
            //{
            //    string request = GetNTPServerRequestString(config);
            //    requestBuilder.AppendLine(request);
            //}

            //bool confirmation = MessageBox.Show("Are you sure you want to send these NTP server requests?\n\n" + requestBuilder.ToString(), "Confirmation", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
            //if (!confirmation)
            //    return;

            foreach (var config in selectedConfigs)
            {
                config.Status = "Sending NTP server...";
                Task.Run(async () => await SendSetNTPRequest(config));


            }
        }

        private string GetEnableNTPRequestString(NetworkConfiguration config)
        {
            string request = string.Empty;
            if (config.Model == "Dahua")
            {
                //request = $"http://{config.CurrentIP}/cgi-bin/configManager.cgi?action=setConfig&NTPServer.ip={config.NtpServer}";
            }
            else if (config.Model == "Hikvision")
            {
                request = $"http://{config.CurrentIP}/ISAPI/System/time";
            }
            else if (config.Model == "Axis")
            {
                //request = $"http://{config.CurrentIP}/axis-cgi/param.cgi?action=update&root.Network.NTP.ServerAddress={config.NtpServer}";
            }
            return request;
        }

        private string GetSetNTPServerRequestString(NetworkConfiguration config)
        {
            string request = string.Empty;
            if (config.Model == "Dahua")
            {
                //request = $"http://{config.CurrentIP}/cgi-bin/configManager.cgi?action=setConfig&NTPServer.ip={config.NtpServer}";
            }
            else if (config.Model == "Hikvision")
            {
                request = $"http://{config.CurrentIP}/ISAPI/System/time/ntpServers";
            }
            else if (config.Model == "Axis")
            {
                //request = $"http://{config.CurrentIP}/axis-cgi/param.cgi?action=update&root.Network.NTP.ServerAddress={config.NtpServer}";
            }
            return request;
        }

        private async Task SendSetNTPRequest(NetworkConfiguration config)
        {
            try
            {
                if (config.Model == "Dahua")
                {
                    string url = $"http://{config.CurrentIP}/cgi-bin/configManager.cgi?action=setConfig&NTP.Enable=true&NTP.TimeZone=1&NTP.Address={config.NewNTPServer}";
                    var credCache = new CredentialCache();
                    credCache.Add(new Uri($"http://{config.CurrentIP}/"), "Digest", new NetworkCredential(config.User, config.Password));
                    using (HttpClient httpClient = new HttpClient(new HttpClientHandler { Credentials = credCache }))
                    {
                        var answer = await httpClient.GetAsync(new Uri($"http://{config.CurrentIP}/cgi-bin/configManager.cgi?action=getConfig&name=NTP"));
                        if (answer.StatusCode == HttpStatusCode.OK)
                        {
                            config.Status = "Connection OK";
                            var response = await httpClient.GetAsync(new Uri(url));
                            if (response.StatusCode == HttpStatusCode.OK)
                            {
                                config.Status = "NTP server sent successfully";
                                url = $"http://{config.CurrentIP}/cgi-bin/configManager.cgi?action=setConfig&Locales.DSTEnable=true&Locales.DSTStart.Month=3&Locales.DSTStart.Week=-1&Locales.DSTStart.Day=0&Locales.DSTStart.Hour=2&Locales.DSTStart.Minute=0&Locales.DSTEnd.Month=10&Locales.DSTEnd.Week=-1&Locales.DSTEnd.Day=0&Locales.DSTEnd.Hour=2&Locales.DSTEnd.Minute=0";
                                var responseDST = await httpClient.GetAsync(new Uri(url));
                                if (responseDST.StatusCode == HttpStatusCode.OK)
                                {
                                    config.Status = "DST settings sent successfully";
                                }
                                else
                                {
                                    config.Status = "Error sending DST settings";
                                }
                            }
                            else
                            {
                                config.Status = "Error sending NTP server";
                            }
                        }
                        else if (answer.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            config.Status = "Login failed";
                        }
                        else
                        {
                            config.Status = "Unknown connection error";
                        }
                    }
                }
                if (config.Model == "Hikvision")
                {
                    string url = $"http://{config.CurrentIP}/ISAPI/System/time";
                    string xmlEnableNTPContent = $@"<?xml version='1.0' encoding='UTF-8'?>
                                            <Time xmlns='http://www.hikvision.com/ver20/XMLSchema' version='2.0'>
                                                <timeMode>NTP</timeMode>
                                                <timeZone>CST-1:00:00DST01:00:00,M3.5.0/02:00:00,M10.5.0/02:00:00</timeZone>
                                            </Time>";
                    string xmlSetNTPServerContent = $@"<?xml version='1.0' encoding='UTF-8'?>
<NTPServerList xmlns = 'http://www.hikvision.com/ver20/XMLSchema' version = '2.0'>
                                                        <NTPServer xmlns = 'http://www.hikvision.com/ver20/XMLSchema' version = '2.0'>
                                                        <id>1</id>
                                                        <addressingFormatType>ipaddress</addressingFormatType>
                                                        <ipAddress>{config.NewNTPServer}</ipAddress>
                                                        </NTPServer>
                                                    </NTPServerList>";

                    var content = new StringContent(xmlEnableNTPContent, Encoding.UTF8, "application/xml");
                    var request = new HttpRequestMessage(HttpMethod.Put, url)
                    {
                        Content = content
                    };
                    using (HttpClient httpClient = new HttpClient(new HttpClientHandler { Credentials = new NetworkCredential(config.User, config.Password) }))
                    {
                        var answer = await httpClient.GetAsync(new Uri($"http://{config.CurrentIP}/ISAPI/System/time/"));
                        if (answer.StatusCode == HttpStatusCode.OK)
                        {
                            config.Status = "Connection OK";
                            var response = await httpClient.SendAsync(request);
                            if (response.StatusCode == HttpStatusCode.OK)
                            {
                                config.Status = "NTP enable sent successfully";
                                url = $"http://{config.CurrentIP}/ISAPI/System/time/ntpServers";
                                content = new StringContent(xmlSetNTPServerContent, Encoding.UTF8, "application/xml");
                                request = new HttpRequestMessage(HttpMethod.Put, url)
                                {
                                    Content = content
                                };
                                var responseNTP = await httpClient.SendAsync(request);
                                if (responseNTP.StatusCode == HttpStatusCode.OK)
                                {
                                    config.Status = "NTP server sent successfully";
                                }
                                else
                                {
                                    config.Status = "Error sending NTP server";
                                }
                            }
                            else
                            {
                                config.Status = "Error sending NTP server";
                            }
                        }
                        else if (answer.StatusCode == HttpStatusCode.Unauthorized)
                        {
                            config.Status = "Login failed";
                        }
                        else
                        {
                            config.Status = "Unknown connection error";
                        }
                    }
                }
                //if (config.Model == "Axis")
                //    {
                //    string url = $"http://{config.CurrentIP}/axis-cgi/param.cgi?action=update&root.Network.NTP.ServerAddress={config.NtpServer}";
                //    using (HttpClient httpClient = new HttpClient(new HttpClientHandler { Credentials = new NetworkCredential(config.User, config.Password) }))
                //    {
                //        var answer = await httpClient.GetAsync(new Uri($"http://{config.CurrentIP}/axis-cgi/param.cgi?action=list&group=Network.NTP"));
                //        if (answer.StatusCode == HttpStatusCode.OK)
                //        {
                //            config.Status = "Connection OK";
                //            var response = await httpClient.GetAsync(new Uri(url));
                //            if (response.StatusCode == HttpStatusCode.OK)
                //            {
                //                config.Status = "NTP server sent successfully";
                //            }
                //            else
                //            {
                //                config.Status = "Error sending NTP server";
                //            }
                //        }
                //        else if (answer.StatusCode == HttpStatusCode.Unauthorized)
                //        {
                //            config.Status = "Login failed";
                //        }
                //        else
                //        {
                //            config.Status = "Unknown connection error";
                //        }
                //    }
                //}
            }
            catch (Exception ex)
            {
                config.Status = "Error sending NTP server";
            }
        }
        

        private int MaskToCIDR(string mask)
        {
            // Divide la máscara de subred en sus octetos.
            var octets = mask.Split('.').Select(int.Parse).ToArray();
            // Convierte los octetos a binario y concatena.
            var binaryStr = string.Join("", octets.Select(o => Convert.ToString(o, 2).PadLeft(8, '0')));
            // Cuenta los bits '1'.
            var prefixLength = binaryStr.Count(c => c == '1');
            return prefixLength;
        }
        // COPY & PASTE
        private void DataGrid_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // COPY
            if (e.Key == Key.C && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                var cellInfo = dataGrid.CurrentCell;
                if (cellInfo.Column != null)
                {
                    var content = cellInfo.Column.GetCellContent(cellInfo.Item);
                    if (content is TextBlock textBlock)
                    {
                        Clipboard.SetText(textBlock.Text);
                        e.Handled = true;
                    }
                }
            }
            // PASTE
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                PasteToSelectedCell();
                e.Handled = true;
            }
        }
        private void PasteToSelectedCell()
        {
            if (dataGrid.CurrentCell.Item != null && Clipboard.ContainsText())
            {
                var clipboardText = Clipboard.GetText();
                var column = dataGrid.CurrentCell.Column;
                var row = (NetworkConfiguration)dataGrid.CurrentItem;
                var property = typeof(NetworkConfiguration).GetProperty(column.SortMemberPath);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(row, clipboardText);
                }
            }
        }

        // ERROR HANDLING
        private void ShowErrorMessage(string message, Exception ex)
        {
            MessageBox.Show($"{message}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

    }


}
