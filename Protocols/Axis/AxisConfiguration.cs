using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

using wpfhikip.Models;

namespace wpfhikip.Protocols.Axis
{
    /// <summary>
    /// Configuration management for Axis cameras
    /// </summary>
    public sealed class AxisConfiguration : IDisposable
    {
        private readonly AxisConnection _connection;
        private HttpClient? _httpClient;
        private bool _disposed;

        public AxisConfiguration(AxisConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        /// <summary>
        /// Gets current network configuration from the Axis device
        /// </summary>
        public async Task<(bool Success, Dictionary<string, object> Configuration, string ErrorMessage)> GetNetworkConfigurationAsync()
        {
            try
            {
                EnsureHttpClient();

                var jsonRequest = AxisJsonTemplates.CreateGetIPv4ConfigJson();
                var content = new StringContent(jsonRequest, Encoding.UTF8, AxisContentTypes.Json);

                var url = BuildUrl(AxisUrl.NetworkSettings);
                var response = await _httpClient!.PostAsync(url, content);

                return response.StatusCode switch
                {
                    HttpStatusCode.OK => await HandleSuccessfulNetworkResponse(response),
                    HttpStatusCode.Unauthorized => (false, new Dictionary<string, object>(), AxisStatusMessages.LoginFailed),
                    _ => (false, new Dictionary<string, object>(), $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}")
                };
            }
            catch (Exception ex)
            {
                return (false, new Dictionary<string, object>(), ex.Message);
            }
        }

        /// <summary>
        /// Sets network configuration on the Axis device using Camera object
        /// </summary>
        public async Task<AxisOperationResult> SetNetworkConfigurationAsync(Camera camera)
        {
            ArgumentNullException.ThrowIfNull(camera);

            try
            {
                EnsureHttpClient();

                var jsonRequest = AxisJsonTemplates.CreateSetIPv4ConfigJson(camera);
                var content = new StringContent(jsonRequest, Encoding.UTF8, AxisContentTypes.Json);

                var url = BuildUrl(AxisUrl.NetworkSettings);
                var response = await _httpClient!.PostAsync(url, content);

                return response.StatusCode switch
                {
                    HttpStatusCode.OK => await HandleConfigurationResponse(response),
                    HttpStatusCode.Unauthorized => AxisOperationResult.CreateFailure(AxisStatusMessages.LoginFailed),
                    _ => AxisOperationResult.CreateFailure($"Failed to send network configuration: {response.StatusCode}")
                };
            }
            catch (Exception ex)
            {
                return AxisOperationResult.CreateFailure($"Error sending network configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates network configuration with validation
        /// </summary>
        public async Task<AxisOperationResult> UpdateNetworkConfigurationAsync(Camera camera)
        {
            ArgumentNullException.ThrowIfNull(camera);

            // Get current configuration
            var (getSuccess, currentConfig, getError) = await GetNetworkConfigurationAsync();
            if (!getSuccess)
            {
                return AxisOperationResult.CreateFailure($"Failed to retrieve current configuration: {getError}");
            }

            // Check if configuration needs updating
            if (!AxisJsonTemplates.HasConfigurationChanged(currentConfig, camera))
            {
                return AxisOperationResult.CreateSuccess("Configuration is already up to date");
            }

            // Send the new configuration
            return await SetNetworkConfigurationAsync(camera);
        }

        /// <summary>
        /// Gets device information from the Axis camera
        /// </summary>
        public async Task<(bool Success, Dictionary<string, object> DeviceInfo, string ErrorMessage)> GetDeviceInfoAsync()
        {
            try
            {
                EnsureHttpClient();

                var url = BuildUrl(AxisUrl.DeviceInfo);
                var response = await _httpClient!.GetAsync(url);

                return response.StatusCode switch
                {
                    HttpStatusCode.OK => await HandleParameterResponse(response),
                    HttpStatusCode.Unauthorized => (false, new Dictionary<string, object>(), AxisStatusMessages.LoginFailed),
                    _ => (false, new Dictionary<string, object>(), $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}")
                };
            }
            catch (Exception ex)
            {
                return (false, new Dictionary<string, object>(), ex.Message);
            }
        }

        /// <summary>
        /// Gets system parameters from the Axis camera
        /// </summary>
        public async Task<(bool Success, Dictionary<string, object> SystemParams, string ErrorMessage)> GetSystemParametersAsync()
        {
            try
            {
                EnsureHttpClient();

                var url = BuildUrl(AxisUrl.SystemParams);
                var response = await _httpClient!.GetAsync(url);

                return response.StatusCode switch
                {
                    HttpStatusCode.OK => await HandleParameterResponse(response),
                    HttpStatusCode.Unauthorized => (false, new Dictionary<string, object>(), AxisStatusMessages.LoginFailed),
                    _ => (false, new Dictionary<string, object>(), $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}")
                };
            }
            catch (Exception ex)
            {
                return (false, new Dictionary<string, object>(), ex.Message);
            }
        }

        /// <summary>
        /// Gets network parameters from the Axis camera
        /// </summary>
        public async Task<(bool Success, Dictionary<string, object> NetworkParams, string ErrorMessage)> GetNetworkParametersAsync()
        {
            try
            {
                EnsureHttpClient();

                var url = BuildUrl(AxisUrl.NetworkParams);
                var response = await _httpClient!.GetAsync(url);

                return response.StatusCode switch
                {
                    HttpStatusCode.OK => await HandleParameterResponse(response),
                    HttpStatusCode.Unauthorized => (false, new Dictionary<string, object>(), AxisStatusMessages.LoginFailed),
                    _ => (false, new Dictionary<string, object>(), $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}")
                };
            }
            catch (Exception ex)
            {
                return (false, new Dictionary<string, object>(), ex.Message);
            }
        }

        private async Task<(bool Success, Dictionary<string, object> Configuration, string ErrorMessage)> HandleSuccessfulNetworkResponse(HttpResponseMessage response)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            var config = AxisJsonTemplates.ParseJsonResponse(responseContent);
            return (true, config, string.Empty);
        }

        private async Task<AxisOperationResult> HandleConfigurationResponse(HttpResponseMessage response)
        {
            var responseContent = await response.Content.ReadAsStringAsync();

            try
            {
                using var document = JsonDocument.Parse(responseContent);
                var root = document.RootElement;

                if (root.TryGetProperty("error", out var errorElement))
                {
                    var errorCode = errorElement.GetProperty("code").GetInt32();
                    var errorMessage = errorElement.GetProperty("message").GetString();

                    return AxisOperationResult.CreateFailure($"Axis API error {errorCode}: {errorMessage}");
                }

                return AxisOperationResult.CreateSuccess(AxisStatusMessages.NetworkSettingsSent);
            }
            catch
            {
                // If we can't parse the response, assume success if status was OK
                return AxisOperationResult.CreateSuccess(AxisStatusMessages.NetworkSettingsSent);
            }
        }

        private async Task<(bool Success, Dictionary<string, object> Data, string ErrorMessage)> HandleParameterResponse(HttpResponseMessage response)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            var parameters = ParseAxisParameterResponse(responseContent);
            return (true, parameters, string.Empty);
        }

        private static Dictionary<string, object> ParseAxisParameterResponse(string response)
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
                    var key = line[..equalIndex].Trim();
                    var value = line[(equalIndex + 1)..].Trim();
                    result[key] = value;
                }
            }

            return result;
        }

        private string BuildUrl(string endpoint)
        {
            var portSuffix = _connection.Port is not (80 or 443) ? $":{_connection.Port}" : "";
            var protocol = _connection.Port == 443 ? "https" : "http";
            return $"{protocol}://{_connection.IpAddress}{portSuffix}{endpoint}";
        }

        private void EnsureHttpClient()
        {
            _httpClient ??= _connection.CreateAuthenticatedHttpClient();
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