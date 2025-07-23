using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using wpfhikip.Models;

namespace wpfhikip.Protocols.Axis
{
    public class AxisOperation : IDisposable
    {
        private readonly AxisConnection _connection;
        private HttpClient? _httpClient;
        private bool _disposed = false;

        public AxisOperation(AxisConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        /// <summary>
        /// Gets the RTSP stream URL for live video
        /// </summary>
        public string GetRtspStreamUrl(int channel = 1, string profile = "1")
        {
            var protocol = _connection.Port == 443 ? "rtsps" : "rtsp";
            var port = _connection.Port == 443 ? 322 : 554; // Axis RTSP ports
            return $"{protocol}://{_connection.Username}:{_connection.Password}@{_connection.IpAddress}:{port}/axis-media/media.amp?camera={channel}&videocodec=h264&resolution=1920x1080";
        }

        /// <summary>
        /// Gets the MJPEG stream URL for live video
        /// </summary>
        public string GetMjpegStreamUrl(int channel = 1, int resolution = 1920)
        {
            var protocol = _connection.Port == 443 ? "https" : "http";
            var port = _connection.Port == 443 ? 443 : 80;
            return $"{protocol}://{_connection.IpAddress}:{port}/axis-cgi/mjpg/video.cgi?camera={channel}&resolution={resolution}x{resolution * 9 / 16}";
        }

        /// <summary>
        /// Captures a snapshot from the camera
        /// </summary>
        public async Task<(bool Success, byte[] ImageData, string ErrorMessage)> CaptureSnapshotAsync(int channel = 1, int resolution = 1920)
        {
            try
            {
                EnsureHttpClient();

                var url = $"{GetBaseUrl()}/axis-cgi/jpg/image.cgi?camera={channel}&resolution={resolution}x{resolution * 9 / 16}";
                var response = await _httpClient!.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var imageData = await response.Content.ReadAsByteArrayAsync();
                    return (true, imageData, string.Empty);
                }
                else
                {
                    return (false, Array.Empty<byte>(), $"Failed to capture snapshot: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                return (false, Array.Empty<byte>(), $"Error capturing snapshot: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends PTZ command to the camera
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> SendPtzCommandAsync(AxisPtzCommand command, int channel = 1, int speed = 50)
        {
            try
            {
                EnsureHttpClient();

                var ptzParams = GeneratePtzParams(command, speed);
                var url = $"{GetBaseUrl()}/axis-cgi/com/ptz.cgi?camera={channel}&{ptzParams}";

                var response = await _httpClient!.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    return (true, string.Empty);
                }
                else
                {
                    return (false, $"PTZ command failed: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Error sending PTZ command: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets camera status information
        /// </summary>
        public async Task<(bool Success, Dictionary<string, object> Status, string ErrorMessage)> GetCameraStatusAsync()
        {
            try
            {
                EnsureHttpClient();

                var url = $"{GetBaseUrl()}/axis-cgi/param.cgi?action=list&group=Properties.System";
                var response = await _httpClient!.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var status = ParseAxisParameterResponse(content);
                    return (true, status, string.Empty);
                }
                else
                {
                    return (false, new Dictionary<string, object>(), $"Failed to get status: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                return (false, new Dictionary<string, object>(), $"Error getting camera status: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets recording status
        /// </summary>
        public async Task<(bool Success, bool IsRecording, string ErrorMessage)> GetRecordingStatusAsync(int channel = 1)
        {
            try
            {
                EnsureHttpClient();

                var url = $"{GetBaseUrl()}/axis-cgi/param.cgi?action=list&group=Properties.LocalStorage.Recording";
                var response = await _httpClient!.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var isRecording = content.Contains("Recording=yes", StringComparison.OrdinalIgnoreCase);
                    return (true, isRecording, string.Empty);
                }
                else
                {
                    return (false, false, $"Failed to get recording status: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                return (false, false, $"Error getting recording status: {ex.Message}");
            }
        }

        /// <summary>
        /// Starts or stops recording
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> SetRecordingAsync(bool enable, int channel = 1)
        {
            try
            {
                EnsureHttpClient();

                var action = enable ? "start" : "stop";
                var url = $"{GetBaseUrl()}/axis-cgi/record/{action}.cgi?camera={channel}";

                var response = await _httpClient!.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    return (true, string.Empty);
                }
                else
                {
                    return (false, $"Failed to {(enable ? "start" : "stop")} recording: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Error {(enable ? "starting" : "stopping")} recording: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets motion detection status
        /// </summary>
        public async Task<(bool Success, bool IsMotionDetected, string ErrorMessage)> GetMotionDetectionStatusAsync()
        {
            try
            {
                EnsureHttpClient();

                var url = $"{GetBaseUrl()}/axis-cgi/param.cgi?action=list&group=Properties.Motion";
                var response = await _httpClient!.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var isMotionDetected = content.Contains("Motion=yes", StringComparison.OrdinalIgnoreCase);
                    return (true, isMotionDetected, string.Empty);
                }
                else
                {
                    return (false, false, $"Failed to get motion detection status: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                return (false, false, $"Error getting motion detection status: {ex.Message}");
            }
        }

        private string GetBaseUrl()
        {
            var protocol = _connection.Port == 443 ? "https" : "http";
            var portSuffix = _connection.Port != 80 && _connection.Port != 443 ? $":{_connection.Port}" : "";
            return $"{protocol}://{_connection.IpAddress}{portSuffix}";
        }

        private string GeneratePtzParams(AxisPtzCommand command, int speed)
        {
            return command switch
            {
                AxisPtzCommand.Left => $"move=left&speed={speed}",
                AxisPtzCommand.Right => $"move=right&speed={speed}",
                AxisPtzCommand.Up => $"move=up&speed={speed}",
                AxisPtzCommand.Down => $"move=down&speed={speed}",
                AxisPtzCommand.ZoomIn => $"zoom=tele&speed={speed}",
                AxisPtzCommand.ZoomOut => $"zoom=wide&speed={speed}",
                AxisPtzCommand.Stop => "move=stop",
                AxisPtzCommand.Home => "move=home",
                AxisPtzCommand.UpLeft => $"move=upleft&speed={speed}",
                AxisPtzCommand.UpRight => $"move=upright&speed={speed}",
                AxisPtzCommand.DownLeft => $"move=downleft&speed={speed}",
                AxisPtzCommand.DownRight => $"move=downright&speed={speed}",
                _ => "move=stop"
            };
        }

        private Dictionary<string, object> ParseAxisParameterResponse(string response)
        {
            var result = new Dictionary<string, object>();

            if (string.IsNullOrWhiteSpace(response))
                return result;

            var lines = response.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var equalIndex = line.IndexOf('=');
                if (equalIndex > 0)
                {
                    var key = line.Substring(0, equalIndex).Trim();
                    var value = line.Substring(equalIndex + 1).Trim();
                    result[key] = value;
                }
            }

            return result;
        }

        private void EnsureHttpClient()
        {
            if (_httpClient == null)
            {
                _httpClient = _connection.CreateAuthenticatedHttpClient();
            }
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
                    _httpClient?.Dispose();
                }
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Axis PTZ command enumeration
    /// </summary>
    public enum AxisPtzCommand
    {
        Stop,
        Up,
        Down,
        Left,
        Right,
        ZoomIn,
        ZoomOut,
        UpLeft,
        UpRight,
        DownLeft,
        DownRight,
        Home
    }
}