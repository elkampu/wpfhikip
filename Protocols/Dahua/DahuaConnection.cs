using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using wpfhikip.Models;

namespace wpfhikip.Protocols.Dahua
{
    public class DahuaConnection : IDisposable
    {
        public string IpAddress { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public AuthenticationMode AuthenticationMode { get; set; } = AuthenticationMode.Digest;

        private HttpClient? _httpClient;
        private bool _disposed = false;

        public DahuaConnection(string ipAddress, int port, string username, string password)
        {
            IpAddress = ipAddress;
            Port = port;
            Username = username;
            Password = password;
        }

        public DahuaConnection(string ipAddress, int port, string username, string password, AuthenticationMode authMode)
        {
            IpAddress = ipAddress;
            Port = port;
            Username = username;
            Password = password;
            AuthenticationMode = authMode;
        }

        /// <summary>
        /// Checks if the camera is Dahua compatible by attempting to access the configManager API
        /// </summary>
        /// <returns>
        /// CompatibilityResult containing success status, whether it's Dahua compatible, 
        /// authentication status, and any error messages
        /// </returns>
        public async Task<CompatibilityResult> CheckCompatibilityAsync()
        {
            try
            {
                InitializeHttpClient();

                var deviceInfoUrl = BuildUrl(DahuaUrl.DeviceInfo);

                // First, try without authentication to check if it's a Dahua device
                var response = await _httpClient.GetAsync(deviceInfoUrl);

                var result = new CompatibilityResult();

                switch (response.StatusCode)
                {
                    case HttpStatusCode.Unauthorized: // 401
                        // This is what we expect from a Dahua device - it requires authentication
                        result.IsDahuaCompatible = true;
                        result.RequiresAuthentication = true;
                        result.Success = true;
                        result.Message = "Dahua device detected - authentication required";

                        // Now test authentication
                        var authResult = await TestAuthenticationAsync();
                        result.IsAuthenticated = authResult.IsAuthenticated;
                        result.AuthenticationMessage = authResult.Message;
                        break;

                    case HttpStatusCode.OK: // 200
                        // Device responds without authentication - might be Dahua with auth disabled
                        var content = await response.Content.ReadAsStringAsync();
                        if (IsDahuaResponse(content))
                        {
                            result.IsDahuaCompatible = true;
                            result.RequiresAuthentication = false;
                            result.IsAuthenticated = true;
                            result.Success = true;
                            result.Message = "Dahua device detected - no authentication required";
                        }
                        else
                        {
                            result.IsDahuaCompatible = false;
                            result.Success = true;
                            result.Message = "Device responds but is not a Dahua device";
                        }
                        break;

                    case HttpStatusCode.NotFound: // 404
                        result.IsDahuaCompatible = false;
                        result.Success = true;
                        result.Message = "configManager API not found - not a Dahua device";
                        break;

                    case HttpStatusCode.Forbidden: // 403
                        result.IsDahuaCompatible = true;
                        result.RequiresAuthentication = true;
                        result.Success = true;
                        result.Message = "Dahua device detected - access forbidden with current credentials";
                        break;

                    default:
                        result.IsDahuaCompatible = false;
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
                    IsDahuaCompatible = false,
                    Message = $"Network error: {ex.Message}"
                };
            }
            catch (TaskCanceledException ex)
            {
                return new CompatibilityResult
                {
                    Success = false,
                    IsDahuaCompatible = false,
                    Message = ex.InnerException is TimeoutException ? "Connection timeout" : "Request cancelled"
                };
            }
            catch (Exception ex)
            {
                return new CompatibilityResult
                {
                    Success = false,
                    IsDahuaCompatible = false,
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

                var deviceInfoUrl = BuildUrl(DahuaUrl.DeviceInfo);
                var response = await _httpClient.GetAsync(deviceInfoUrl);

                switch (response.StatusCode)
                {
                    case HttpStatusCode.OK:
                        var content = await response.Content.ReadAsStringAsync();
                        if (IsDahuaResponse(content))
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
                                Message = "Authentication successful but device is not Dahua"
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
        /// Sends network configuration to the Dahua device
        /// </summary>
        /// <param name="config">Network configuration to apply</param>
        /// <returns>Operation result</returns>
        public async Task<DahuaOperationResult> SendNetworkConfigurationAsync(NetworkConfiguration config)
        {
            try
            {
                InitializeHttpClientWithAuth();

                // First, get current configuration to check if changes are needed
                var currentConfigUrl = BuildUrl(DahuaUrl.NetworkEth0Config);
                var currentConfigResponse = await _httpClient.GetAsync(currentConfigUrl);

                if (currentConfigResponse.StatusCode != HttpStatusCode.OK)
                {
                    return new DahuaOperationResult
                    {
                        Success = false,
                        Message = "Failed to retrieve current network configuration"
                    };
                }

                var currentConfigContent = await currentConfigResponse.Content.ReadAsStringAsync();
                var currentConfig = DahuaConfigTemplates.ParseConfigResponse(currentConfigContent);

                // Check if configuration needs to be changed
                if (!DahuaConfigTemplates.HasConfigurationChanged(currentConfig, config, "network"))
                {
                    return new DahuaOperationResult
                    {
                        Success = true,
                        Message = "Network configuration is already up to date"
                    };
                }

                // Build and send the network configuration URL
                var setConfigUrl = DahuaUrl.UrlBuilders.BuildNetworkConfigUrl(
                    IpAddress, config.NewIP, config.NewMask, config.NewGateway);

                var setConfigResponse = await _httpClient.GetAsync(setConfigUrl);

                if (setConfigResponse.StatusCode == HttpStatusCode.OK)
                {
                    return new DahuaOperationResult
                    {
                        Success = true,
                        Message = DahuaStatusMessages.NetworkSettingsSent
                    };
                }
                else
                {
                    return new DahuaOperationResult
                    {
                        Success = false,
                        Message = $"Failed to send network configuration: {setConfigResponse.StatusCode}"
                    };
                }
            }
            catch (Exception ex)
            {
                return new DahuaOperationResult
                {
                    Success = false,
                    Message = $"Error sending network configuration: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Sends NTP configuration to the Dahua device
        /// </summary>
        /// <param name="config">Configuration containing NTP settings</param>
        /// <returns>Operation result</returns>
        public async Task<DahuaOperationResult> SendNtpConfigurationAsync(NetworkConfiguration config)
        {
            try
            {
                InitializeHttpClientWithAuth();

                // Send NTP configuration
                var ntpConfigUrl = DahuaUrl.UrlBuilders.BuildNtpConfigUrl(IpAddress, config.NewNTPServer);
                var ntpResponse = await _httpClient.GetAsync(ntpConfigUrl);

                if (ntpResponse.StatusCode != HttpStatusCode.OK)
                {
                    return new DahuaOperationResult
                    {
                        Success = false,
                        Message = DahuaStatusMessages.NtpServerError
                    };
                }

                // Send DST configuration
                var dstConfigUrl = DahuaUrl.UrlBuilders.BuildDstConfigUrl(IpAddress);
                var dstResponse = await _httpClient.GetAsync(dstConfigUrl);

                if (dstResponse.StatusCode == HttpStatusCode.OK)
                {
                    return new DahuaOperationResult
                    {
                        Success = true,
                        Message = DahuaStatusMessages.NtpServerSent
                    };
                }
                else
                {
                    return new DahuaOperationResult
                    {
                        Success = true,
                        Message = "NTP sent successfully, but DST configuration failed"
                    };
                }
            }
            catch (Exception ex)
            {
                return new DahuaOperationResult
                {
                    Success = false,
                    Message = $"Error sending NTP configuration: {ex.Message}"
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
                return result.IsDahuaCompatible;
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
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "DahuaCompatibilityChecker/1.0");
        }

        private void InitializeHttpClientWithAuth()
        {
            _httpClient?.Dispose();

            var handler = new HttpClientHandler();

            // Configure authentication based on the authentication mode
            switch (AuthenticationMode)
            {
                case AuthenticationMode.Digest:
                    var credCache = new CredentialCache();
                    credCache.Add(new Uri(BuildBaseUrl()), "Digest", new NetworkCredential(Username, Password));
                    handler.Credentials = credCache;
                    break;

                case AuthenticationMode.Basic:
                    handler.Credentials = new NetworkCredential(Username, Password);
                    break;

                case AuthenticationMode.NTLM:
                    var ntlmCredCache = new CredentialCache();
                    ntlmCredCache.Add(new Uri(BuildBaseUrl()), "NTLM", new NetworkCredential(Username, Password));
                    handler.Credentials = ntlmCredCache;
                    break;

                default:
                    handler.Credentials = new NetworkCredential(Username, Password);
                    break;
            }

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };

            _httpClient.DefaultRequestHeaders.Add("User-Agent", "DahuaCompatibilityChecker/1.0");
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

        private static bool IsDahuaResponse(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return false;

            // Check for common Dahua response patterns
            var dahuaIndicators = new[]
            {
                "table.Network.eth0",
                "table.NTP",
                "table.General",
                "table.Locales",
                "configManager"
            };

            return dahuaIndicators.Any(indicator =>
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
    /// Result of Dahua operation
    /// </summary>
    public class DahuaOperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public Dictionary<string, string>? Data { get; set; }
    }

    /// <summary>
    /// Extended compatibility result for Dahua devices
    /// </summary>
    public class CompatibilityResult
    {
        public bool Success { get; set; }
        public bool IsDahuaCompatible { get; set; }
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
