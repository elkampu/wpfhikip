using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

using LibVLCSharp.Shared;

using wpfhikip.Models;
using wpfhikip.Protocols.Common;
using wpfhikip.Protocols.Onvif;
using wpfhikip.ViewModels.Commands;
using wpfhikip.Views.Dialogs;

namespace wpfhikip.ViewModels.Dialogs
{
    public class LiveVideoStreamViewModel : ViewModelBase, IDisposable
    {
        private readonly Camera _camera;
        private bool _disposed;
        private LibVLC? _libVLC;
        private MediaPlayer? _mediaPlayer;
        private Media? _currentMedia;

        // State properties
        private bool _isLoading;
        private bool _isConnected;
        private bool _isPlaying;
        private bool _isPaused;
        private bool _hasError;
        private string _errorMessage = string.Empty;
        private string _statusMessage = "Ready";
        private StreamOption? _selectedStreamOption;
        private string? _currentStreamUrl;

        // Events
        public event Action? RequestClose;
        public event Action<VideoPlayerAction>? VideoPlayerAction;

        public LiveVideoStreamViewModel(Camera camera)
        {
            _camera = camera ?? throw new ArgumentNullException(nameof(camera));

            InitializeVLC();
            InitializeCommands();
            InitializeStreamOptions();

            StatusMessage = "Select a stream and click play to begin";
        }

        #region Properties

        public string WindowTitle => $"Live Video Stream - {_camera.CurrentIP}";
        public string Title => "Live RTSP Video Stream";
        public string Subtitle => $"{_camera.CurrentIP} • {_camera.Protocol}";

        public MediaPlayer? MediaPlayer => _mediaPlayer;

        public ObservableCollection<StreamOption> StreamOptions { get; } = new();

        public StreamOption? SelectedStreamOption
        {
            get => _selectedStreamOption;
            set
            {
                if (SetProperty(ref _selectedStreamOption, value))
                {
                    CurrentStreamUrl = value?.StreamUrl;
                    OnPropertyChanged(nameof(CanPlay));
                }
            }
        }

