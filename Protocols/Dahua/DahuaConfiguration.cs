using System.Net;
using System.Net.Http;
using System.Text;

using wpfhikip.Models;
using wpfhikip.Protocols.Common;

namespace wpfhikip.Protocols.Dahua
{
    /// <summary>
    /// Dahua protocol configuration implementation
    /// </summary>
    public sealed class DahuaConfiguration : IProtocolConfiguration
    {
        private readonly DahuaConnection _connection;
        private HttpClient? _httpClient;
        private bool _disposed;

        public DahuaConfiguration(DahuaConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        public async Task<ProtocolOperationResult<Dictionary<string, object>>> GetDeviceInfoAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                EnsureHttpClient();

                var deviceInfoUrl = DahuaUrl.UrlBuilders.BuildGetUrl(_connection.IpAddress, DahuaUrl.DeviceInfo, _connection.Port == 443);
                var response = await _httpClient!.GetAsync(deviceInfoUrl, cancellationToken);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(
                        $"Failed to get device info: {response.StatusCode} - {response.ReasonPhrase}");
                }

                var content = await response.Content.ReadAsStringAsync();
                var parsedConfig = DahuaConfigTemplates.ParseConfigResponse(content);

                if (!DahuaConfigTemplates.ValidateConfigResponse(content))
                {
                    return ProtocolOperationResult<Dictionary<string, object>>.CreateFailure("Invalid Dahua response format");
                }

                // Convert to object dictionary and add additional device information
                var deviceData = parsedConfig.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);

                // Extract commonly used device information
                deviceData.TryAdd("DeviceModel", DahuaConfigTemplates.ConfigExtractor.GetDeviceModel(parsedConfig));
                deviceData.TryAdd("CurrentIP", DahuaConfigTemplates.ConfigExtractor.GetCurrentIP(parsedConfig));

