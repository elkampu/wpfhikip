using System.Net.Http;

using wpfhikip.Protocols.Common;

namespace wpfhikip.Protocols.Dahua
{
    /// <summary>
    /// Dahua protocol operation implementation
    /// </summary>
    public sealed class DahuaOperation : IProtocolOperation
    {
        private readonly DahuaConnection _connection;
        private HttpClient? _httpClient;
        private bool _disposed;

        public DahuaOperation(DahuaConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        public string GetMainStreamUrl(int channel = 1)
        {
            var protocol = _connection.Port == 554 || _connection.Port == 8554 ? "rtsp" : "rtsp";
            return $"{protocol}://{_connection.Username}:{_connection.Password}@{_connection.IpAddress}:554/cam/realmonitor?channel={channel}&subtype=0";
        }

        public string GetSubStreamUrl(int channel = 1)
        {
            var protocol = _connection.Port == 554 || _connection.Port == 8554 ? "rtsp" : "rtsp";
            return $"{protocol}://{_connection.Username}:{_connection.Password}@{_connection.IpAddress}:554/cam/realmonitor?channel={channel}&subtype=1";
        }

        /// <summary>
        /// Gets the HTTP stream URL for snapshots
        /// </summary>
        public string GetHttpStreamUrl(int channel = 1)
        {
            var protocol = _connection.Port == 443 ? "https" : "http";
            var port = _connection.Port is 80 or 443 ? "" : $":{_connection.Port}";
            return $"{protocol}://{_connection.IpAddress}{port}/cgi-bin/snapshot.cgi?channel={channel}";
        }

        /// <summary>
        /// Gets alternative main stream URL format
        /// </summary>
        public string GetMainStreamUrlAlternative(int channel = 1)
        {
            return $"rtsp://{_connection.Username}:{_connection.Password}@{_connection.IpAddress}:554/live/ch{channel:D2}/main";
        }

        /// <summary>
        /// Gets alternative sub stream URL format
        /// </summary>
        public string GetSubStreamUrlAlternative(int channel = 1)
        {
            return $"rtsp://{_connection.Username}:{_connection.Password}@{_connection.IpAddress}:554/live/ch{channel:D2}/sub";
        }

        public async Task<ProtocolOperationResult<Dictionary<string, object>>> GetCameraStatusAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                EnsureHttpClient();

                var statusData = new Dictionary<string, object>();

                // Try to get device status information
                var deviceInfoUrl = DahuaUrl.UrlBuilders.BuildGetUrl(_connection.IpAddress, DahuaUrl.DeviceInfo, _connection.Port == 443);
                var response = await _httpClient!.GetAsync(deviceInfoUrl, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var parsedConfig = DahuaConfigTemplates.ParseConfigResponse(content);

                    foreach (var item in parsedConfig)
                    {
                        statusData[item.Key] = item.Value;
                    }

                    // Add connection status
                    statusData["ConnectionStatus"] = "Connected";
                    statusData["LastUpdate"] = DateTime.Now;
                    statusData["Protocol"] = "Dahua";
                }
                else
                {
                    statusData["ConnectionStatus"] = $"Error: {response.StatusCode}";
                    statusData["LastUpdate"] = DateTime.Now;
                    statusData["Protocol"] = "Dahua";
                }

                // Add stream URLs
                statusData["MainStreamUrl"] = GetMainStreamUrl(1);
                statusData["SubStreamUrl"] = GetSubStreamUrl(1);
                statusData["HttpStreamUrl"] = GetHttpStreamUrl(1);

                return ProtocolOperationResult<Dictionary<string, object>>.CreateSuccess(statusData);
            }
            catch (Exception ex)
            {
                return ProtocolOperationResult<Dictionary<string, object>>.CreateFailure($"Error getting camera status: {ex.Message}");
            }
        }

        /// <summary>
        /// Captures a snapshot from the camera
        /// </summary>
        public async Task<(bool Success, byte[] ImageData, string ErrorMessage)> CaptureSnapshotAsync(int channel = 1, CancellationToken cancellationToken = default)
        {
            try
            {
                EnsureHttpClient();

                var snapshotUrl = GetHttpStreamUrl(channel);
                var response = await _httpClient!.GetAsync(snapshotUrl, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var imageData = await response.Content.ReadAsByteArrayAsync();
                    return (true, imageData, string.Empty);
                }
                else
                {
                    return (false, Array.Empty<byte>(), $"Failed to capture snapshot: {response.StatusCode} - {response.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                return (false, Array.Empty<byte>(), $"Error capturing snapshot: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the CGI snapshot URL for Dahua cameras
        /// </summary>
        public string GetSnapshotUrl(int channel = 1)
        {
            var protocol = _connection.Port == 443 ? "https" : "http";
            var port = _connection.Port is 80 or 443 ? "" : $":{_connection.Port}";
            return $"{protocol}://{_connection.Username}:{_connection.Password}@{_connection.IpAddress}{port}/cgi-bin/snapshot.cgi?channel={channel}";
        }

        /// <summary>
        /// Tests the camera connection by attempting to get device info
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                EnsureHttpClient();

                var deviceInfoUrl = DahuaUrl.UrlBuilders.BuildGetUrl(_connection.IpAddress, DahuaUrl.DeviceInfo, _connection.Port == 443);
                var response = await _httpClient!.GetAsync(deviceInfoUrl, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    if (DahuaConfigTemplates.ValidateConfigResponse(content))
                    {
                        return (true, DahuaStatusMessages.ConnectionOk);
                    }
                    else
                    {
                        return (false, "Device responded but is not a Dahua device");
                    }
                }
                else
                {
                    return (false, $"Connection failed: {response.StatusCode} - {response.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Connection test failed: {ex.Message}");
            }
        }

        private void EnsureHttpClient()
        {
            if (_httpClient != null) return;

            _httpClient = _connection.CreateAuthenticatedHttpClient();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient?.Dispose();
                _disposed = true;
            }
        }
    }
}