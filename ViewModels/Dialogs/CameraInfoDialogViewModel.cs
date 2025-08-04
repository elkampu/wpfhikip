using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

using wpfhikip.Models;
using wpfhikip.Protocols.Common;
using wpfhikip.ViewModels.Commands;
using wpfhikip.Views.Dialogs;

namespace wpfhikip.ViewModels.Dialogs
{
    public class CameraInfoDialogViewModel : ViewModelBase, IDisposable
    {
        private readonly Camera _camera;
        private CancellationTokenSource? _loadingCancellation;
        private bool _disposed;

        private bool _isLoading;
        private bool _hasError;
        private string _errorMessage = string.Empty;
        private string _loadingStatus = "Initializing...";

        // Connection Information
        private string _ipAddress = string.Empty;
        private string _port = string.Empty;
        private string _protocol = string.Empty;
        private string _username = string.Empty;
        private string _status = string.Empty;

        // Device Information
        private string _manufacturer = string.Empty;
        private string _model = string.Empty;
        private string _firmware = string.Empty;
        private string _serialNumber = string.Empty;
        private string _macAddress = string.Empty;

        // Network Settings
        private string _currentIp = string.Empty;
        private string _subnetMask = string.Empty;
        private string _gateway = string.Empty;
        private string _dns1 = string.Empty;
        private string _dns2 = string.Empty;

        // Video Stream Information Properties
        private string _mainStream = string.Empty;
        private string _subStream = string.Empty;
        private string _resolution = string.Empty;
        private string _frameRate = string.Empty;
        private string _bitRate = string.Empty;
        private string _codecType = string.Empty;
        private string _qualityControlType = string.Empty;