                return ProtocolOperationResult<Dictionary<string, object>>.CreateSuccess(deviceData);
            }
            catch (Exception ex)
            {
                return ProtocolOperationResult<Dictionary<string, object>>.CreateFailure($"Error getting device info: {ex.Message}");
            }
        }

        public async Task<ProtocolOperationResult<Dictionary<string, object>>> GetNetworkInfoAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                EnsureHttpClient();

                var networkUrl = DahuaUrl.UrlBuilders.BuildGetUrl(_connection.IpAddress, DahuaUrl.NetworkEth0Config, _connection.Port == 443);
                var response = await _httpClient!.GetAsync(networkUrl, cancellationToken);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    return ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(
                        $"Failed to get network info: {response.StatusCode} - {response.ReasonPhrase}");
                }

                var content = await response.Content.ReadAsStringAsync();
                var parsedConfig = DahuaConfigTemplates.ParseConfigResponse(content);

                // Convert to object dictionary
                var networkData = parsedConfig.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);

                // Add extracted network information for easier access
                networkData.TryAdd("IPAddress", DahuaConfigTemplates.ConfigExtractor.GetCurrentIP(parsedConfig));
                networkData.TryAdd("SubnetMask", DahuaConfigTemplates.ConfigExtractor.GetCurrentSubnetMask(parsedConfig));
                networkData.TryAdd("DefaultGateway", DahuaConfigTemplates.ConfigExtractor.GetCurrentGateway(parsedConfig));

                return ProtocolOperationResult<Dictionary<string, object>>.CreateSuccess(networkData);
            }
            catch (Exception ex)
            {
                return ProtocolOperationResult<Dictionary<string, object>>.CreateFailure($"Error getting network info: {ex.Message}");
            }
        }

        public async Task<ProtocolOperationResult<Dictionary<string, object>>> GetVideoInfoAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                EnsureHttpClient();

                // Dahua doesn't have a direct video info endpoint like Hikvision
                // We'll try to get some basic information from available endpoints
                var videoData = new Dictionary<string, object>();

                // Get basic system info which might contain video-related information
                var systemUrl = DahuaUrl.UrlBuilders.BuildGetUrl(_connection.IpAddress, DahuaUrl.SystemInfo, _connection.Port == 443);
                var response = await _httpClient!.GetAsync(systemUrl, cancellationToken);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var parsedConfig = DahuaConfigTemplates.ParseConfigResponse(content);

                    foreach (var item in parsedConfig)
                    {
                        videoData[item.Key] = item.Value;
                    }
                }

                // Add default stream information
                using var operation = new DahuaOperation(_connection);
                videoData["MainStreamUrl"] = operation.GetMainStreamUrl(1);
                videoData["SubStreamUrl"] = operation.GetSubStreamUrl(1);
                videoData["HttpStreamUrl"] = operation.GetHttpStreamUrl(1);

                return ProtocolOperationResult<Dictionary<string, object>>.CreateSuccess(videoData);
            }
            catch (Exception ex)
            {
                return ProtocolOperationResult<Dictionary<string, object>>.CreateFailure($"Error getting video info: {ex.Message}");
            }
        }

        public async Task<ProtocolOperationResult<bool>> SetNetworkConfigurationAsync(Camera camera, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(camera);

            try
            {
                EnsureHttpClient();

                // Get current configuration first
                var currentConfigUrl = DahuaUrl.UrlBuilders.BuildGetUrl(_connection.IpAddress, DahuaUrl.NetworkEth0Config, _connection.Port == 443);
                var currentResponse = await _httpClient!.GetAsync(currentConfigUrl, cancellationToken);

                if (currentResponse.StatusCode != HttpStatusCode.OK)
                {
                    return ProtocolOperationResult<bool>.CreateFailure("Failed to retrieve current network configuration");
                }

                var currentContent = await currentResponse.Content.ReadAsStringAsync();
                var currentConfig = DahuaConfigTemplates.ParseConfigResponse(currentContent);

                // Check if configuration has changed
                if (!DahuaConfigTemplates.HasConfigurationChanged(currentConfig, camera, "network"))
                {
                    return ProtocolOperationResult<bool>.CreateSuccess(true); // No changes needed
                }

                // Create network configuration parameters
                var networkParams = DahuaConfigTemplates.CreateNetworkConfigParameters(camera);

                if (networkParams.Count == 0)
                {
                    return ProtocolOperationResult<bool>.CreateFailure("No network parameters to update");
                }

                // Build configuration URL
                var configUrl = DahuaUrl.UrlBuilders.BuildSetConfigUrl(_connection.IpAddress, networkParams, _connection.Port == 443);

                // Send configuration (Dahua uses GET for configuration changes)
                var configResponse = await _httpClient.GetAsync(configUrl, cancellationToken);

                if (configResponse.StatusCode == HttpStatusCode.OK)
                {
                    return ProtocolOperationResult<bool>.CreateSuccess(true);
                }
                else
                {
                    return ProtocolOperationResult<bool>.CreateFailure(
                        $"Failed to set network configuration: {configResponse.StatusCode} - {configResponse.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                return ProtocolOperationResult<bool>.CreateFailure($"Error setting network configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets NTP configuration on the Dahua device
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> SetNtpConfigurationAsync(Camera camera, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(camera);

            try
            {
                EnsureHttpClient();

                // Get current NTP configuration first
                var currentConfigUrl = DahuaUrl.UrlBuilders.BuildGetUrl(_connection.IpAddress, DahuaUrl.NtpConfig, _connection.Port == 443);
                var currentResponse = await _httpClient!.GetAsync(currentConfigUrl, cancellationToken);

                if (currentResponse.StatusCode != HttpStatusCode.OK)
                {
                    return (false, "Failed to retrieve current NTP configuration");
                }

                var currentContent = await currentResponse.Content.ReadAsStringAsync();
                var currentConfig = DahuaConfigTemplates.ParseConfigResponse(currentContent);

                // Check if NTP configuration has changed
                if (!DahuaConfigTemplates.HasConfigurationChanged(currentConfig, camera, "ntp"))
                {
                    return (true, string.Empty); // No changes needed
                }

                // Create NTP configuration parameters
                var ntpParams = DahuaConfigTemplates.CreateNtpConfigParameters(camera);

                if (!string.IsNullOrEmpty(camera.NewNTPServer))
                {
                    // Build NTP configuration URL
                    var ntpConfigUrl = DahuaUrl.UrlBuilders.BuildSetConfigUrl(_connection.IpAddress, ntpParams, _connection.Port == 443);

                    // Send NTP configuration (Dahua uses GET for configuration changes)
                    var ntpResponse = await _httpClient.GetAsync(ntpConfigUrl, cancellationToken);

                    if (ntpResponse.StatusCode != HttpStatusCode.OK)
                    {
                        return (false, $"Failed to set NTP configuration: {ntpResponse.StatusCode} - {ntpResponse.ReasonPhrase}");
                    }

                    // Also send DST configuration if needed
                    var dstParams = DahuaConfigTemplates.CreateDstConfigParameters();
                    var dstConfigUrl = DahuaUrl.UrlBuilders.BuildSetConfigUrl(_connection.IpAddress, dstParams, _connection.Port == 443);
                    var dstResponse = await _httpClient.GetAsync(dstConfigUrl, cancellationToken);

                    if (dstResponse.StatusCode != HttpStatusCode.OK)
                    {
                        return (false, $"NTP configuration set but DST configuration failed: {dstResponse.StatusCode}");
                    }
                }

                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, $"Error setting NTP configuration: {ex.Message}");
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

    /// <summary>
    /// Extension to add authenticated HttpClient creation to DahuaConnection
    /// </summary>
    public static class DahuaConnectionExtensions
    {
        /// <summary>
        /// Creates a new HttpClient instance with authentication configured
        /// </summary>
        public static HttpClient CreateAuthenticatedHttpClient(this DahuaConnection connection)
        {
            var handler = new HttpClientHandler();
            ConfigureAuthentication(handler, connection);

            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            httpClient.DefaultRequestHeaders.Add("User-Agent", "DahuaClient/1.0");

            return httpClient;
        }

        private static void ConfigureAuthentication(HttpClientHandler handler, DahuaConnection connection)
        {
            var protocol = connection.Port == 443 ? "https" : "http";
            var portSuffix = connection.Port is not (80 or 443) ? $":{connection.Port}" : "";
            var baseUrl = $"{protocol}://{connection.IpAddress}{portSuffix}";
            var baseUri = new Uri(baseUrl);
            var credentials = new NetworkCredential(connection.Username, connection.Password);

            switch (connection.AuthenticationMode)
            {
                case AuthenticationMode.Digest:
                    var credCache = new CredentialCache();
                    credCache.Add(baseUri, "Digest", credentials);
                    handler.Credentials = credCache;
                    break;

                case AuthenticationMode.Basic:
                    handler.Credentials = credentials;
                    break;

                case AuthenticationMode.NTLM:
                    var ntlmCredCache = new CredentialCache();
                    ntlmCredCache.Add(baseUri, "NTLM", credentials);
                    handler.Credentials = ntlmCredCache;
                    break;

                default:
                    handler.Credentials = credentials;
                    break;
            }
        }
    }
}