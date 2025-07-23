using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using wpfhikip.Models;

namespace wpfhikip.Protocols.Axis
{
    public class AxisConfiguration : IDisposable
    {
        private readonly AxisConnection _connection;
        private HttpClient? _httpClient;
        private bool _disposed = false;

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

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var config = AxisJsonTemplates.ParseJsonResponse(responseContent);
                    return (true, config, string.Empty);
                }
                else if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return (false, new Dictionary<string, object>(), AxisStatusMessages.LoginFailed);
                }
                else
                {
                    return (false, new Dictionary<string, object>(), $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                }
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
            try
            {
                EnsureHttpClient();

                // Create the JSON request for setting IPv4 configuration
                var jsonRequest = AxisJsonTemplates.CreateSetIPv4ConfigJson(camera);
                var content = new StringContent(jsonRequest, Encoding.UTF8, AxisContentTypes.Json);

                var url = BuildUrl(AxisUrl.NetworkSettings);
                var response = await _httpClient!.PostAsync(url, content);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();

                    // Parse response to check for errors
                    try
                    {
                        using var document = JsonDocument.Parse(responseContent);
                        var root = document.RootElement;

                        if (root.TryGetProperty("error", out var errorElement))
                        {
                            var errorCode = errorElement.GetProperty("code").GetInt32();
                            var errorMessage = errorElement.GetProperty("message").GetString();

                            return new AxisOperationResult
                            {
                                Success = false,
                                Message = $"Axis API error {errorCode}: {errorMessage}"
                            };
                        }

                        return new AxisOperationResult
                        {
                            Success = true,
                            Message = AxisStatusMessages.NetworkSettingsSent
                        };
                    }
                    catch
                    {
                        // If we can't parse the response, assume success if status was OK
                        return new AxisOperationResult
                        {
                            Success = true,
                            Message = AxisStatusMessages.NetworkSettingsSent
                        };
                    }
                }
                else if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return new AxisOperationResult
                    {
                        Success = false,
                        Message = AxisStatusMessages.LoginFailed
                    };
                }
                else
                {
                    return new AxisOperationResult
                    {
                        Success = false,
                        Message = $"Failed to send network configuration: {response.StatusCode}"
                    };
                }
            }
            catch (Exception ex)
            {
                return new AxisOperationResult
                {
                    Success = false,
                    Message = $"Error sending network configuration: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Updates network configuration with validation
        /// </summary>
        public async Task<AxisOperationResult> UpdateNetworkConfigurationAsync(Camera camera)
        {
            // Step 1: Get current configuration
            var (getSuccess, currentConfig, getError) = await GetNetworkConfigurationAsync();
            if (!getSuccess)
            {
                return new AxisOperationResult
                {
                    Success = false,
                    Message = $"Failed to retrieve current configuration: {getError}"
                };
            }

            // Step 2: Check if configuration actually needs updating
            if (!AxisJsonTemplates.HasConfigurationChanged(currentConfig, camera))
            {
                return new AxisOperationResult
                {
                    Success = true,
                    Message = "Configuration is already up to date"
                };
            }

            // Step 3: Send the new configuration
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

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();

                    // Parse the response (Axis uses key-value format for param.cgi)
                    var deviceInfo = ParseAxisParameterResponse(responseContent);
                    return (true, deviceInfo, string.Empty);
                }
                else if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return (false, new Dictionary<string, object>(), AxisStatusMessages.LoginFailed);
                }
                else
                {
                    return (false, new Dictionary<string, object>(), $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                }
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

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var systemParams = ParseAxisParameterResponse(responseContent);
                    return (true, systemParams, string.Empty);
                }
                else if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return (false, new Dictionary<string, object>(), AxisStatusMessages.LoginFailed);
                }
                else
                {
                    return (false, new Dictionary<string, object>(), $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                }
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

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var networkParams = ParseAxisParameterResponse(responseContent);
                    return (true, networkParams, string.Empty);
                }
                else if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return (false, new Dictionary<string, object>(), AxisStatusMessages.LoginFailed);
                }
                else
                {
                    return (false, new Dictionary<string, object>(), $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                return (false, new Dictionary<string, object>(), ex.Message);
            }
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

        private string BuildUrl(string endpoint)
        {
            var portSuffix = _connection.Port != 80 && _connection.Port != 443 ? $":{_connection.Port}" : "";
            var protocol = _connection.Port == 443 ? "https" : "http";
            return $"{protocol}://{_connection.IpAddress}{portSuffix}{endpoint}";
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
}