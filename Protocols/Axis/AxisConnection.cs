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
    public class AxisConnection : IDisposable
    {
        public string IpAddress { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public AuthenticationMode AuthenticationMode { get; set; } = AuthenticationMode.Basic;

        private HttpClient? _httpClient;
        private bool _disposed = false;

        public AxisConnection(string ipAddress, int port, string username, string password)
        {
            IpAddress = ipAddress;
            Port = port;
            Username = username;
            Password = password;
        }

        public AxisConnection(string ipAddress, int port, string username, string password, AuthenticationMode authMode)
        {
            IpAddress = ipAddress;
            Port = port;
            Username = username;
            Password = password;
            AuthenticationMode = authMode;
        }

        /// <summary>
        /// Checks if the camera is Axis compatible by attempting to access the device info API
        /// </summary>
        /// <returns>
        /// CompatibilityResult containing success status, whether it's Axis compatible, 
        /// authentication status, and any error messages
        /// </returns>
        public async Task<CompatibilityResult> CheckCompatibilityAsync()
        {
            try
            {
                InitializeHttpClient();

                var deviceInfoUrl = BuildUrl(AxisUrl.DeviceInfo);

                // First, try without authentication to check if it's an Axis device
                var response = await _httpClient.GetAsync(deviceInfoUrl);

                var result = new CompatibilityResult();

                switch (response.StatusCode)
                {
                    case HttpStatusCode.Unauthorized: // 401
                        // This is what we expect from an Axis device - it requires authentication
                        result.IsAxisCompatible = true;
                        result.RequiresAuthentication = true;
                        result.Success = true;
                        result.Message = "Axis device detected - authentication required";

                        // Now test authentication
                        var authResult = await TestAuthenticationAsync();
                        result.IsAuthenticated = authResult.IsAuthenticated;
                        result.AuthenticationMessage = authResult.Message;
                        break;

                    case HttpStatusCode.OK: // 200
                        // Device responds without authentication - might be Axis with auth disabled
                        var content = await response.Content.ReadAsStringAsync();
                        if (IsAxisResponse(content))
                        {
                            result.IsAxisCompatible = true;
                            result.RequiresAuthentication = false;
                            result.IsAuthenticated = true;
                            result.Success = true;
                            result.Message = "Axis device detected - no authentication required";
                        }
                        else
                        {
                            result.IsAxisCompatible = false;
                            result.Success = true;
                            result.Message = "Device responds but is not an Axis device";
                        }
                        break;

                    case HttpStatusCode.NotFound: // 404
                        result.IsAxisCompatible = false;
                        result.Success = true;
                        result.Message = "Axis API not found - not an Axis device";
                        break;

                    case HttpStatusCode.Forbidden: // 403
                        result.IsAxisCompatible = true;
                        result.RequiresAuthentication = true;
                        result.Success = true;
                        result.Message = "Axis device detected - access forbidden with current credentials";
                        break;

                    default:
                        result.IsAxisCompatible = false;
                        result.Success = false;
                        result.Message = $"Unexpected response: {response.StatusCode} - {response.ReasonPhrase}";
                        break;
                }

                return result;
            }
            catch (HttpRequestException ex)
            {
                return new CompatibilityResult
                {
                    Success = false,
                    IsAxisCompatible = false,
                    Message = $"Network error: {ex.Message}"
                };
            }
            catch (TaskCanceledException ex)
            {
                return new CompatibilityResult
                {
                    Success = false,
                    IsAxisCompatible = false,
                    Message = ex.InnerException is TimeoutException ? "Connection timeout" : "Request cancelled"
                };
            }
            catch (Exception ex)
            {
                return new CompatibilityResult
                {
                    Success = false,
                    IsAxisCompatible = false,
                    Message = $"Error checking compatibility: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Tests authentication with the provided credentials
        /// </summary>
        /// <returns>Authentication result</returns>
        public async Task<AuthenticationResult> TestAuthenticationAsync()
        {
            try
            {
                InitializeHttpClientWithAuth();

                var deviceInfoUrl = BuildUrl(AxisUrl.DeviceInfo);
                var response = await _httpClient.GetAsync(deviceInfoUrl);

                switch (response.StatusCode)
                {
                    case HttpStatusCode.OK:
                        var content = await response.Content.ReadAsStringAsync();
                        if (IsAxisResponse(content))
                        {
                            return new AuthenticationResult
                            {
                                IsAuthenticated = true,
                                Success = true,
                                Message = "Authentication successful"
                            };
                        }
                        else
                        {
                            return new AuthenticationResult
                            {
                                IsAuthenticated = false,
                                Success = true,
                                Message = "Authentication successful but device is not Axis"
                            };
                        }

                    case HttpStatusCode.Unauthorized:
                        return new AuthenticationResult
                        {
                            IsAuthenticated = false,
                            Success = true,
                            Message = "Authentication failed - invalid credentials"
                        };

                    case HttpStatusCode.Forbidden:
                        return new AuthenticationResult
                        {
                            IsAuthenticated = false,
                            Success = true,
                            Message = "Authentication failed - access forbidden"
                        };

                    default:
                        return new AuthenticationResult
                        {
                            IsAuthenticated = false,
                            Success = false,
                            Message = $"Unexpected response during authentication: {response.StatusCode}"
                        };
                }
            }
            catch (Exception ex)
            {
                return new AuthenticationResult
                {
                    IsAuthenticated = false,
                    Success = false,
                    Message = $"Error during authentication: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Sends network configuration to the Axis device
        /// </summary>
        /// <param name="config">Network configuration to apply</param>
        /// <returns>Operation result</returns>
        public async Task<AxisOperationResult> SendNetworkConfigurationAsync(NetworkConfiguration config)
        {
            try
            {
                InitializeHttpClientWithAuth();

                // Create the JSON request for setting IPv4 configuration
                var jsonRequest = AxisJsonTemplates.CreateSetIPv4ConfigJson(config);
                var content = new StringContent(jsonRequest, Encoding.UTF8, AxisContentTypes.Json);

                var networkSettingsUrl = BuildUrl(AxisUrl.NetworkSettings);
                var response = await _httpClient.PostAsync(networkSettingsUrl, content);

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
        /// Synchronous version of CheckCompatibilityAsync for UI compatibility
        /// </summary>
        /// <returns>True if compatible, false otherwise</returns>
        public bool CheckCompatibility()
        {
            try
            {
                var result = CheckCompatibilityAsync().GetAwaiter().GetResult();
                return result.IsAxisCompatible;
            }
            catch
            {
                return false;
            }
        }

        private void InitializeHttpClient()
        {
            if (_httpClient != null)
                return;

            var handler = new HttpClientHandler();
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10) // 10 second timeout for compatibility checks
            };

            // Add common headers
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "AxisCompatibilityChecker/1.0");
        }

        private void InitializeHttpClientWithAuth()
        {
            _httpClient?.Dispose();

            var handler = new HttpClientHandler();

            // Configure authentication - Axis typically uses Basic auth
            handler.Credentials = new NetworkCredential(Username, Password);

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            _httpClient.DefaultRequestHeaders.Add("User-Agent", "AxisCompatibilityChecker/1.0");
        }

        private string BuildBaseUrl()
        {
            var portSuffix = Port != 80 && Port != 443 ? $":{Port}" : "";
            var protocol = Port == 443 ? "https" : "http";
            return $"{protocol}://{IpAddress}{portSuffix}";
        }

        private string BuildUrl(string endpoint)
        {
            return $"{BuildBaseUrl()}{endpoint}";
        }

        private static bool IsAxisResponse(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return false;

            // Check for common Axis response patterns
            var axisIndicators = new[]
            {
                "Properties.System",
                "Network.IPAddress",
                "axis-cgi",
                "AXIS",
                "apiVersion"
            };

            return axisIndicators.Any(indicator =>
                content.Contains(indicator, StringComparison.OrdinalIgnoreCase));
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
    /// Result of Axis operation
    /// </summary>
    public class AxisOperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public Dictionary<string, object>? Data { get; set; }
    }

    /// <summary>
    /// Extended compatibility result for Axis devices
    /// </summary>
    public class CompatibilityResult
    {
        public bool Success { get; set; }
        public bool IsAxisCompatible { get; set; }
        public bool RequiresAuthentication { get; set; }
        public bool IsAuthenticated { get; set; }
        public string Message { get; set; } = string.Empty;
        public string AuthenticationMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Result of authentication test
    /// </summary>
    public class AuthenticationResult
    {
        public bool Success { get; set; }
        public bool IsAuthenticated { get; set; }
        public string Message { get; set; } = string.Empty;
    }
}