        public CameraInfoDialogViewModel(Camera camera)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));

            InitializeBasicInfo();

            // Commands
            RefreshCommand = new RelayCommand(async _ => await RefreshCameraInfoAsync(), _ => !IsLoading);
            CancelCommand = new RelayCommand(_ => CancelLoading(), _ => IsLoading);
            CopyAllCommand = new RelayCommand(_ => CopyAllInformation(), _ => true);
            OpenLiveStreamCommand = new RelayCommand(_ => OpenLiveStreamWindow(), _ => CanShowLiveStream);
        }

        // Properties
        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool HasError
        {
            get => _hasError;
            private set => SetProperty(ref _hasError, value);
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set => SetProperty(ref _errorMessage, value);
        }

        public string LoadingStatus
        {
            get => _loadingStatus;
            private set => SetProperty(ref _loadingStatus, value);
        }

        /// <summary>
        /// Determines if the live stream button should be shown
        /// </summary>
        public bool CanShowLiveStream
        {
            get
            {
                // Show live stream button if camera is compatible and has IP/credentials
                return _camera.CanShowCameraInfo &&
                       !string.IsNullOrEmpty(_camera.CurrentIP) &&
                       !string.IsNullOrEmpty(_camera.Username);
            }
        }

        // Connection Information Properties
        public string IpAddress
        {
            get => _ipAddress;
            private set => SetProperty(ref _ipAddress, value);
        }

        public string Port
        {
            get => _port;
            private set => SetProperty(ref _port, value);
        }

        public string Protocol
        {
            get => _protocol;
            private set => SetProperty(ref _protocol, value);
        }

        public string Username
        {
            get => _username;
            private set => SetProperty(ref _username, value);
        }

        public string Status
        {
            get => _status;
            private set => SetProperty(ref _status, value);
        }

        // Device Information Properties
        public string Manufacturer
        {
            get => _manufacturer;
            private set => SetProperty(ref _manufacturer, value);
        }

        public string Model
        {
            get => _model;
            private set => SetProperty(ref _model, value);
        }

        public string Firmware
        {
            get => _firmware;
            private set => SetProperty(ref _firmware, value);
        }

        public string SerialNumber
        {
            get => _serialNumber;
            private set => SetProperty(ref _serialNumber, value);
        }

        public string MacAddress
        {
            get => _macAddress;
            private set => SetProperty(ref _macAddress, value);
        }

        // Network Settings Properties
        public string CurrentIp
        {
            get => _currentIp;
            private set => SetProperty(ref _currentIp, value);
        }

        public string SubnetMask
        {
            get => _subnetMask;
            private set => SetProperty(ref _subnetMask, value);
        }

        public string Gateway
        {
            get => _gateway;
            private set => SetProperty(ref _gateway, value);
        }

        public string Dns1
        {
            get => _dns1;
            private set => SetProperty(ref _dns1, value);
        }

        public string Dns2
        {
            get => _dns2;
            private set => SetProperty(ref _dns2, value);
        }

        // Video Stream Information Properties
        public string MainStream
        {
            get => _mainStream;
            private set => SetProperty(ref _mainStream, value);
        }

        public string SubStream
        {
            get => _subStream;
            private set => SetProperty(ref _subStream, value);
        }

        public string Resolution
        {
            get => _resolution;
            private set => SetProperty(ref _resolution, value);
        }

        public string FrameRate
        {
            get => _frameRate;
            private set => SetProperty(ref _frameRate, value);
        }

        public string BitRate
        {
            get => _bitRate;
            private set => SetProperty(ref _bitRate, value);
        }

        public string CodecType
        {
            get => _codecType;
            private set => SetProperty(ref _codecType, value);
        }

        public string QualityControlType
        {
            get => _qualityControlType;
            private set => SetProperty(ref _qualityControlType, value);
        }

        // Commands
        public ICommand RefreshCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand CopyAllCommand { get; }
        public ICommand OpenLiveStreamCommand { get; }

        /// <summary>
        /// Opens the live video stream window
        /// </summary>
        private void OpenLiveStreamWindow()
        {
            try
            {
                var liveStreamWindow = new LiveVideoStreamWindow(_camera);

                // Try to find the owner window (CameraInfoDialog)
                var ownerWindow = Application.Current.Windows
                    .OfType<Window>()
                    .FirstOrDefault(w => w.IsActive && w is CameraInfoDialog);

                if (ownerWindow != null)
                {
                    liveStreamWindow.Owner = ownerWindow;
                }

                liveStreamWindow.Show();

                // Log the action
                _camera.AddProtocolLog("Live Stream", "Open", "Live stream window opened", ProtocolLogLevel.Info);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open live stream window: {ex.Message}",
                               "Error",
                               MessageBoxButton.OK,
                               MessageBoxImage.Error);

                _camera.AddProtocolLog("Live Stream", "Open", $"Failed to open live stream: {ex.Message}", ProtocolLogLevel.Error);
            }
        }

        /// <summary>
        /// Copies all camera information to the clipboard in a formatted text
        /// </summary>
        private void CopyAllInformation()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("CAMERA INFORMATION");
                sb.AppendLine("==================");
                sb.AppendLine();

                // Connection Information
                sb.AppendLine("CONNECTION INFORMATION");
                sb.AppendLine("---------------------");
                sb.AppendLine($"IP Address: {IpAddress}");
                sb.AppendLine($"Port: {Port}");
                sb.AppendLine($"Protocol: {Protocol}");
                sb.AppendLine($"Username: {Username}");
                sb.AppendLine($"Status: {Status}");
                sb.AppendLine();

                // Device Information
                sb.AppendLine("DEVICE INFORMATION");
                sb.AppendLine("------------------");
                sb.AppendLine($"Manufacturer: {Manufacturer}");
                sb.AppendLine($"Model: {Model}");
                sb.AppendLine($"Firmware: {Firmware}");
                sb.AppendLine($"Serial Number: {SerialNumber}");
                sb.AppendLine($"MAC Address: {MacAddress}");
                sb.AppendLine();

                // Network Settings
                sb.AppendLine("NETWORK SETTINGS");
                sb.AppendLine("----------------");
                sb.AppendLine($"Current IP: {CurrentIp}");
                sb.AppendLine($"Subnet Mask: {SubnetMask}");
                sb.AppendLine($"Gateway: {Gateway}");
                sb.AppendLine($"DNS1: {Dns1}");
                sb.AppendLine($"DNS2: {Dns2}");
                sb.AppendLine();

                // Video Stream Information
                sb.AppendLine("VIDEO STREAM INFORMATION");
                sb.AppendLine("------------------------");
                sb.AppendLine($"Main Stream: {MainStream}");
                sb.AppendLine($"Sub Stream: {SubStream}");
                sb.AppendLine($"Codec Type: {CodecType}");
                sb.AppendLine($"Resolution: {Resolution}");
                sb.AppendLine($"Quality Control: {QualityControlType}");
                sb.AppendLine($"Frame Rate: {FrameRate}");
                sb.AppendLine($"Bit Rate: {BitRate}");
                sb.AppendLine();

                // Footer
                sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                // Copy to clipboard
                Clipboard.SetText(sb.ToString());

                // Log the action
                _camera.AddProtocolLog("Info Dialog", "Copy", "All camera information copied to clipboard", ProtocolLogLevel.Info);
            }
            catch (Exception ex)
            {
                _camera.AddProtocolLog("Info Dialog", "Copy", $"Failed to copy information: {ex.Message}", ProtocolLogLevel.Error);
            }
        }

        /// <summary>
        /// Loads camera information from the device using generic ProtocolManager methods
        /// </summary>
        public async Task LoadCameraInfoAsync()
        {
            if (!ProtocolConnectionFactory.IsProtocolSupported(_camera.Protocol))
            {
                SetError($"Camera information loading is currently not supported for {_camera.Protocol} cameras.");
                return;
            }

            if (!_camera.CanShowCameraInfo)
            {
                SetError("Camera must be compatible and authenticated to load information.");
                return;
            }

            await RefreshCameraInfoAsync();
        }

        private async Task RefreshCameraInfoAsync()
        {
            _loadingCancellation?.Cancel();
            _loadingCancellation = new CancellationTokenSource();

            IsLoading = true;
            HasError = false;
            ErrorMessage = string.Empty;

            try
            {
                LoadingStatus = "Connecting to camera...";

                // Use generic ProtocolManager method instead of protocol-specific methods
                bool success = await ProtocolManager.LoadCameraInfoAsync(_camera, _loadingCancellation.Token);

                // Always update UI properties from the camera model, regardless of success
                UpdateUIFromCameraModel();

                if (success)
                {
                    LoadingStatus = "Information loaded successfully";
                    Status = $"Connected - Last updated: {DateTime.Now:HH:mm:ss}";
                }
                else
                {
                    LoadingStatus = "Partial information loaded";
                    Status = $"Some errors - Last updated: {DateTime.Now:HH:mm:ss}";
                }
            }
            catch (OperationCanceledException)
            {
                LoadingStatus = "Loading cancelled";
                _camera.AddProtocolLog(_camera.Protocol.ToString(), "Info Dialog", "Information loading was cancelled", ProtocolLogLevel.Info);
            }
            catch (Exception ex)
            {
                SetError($"Failed to load camera information: {ex.Message}");
                _camera.AddProtocolLog(_camera.Protocol.ToString(), "Info Dialog", $"Failed to load information: {ex.Message}", ProtocolLogLevel.Error);
                Status = $"Error - {DateTime.Now:HH:mm:ss}";

                // Still update what we can from the camera model
                UpdateUIFromCameraModel();
            }
            finally
            {
                IsLoading = false;
                _loadingCancellation?.Dispose();
                _loadingCancellation = null;

                // Update live stream availability after loading
                OnPropertyChanged(nameof(CanShowLiveStream));
            }
        }

        /// <summary>
        /// Updates UI properties from the camera model after loading information
        /// </summary>
        private void UpdateUIFromCameraModel()
        {
            // Update device information - show "Not detected" for null/empty values
            Manufacturer = _camera.Manufacturer ?? "Not detected";
            Model = _camera.Model ?? "Not detected";
            Firmware = _camera.Firmware ?? "Not detected";
            SerialNumber = _camera.SerialNumber ?? "Not detected";
            MacAddress = _camera.MacAddress ?? "Not detected";

            // Update network information - use Current properties for actual camera values
            CurrentIp = _camera.CurrentIP ?? "N/A";
            SubnetMask = _camera.CurrentSubnetMask ?? "Not configured";
            Gateway = _camera.CurrentGateway ?? "Not configured";
            Dns1 = _camera.CurrentDNS1 ?? "Not configured";
            Dns2 = _camera.CurrentDNS2 ?? "Not configured";

            // Debug logging to help identify the values being used
            _camera.AddProtocolLog("UI Update", "Network Values",
                $"Current: SubnetMask='{_camera.CurrentSubnetMask}', Gateway='{_camera.CurrentGateway}', DNS1='{_camera.CurrentDNS1}', DNS2='{_camera.CurrentDNS2}'",
                ProtocolLogLevel.Info);

            // Update video stream information - show "Not available" for null/empty values
            MainStream = _camera.VideoStream?.MainStreamUrl ?? "Not available";
            SubStream = _camera.VideoStream?.SubStreamUrl ?? "Not available";
            Resolution = _camera.VideoStream?.Resolution ?? "Not detected";
            FrameRate = _camera.VideoStream?.FrameRate ?? "Not detected";
            BitRate = _camera.VideoStream?.BitRate ?? "Not detected";
            CodecType = _camera.VideoStream?.CodecType ?? "Not detected";
            QualityControlType = _camera.VideoStream?.QualityControlType ?? "Not detected";
        }

        private void InitializeBasicInfo()
        {
            // Initialize with basic camera information immediately
            IpAddress = _camera.CurrentIP ?? "N/A";
            Port = _camera.EffectivePort.ToString();
            Protocol = _camera.Protocol.ToString();
            Username = string.IsNullOrEmpty(_camera.Username) ? "N/A" : _camera.Username;
            Status = _camera.Status ?? "N/A";

            // Initialize with existing camera information if available
            UpdateUIFromCameraModel();
        }

        private void CancelLoading()
        {
            _loadingCancellation?.Cancel();
        }

        private void SetError(string message)
        {
            HasError = true;
            ErrorMessage = message;
            LoadingStatus = "Error occurred";
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _loadingCancellation?.Cancel();
                    _loadingCancellation?.Dispose();
                }
                _disposed = true;
            }
        }
    }
}