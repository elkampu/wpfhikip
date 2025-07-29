using System.Net.Http;
using System.Text;

namespace wpfhikip.Protocols.Hikvision
{
    public class HikvisionOperation : IDisposable
    {
        private readonly HikvisionConnection _connection;
        private HttpClient? _httpClient;
        private bool _disposed = false;

        public HikvisionOperation(HikvisionConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        /// <summary>
        /// Gets the RTSP stream URL for live video
        /// </summary>
        public string GetRtspStreamUrl(int channel = 1, int subType = 0)
        {
            var protocol = _connection.Port == 443 ? "rtsps" : "rtsp";
            return $"{protocol}://{_connection.Username}:{_connection.Password}@{_connection.IpAddress}:554/Streaming/Channels/{channel}0{subType}";
        }

        /// <summary>
        /// Gets the HTTP stream URL for live video
        /// </summary>
        public string GetHttpStreamUrl(int channel = 1, int subType = 0)
        {
            var protocol = _connection.Port == 443 ? "https" : "http";
            var port = _connection.Port == 443 ? 443 : 80;
            return $"{protocol}://{_connection.IpAddress}:{port}/ISAPI/Streaming/channels/{channel}0{subType}/picture";
        }

        /// <summary>
        /// Captures a snapshot from the camera
        /// </summary>
        public async Task<(bool Success, byte[] ImageData, string ErrorMessage)> CaptureSnapshotAsync(int channel = 1)
        {
            try
            {
                EnsureHttpClient();

                var url = $"{GetBaseUrl()}/ISAPI/Streaming/channels/{channel}01/picture";
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
        public async Task<(bool Success, string ErrorMessage)> SendPtzCommandAsync(PtzCommand command, int channel = 1, int speed = 50)
        {
            try
            {
                EnsureHttpClient();

                var ptzData = GeneratePtzXml(command, speed);
                var url = $"{GetBaseUrl()}/ISAPI/PTZCtrl/channels/{channel}/continuous";

                var content = new StringContent(ptzData, Encoding.UTF8, ContentTypes.Xml);
                var response = await _httpClient!.PutAsync(url, content);

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
        public async Task<(bool Success, Dictionary<string, string> Status, string ErrorMessage)> GetCameraStatusAsync()
        {
            try
            {
                EnsureHttpClient();

                var url = $"{GetBaseUrl()}/ISAPI/System/status";
                var response = await _httpClient!.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var xmlContent = await response.Content.ReadAsStringAsync();
                    var status = HikvisionXmlTemplates.ParseResponseXml(xmlContent);
                    return (true, status, string.Empty);
                }
                else
                {
                    return (false, new Dictionary<string, string>(), $"Failed to get status: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                return (false, new Dictionary<string, string>(), $"Error getting camera status: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets streaming channel information
        /// </summary>
        public async Task<(bool Success, Dictionary<string, string> StreamingInfo, string ErrorMessage)> GetStreamingChannelInfoAsync(int channel = 1)
        {
            try
            {
                EnsureHttpClient();

                var url = $"{GetBaseUrl()}/ISAPI/Streaming/channels/{channel}01";
                var response = await _httpClient!.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var xmlContent = await response.Content.ReadAsStringAsync();
                    var streamingInfo = HikvisionXmlTemplates.ParseResponseXml(xmlContent);
                    return (true, streamingInfo, string.Empty);
                }
                else
                {
                    return (false, new Dictionary<string, string>(), $"Failed to get streaming info: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                return (false, new Dictionary<string, string>(), $"Error getting streaming info: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets video input channel capabilities
        /// </summary>
        public async Task<(bool Success, Dictionary<string, string> VideoInputInfo, string ErrorMessage)> GetVideoInputChannelInfoAsync(int channel = 1)
        {
            try
            {
                EnsureHttpClient();

                var url = $"{GetBaseUrl()}/ISAPI/System/Video/inputs/channels/{channel}";
                var response = await _httpClient!.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var xmlContent = await response.Content.ReadAsStringAsync();
                    var videoInputInfo = HikvisionXmlTemplates.ParseResponseXml(xmlContent);
                    return (true, videoInputInfo, string.Empty);
                }
                else
                {
                    return (false, new Dictionary<string, string>(), $"Failed to get video input info: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                return (false, new Dictionary<string, string>(), $"Error getting video input info: {ex.Message}");
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

                var url = $"{GetBaseUrl()}/ISAPI/ContentMgmt/record/status/channels/{channel}";
                var response = await _httpClient!.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var xmlContent = await response.Content.ReadAsStringAsync();
                    var isRecording = xmlContent.Contains("<enabled>true</enabled>", StringComparison.OrdinalIgnoreCase);
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

                var recordingXml = GenerateRecordingXml(enable);
                var url = $"{GetBaseUrl()}/ISAPI/ContentMgmt/record/tracks/{channel}01";

                var content = new StringContent(recordingXml, Encoding.UTF8, ContentTypes.Xml);
                var response = await _httpClient!.PutAsync(url, content);

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

        private string GetBaseUrl()
        {
            var protocol = _connection.Port == 443 ? "https" : "http";
            var portSuffix = _connection.Port != 80 && _connection.Port != 443 ? $":{_connection.Port}" : "";
            return $"{protocol}://{_connection.IpAddress}{portSuffix}";
        }

        private string GeneratePtzXml(PtzCommand command, int speed)
        {
            var (pan, tilt, zoom) = GetPtzValues(command, speed);

            return $@"<?xml version='1.0' encoding='UTF-8'?>
<PTZData xmlns='http://www.hikvision.com/ver20/XMLSchema' version='2.0'>
    <pan>{pan}</pan>
    <tilt>{tilt}</tilt>
    <zoom>{zoom}</zoom>
</PTZData>";
        }

        private (int pan, int tilt, int zoom) GetPtzValues(PtzCommand command, int speed)
        {
            return command switch
            {
                PtzCommand.Left => (-speed, 0, 0),
                PtzCommand.Right => (speed, 0, 0),
                PtzCommand.Up => (0, speed, 0),
                PtzCommand.Down => (0, -speed, 0),
                PtzCommand.ZoomIn => (0, 0, speed),
                PtzCommand.ZoomOut => (0, 0, -speed),
                PtzCommand.Stop => (0, 0, 0),
                _ => (0, 0, 0)
            };
        }

        private string GenerateRecordingXml(bool enable)
        {
            return $@"<?xml version='1.0' encoding='UTF-8'?>
<Track xmlns='http://www.hikvision.com/ver20/XMLSchema' version='2.0'>
    <enabled>{enable.ToString().ToLower()}</enabled>
</Track>";
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
    /// PTZ command enumeration
    /// </summary>
    public enum PtzCommand
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
        DownRight
    }
}