        public string? CurrentStreamUrl
        {
            get => _currentStreamUrl;
            private set => SetProperty(ref _currentStreamUrl, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (SetProperty(ref _isLoading, value))
                    UpdateVisibilityStates();
            }
        }

        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (SetProperty(ref _isConnected, value))
                {
                    OnPropertyChanged(nameof(CanPlay));
                    UpdateVisibilityStates();
                }
            }
        }

        public bool IsPlaying
        {
            get => _isPlaying;
            private set
            {
                if (SetProperty(ref _isPlaying, value))
                {
                    IsPaused = false;
                    OnPropertyChanged(nameof(CanPlay));
                    OnPropertyChanged(nameof(CanPause));
                    UpdateVisibilityStates();
                }
            }
        }

        public bool IsPaused
        {
            get => _isPaused;
            private set
            {
                if (SetProperty(ref _isPaused, value))
                {
                    OnPropertyChanged(nameof(CanPlay));
                    OnPropertyChanged(nameof(CanPause));
                    UpdateVisibilityStates();
                }
            }
        }

        public bool HasError
        {
            get => _hasError;
            private set
            {
                if (SetProperty(ref _hasError, value))
                    UpdateVisibilityStates();
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set => SetProperty(ref _errorMessage, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        // Visibility states
        public bool ShowVideoPlayer => IsConnected && !HasError && !IsLoading;
        public bool ShowDefaultState => !IsLoading && !HasError && !IsConnected;

        // Command availability
        public bool CanPlay => !IsLoading && SelectedStreamOption != null && (!IsConnected || IsPaused);
        public bool CanPause => IsConnected && IsPlaying;

        #endregion

        #region VLC Initialization

        private void InitializeVLC()
        {
            try
            {
                // Initialize LibVLC
                Core.Initialize();
                _libVLC = new LibVLC("--intf", "dummy", "--no-osd", "--no-stats", "--no-video-title-show");
                _mediaPlayer = new MediaPlayer(_libVLC);

                // Subscribe to MediaPlayer events
                _mediaPlayer.Playing += MediaPlayer_Playing;
                _mediaPlayer.Paused += MediaPlayer_Paused;
                _mediaPlayer.Stopped += MediaPlayer_Stopped;
                _mediaPlayer.EndReached += MediaPlayer_EndReached;
                _mediaPlayer.EncounteredError += MediaPlayer_EncounteredError;

                _camera.AddProtocolLog("Live Stream", "VLC Init", "VLC MediaPlayer initialized successfully", ProtocolLogLevel.Info);
            }
            catch (Exception ex)
            {
                var errorMsg = $"Failed to initialize VLC: {ex.Message}";
                HandleError(errorMsg);
                _camera.AddProtocolLog("Live Stream", "VLC Init", errorMsg, ProtocolLogLevel.Error);
            }
        }

        #endregion

        #region Commands

        public ICommand PlayCommand { get; private set; } = null!;
        public ICommand PauseCommand { get; private set; } = null!;
        public ICommand StopCommand { get; private set; } = null!;
        public ICommand RefreshCommand { get; private set; } = null!;
        public ICommand CopyStreamUrlCommand { get; private set; } = null!;
        public ICommand CloseCommand { get; private set; } = null!;

        private void InitializeCommands()
        {
            PlayCommand = new RelayCommand(async _ => await PlayStreamAsync(), _ => CanPlay);
            PauseCommand = new RelayCommand(_ => PauseStream(), _ => CanPause);
            StopCommand = new RelayCommand(_ => StopStream(), _ => IsConnected);
            RefreshCommand = new RelayCommand(async _ => await RefreshStreamAsync(), _ => !IsLoading);
            CopyStreamUrlCommand = new RelayCommand(_ => CopyStreamUrl(), _ => !string.IsNullOrEmpty(CurrentStreamUrl));
            CloseCommand = new RelayCommand(_ => RequestClose?.Invoke(), _ => true);
        }

        #endregion

        #region Stream Management

        private async void InitializeStreamOptions()
        {
            StreamOptions.Clear();

            // For ONVIF cameras, try to get actual stream URLs first
            if (_camera.Protocol == CameraProtocol.Onvif)
            {
                await LoadOnvifStreamUrlsAsync();
            }

            // Add main stream if available
            if (!string.IsNullOrEmpty(_camera.VideoStream.MainStreamUrl))
            {
                StreamOptions.Add(new StreamOption
                {
                    DisplayName = "Main Stream (High Quality)",
                    StreamUrl = _camera.VideoStream.MainStreamUrl,
                    StreamType = StreamType.Main
                });
                _camera.AddProtocolLog("Live Stream", "Stream Option", $"Added main stream: {_camera.VideoStream.MainStreamUrl}", ProtocolLogLevel.Info);
            }

            // Add sub stream if available
            if (!string.IsNullOrEmpty(_camera.VideoStream.SubStreamUrl))
            {
                StreamOptions.Add(new StreamOption
                {
                    DisplayName = "Sub Stream (Low Quality)",
                    StreamUrl = _camera.VideoStream.SubStreamUrl,
                    StreamType = StreamType.Sub
                });
                _camera.AddProtocolLog("Live Stream", "Stream Option", $"Added sub stream: {_camera.VideoStream.SubStreamUrl}", ProtocolLogLevel.Info);
            }

            // Generate RTSP URLs if not available in VideoStream
            if (StreamOptions.Count == 0)
            {
                _camera.AddProtocolLog("Live Stream", "Stream Generation", "No stream URLs from ONVIF, generating fallback URLs", ProtocolLogLevel.Warning);
                GenerateRtspUrls();
            }

            // Select first option by default
            if (StreamOptions.Any())
            {
                SelectedStreamOption = StreamOptions.First();
                _camera.AddProtocolLog("Live Stream", "Stream Selection", $"Selected default stream: {SelectedStreamOption.DisplayName}", ProtocolLogLevel.Info);
            }
            else
            {
                _camera.AddProtocolLog("Live Stream", "Stream Error", "No stream options available", ProtocolLogLevel.Error);
                HandleError("No stream URLs available. Please check camera credentials and ONVIF compatibility.");
            }
        }

        private void GenerateRtspUrls()
        {
            if (string.IsNullOrEmpty(_camera.CurrentIP) || string.IsNullOrEmpty(_camera.Username))
            {
                _camera.AddProtocolLog("Live Stream", "URL Generation", "Missing IP or credentials for RTSP URL generation", ProtocolLogLevel.Error);
                return;
            }

            var ip = _camera.CurrentIP;
            var port = _camera.EffectivePort == 80 ? 554 : _camera.EffectivePort; // Default RTSP port
            var username = _camera.Username;
            var password = _camera.Password ?? string.Empty;

            // Generate common RTSP URL patterns based on protocol
            switch (_camera.Protocol)
            {
                case CameraProtocol.Onvif:
                    // Try multiple common ONVIF stream URLs
                    var onvifUrls = new[]
                    {
                        $"rtsp://{username}:{password}@{ip}:{port}/MediaInput/stream_1",
                        $"rtsp://{username}:{password}@{ip}:{port}/onvif/profile1",
                        $"rtsp://{username}:{password}@{ip}:{port}/profile1",
                        $"rtsp://{username}:{password}@{ip}:{port}/stream1",
                        $"rtsp://{username}:{password}@{ip}:{port}/cam1/h264"
                    };

                    foreach (var (url, index) in onvifUrls.Select((url, i) => (url, i)))
                    {
                        StreamOptions.Add(new StreamOption
                        {
                            DisplayName = $"ONVIF Stream {index + 1}",
                            StreamUrl = url,
                            StreamType = StreamType.Main
                        });
                    }
                    break;

                default:
                    // Generic RTSP URLs
                    StreamOptions.Add(new StreamOption
                    {
                        DisplayName = "Generic Main Stream",
                        StreamUrl = $"rtsp://{username}:{password}@{ip}:{port}/stream1",
                        StreamType = StreamType.Main
                    });
                    StreamOptions.Add(new StreamOption
                    {
                        DisplayName = "Generic Sub Stream",
                        StreamUrl = $"rtsp://{username}:{password}@{ip}:{port}/stream2",
                        StreamType = StreamType.Sub
                    });
                    break;
            }

            _camera.AddProtocolLog("Live Stream", "URL Generation", $"Generated {StreamOptions.Count} fallback RTSP URLs", ProtocolLogLevel.Info);
        }

        private async Task PlayStreamAsync()
        {
            if (SelectedStreamOption == null || string.IsNullOrEmpty(SelectedStreamOption.StreamUrl) || _mediaPlayer == null || _libVLC == null)
                return;

            try
            {
                IsLoading = true;
                HasError = false;
                StatusMessage = "Connecting to stream...";

                _camera.AddProtocolLog("Live Stream", "Play", $"Starting playback of: {SelectedStreamOption.StreamUrl}", ProtocolLogLevel.Info);

                // Stop current media if playing
                if (_mediaPlayer.IsPlaying)
                {
                    _mediaPlayer.Stop();
                }

                // Dispose current media if exists
                _currentMedia?.Dispose();

                // Create new media from URL (don't use 'using' here!)
                _currentMedia = new Media(_libVLC, SelectedStreamOption.StreamUrl, FromType.FromLocation);

                // Add media options for better RTSP handling
                _currentMedia.AddOption(":network-caching=300");
                _currentMedia.AddOption(":rtsp-timeout=60");
                _currentMedia.AddOption(":rtsp-tcp");
                _currentMedia.AddOption(":rtsp-frame-buffer-size=500000");

                // Set media to player and play
                _mediaPlayer.Media = _currentMedia;
                CurrentStreamUrl = SelectedStreamOption.StreamUrl;

                // Start playback
                _mediaPlayer.Play();

                StatusMessage = $"Connecting to {SelectedStreamOption.DisplayName}...";

                // Give VLC some time to start
                await Task.Delay(100);
            }
            catch (Exception ex)
            {
                var errorMsg = $"Failed to start stream: {ex.Message}";
                HandleError(errorMsg);
                _camera.AddProtocolLog("Live Stream", "Play Error", errorMsg, ProtocolLogLevel.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void PauseStream()
        {
            try
            {
                if (_mediaPlayer?.CanPause == true)
                {
                    _mediaPlayer.Pause();
                    StatusMessage = "Stream paused";
                    _camera.AddProtocolLog("Live Stream", "Pause", "Stream paused", ProtocolLogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                var errorMsg = $"Failed to pause stream: {ex.Message}";
                HandleError(errorMsg);
                _camera.AddProtocolLog("Live Stream", "Pause Error", errorMsg, ProtocolLogLevel.Error);
            }
        }

        private void StopStream()
        {
            try
            {
                _mediaPlayer?.Stop();
                _currentMedia?.Dispose();
                _currentMedia = null;
                IsConnected = false;
                IsPlaying = false;
                IsPaused = false;
                StatusMessage = "Stream stopped";
                _camera.AddProtocolLog("Live Stream", "Stop", "Stream stopped", ProtocolLogLevel.Info);
            }
            catch (Exception ex)
            {
                var errorMsg = $"Failed to stop stream: {ex.Message}";
                HandleError(errorMsg);
                _camera.AddProtocolLog("Live Stream", "Stop Error", errorMsg, ProtocolLogLevel.Error);
            }
        }

        private async Task RefreshStreamAsync()
        {
            if (IsPlaying)
            {
                StopStream();
                await Task.Delay(500);
            }
            await PlayStreamAsync();
        }

        private void CopyStreamUrl()
        {
            if (!string.IsNullOrEmpty(CurrentStreamUrl))
            {
                try
                {
                    Clipboard.SetText(CurrentStreamUrl);
                    StatusMessage = "Stream URL copied to clipboard";
                    _camera.AddProtocolLog("Live Stream", "Copy URL", "Stream URL copied to clipboard", ProtocolLogLevel.Info);
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Failed to copy URL: {ex.Message}";
                    _camera.AddProtocolLog("Live Stream", "Copy Error", $"Failed to copy URL: {ex.Message}", ProtocolLogLevel.Error);
                }
            }
        }

        private async Task LoadOnvifStreamUrlsAsync()
        {
            try
            {
                StatusMessage = "Loading ONVIF stream URLs...";
                _camera.AddProtocolLog("Live Stream", "ONVIF Load", "Loading ONVIF stream URLs...", ProtocolLogLevel.Info);

                // Create ONVIF connection
                using var connection = new OnvifConnection(_camera.CurrentIP, _camera.EffectivePort, _camera.Username, _camera.Password ?? string.Empty);
                using var configuration = new OnvifConfiguration(connection);

                // Get video info which includes stream URLs
                var videoResult = await configuration.GetVideoInfoAsync();

                if (videoResult.Success && videoResult.Data != null)
                {
                    // Update camera video stream with discovered URLs
                    CameraDataProcessor.UpdateVideoInfo(_camera, videoResult.Data);

                    StatusMessage = "ONVIF stream URLs loaded successfully";
                    _camera.AddProtocolLog("Live Stream", "ONVIF Success",
                        $"Loaded stream URLs - Main: {_camera.VideoStream?.MainStreamUrl}, Sub: {_camera.VideoStream?.SubStreamUrl}",
                        ProtocolLogLevel.Info);
                }
                else
                {
                    StatusMessage = "Failed to load ONVIF stream URLs, using fallback URLs";
                    _camera.AddProtocolLog("Live Stream", "ONVIF Warning",
                        $"Failed to load ONVIF stream URLs: {videoResult.ErrorMessage}",
                        ProtocolLogLevel.Warning);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading ONVIF streams: {ex.Message}";
                _camera.AddProtocolLog("Live Stream", "ONVIF Error",
                    $"Error loading ONVIF streams: {ex.Message}",
                    ProtocolLogLevel.Error);
            }
        }

        #endregion

        #region MediaPlayer Event Handlers

        private void MediaPlayer_Playing(object? sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsConnected = true;
                IsPlaying = true;
                HasError = false;
                StatusMessage = $"Connected to {SelectedStreamOption?.DisplayName}";
                _camera.AddProtocolLog("Live Stream", "Playing", "Stream started playing", ProtocolLogLevel.Info);
            });
        }

        private void MediaPlayer_Paused(object? sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsPaused = true;
                IsPlaying = false;
                StatusMessage = "Stream paused";
            });
        }

        private void MediaPlayer_Stopped(object? sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsConnected = false;
                IsPlaying = false;
                IsPaused = false;
                StatusMessage = "Stream stopped";
            });
        }

        private void MediaPlayer_EndReached(object? sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsPlaying = false;
                StatusMessage = "Stream ended";
                _camera.AddProtocolLog("Live Stream", "End", "Stream ended", ProtocolLogLevel.Info);
            });
        }

        private void MediaPlayer_EncounteredError(object? sender, EventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                HandleError("Media playback failed");
                _camera.AddProtocolLog("Live Stream", "Player Error", "MediaPlayer encountered an error", ProtocolLogLevel.Error);
            });
        }

        #endregion

        #region Legacy Event Handlers (for compatibility)

        public void HandleMediaOpened()
        {
            // This method is kept for compatibility with existing event handlers
            // The actual logic is now handled by MediaPlayer events
        }

        public void HandleMediaFailed(string errorMessage)
        {
            HandleError($"Media playback failed: {errorMessage}");
        }

        public void HandleMediaEnded()
        {
            IsPlaying = false;
            StatusMessage = "Stream ended";
        }

        private void HandleError(string message)
        {
            HasError = true;
            ErrorMessage = message;
            StatusMessage = "Connection failed";
            IsConnected = false;
            IsPlaying = false;
            IsPaused = false;
            IsLoading = false;
        }

        #endregion

        #region Helper Methods

        private void UpdateVisibilityStates()
        {
            OnPropertyChanged(nameof(ShowVideoPlayer));
            OnPropertyChanged(nameof(ShowDefaultState));
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                // Unsubscribe from events
                if (_mediaPlayer != null)
                {
                    _mediaPlayer.Playing -= MediaPlayer_Playing;
                    _mediaPlayer.Paused -= MediaPlayer_Paused;
                    _mediaPlayer.Stopped -= MediaPlayer_Stopped;
                    _mediaPlayer.EndReached -= MediaPlayer_EndReached;
                    _mediaPlayer.EncounteredError -= MediaPlayer_EncounteredError;

                    _mediaPlayer.Stop();
                    _mediaPlayer.Dispose();
                }

                _currentMedia?.Dispose();
                _libVLC?.Dispose();

                _camera.AddProtocolLog("Live Stream", "Dispose", "Live stream resources disposed", ProtocolLogLevel.Info);
            }
            catch (Exception ex)
            {
                _camera.AddProtocolLog("Live Stream", "Dispose Error", $"Error disposing resources: {ex.Message}", ProtocolLogLevel.Error);
            }

            _disposed = true;
        }

        #endregion
    }

    public class StreamOption
    {
        public string DisplayName { get; set; } = string.Empty;
        public string StreamUrl { get; set; } = string.Empty;
        public StreamType StreamType { get; set; }
    }

    public enum StreamType
    {
        Main,
        Sub
    }
}