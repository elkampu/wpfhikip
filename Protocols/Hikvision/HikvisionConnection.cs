using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using wpfhikip.Models;

namespace wpfhikip.Protocols.Hikvision
{
    public class HikvisionConnection : IDisposable
    {
        public string IpAddress { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public AuthenticationMode AuthenticationMode { get; set; } = AuthenticationMode.Digest;

        private HttpClient? _httpClient;
        private bool _disposed = false;

        public HikvisionConnection(string ipAddress, int port, string username, string password)
        {
            IpAddress = ipAddress;
            Port = port;
            Username = username;
            Password = password;
        }

        public HikvisionConnection(string ipAddress, int port, string username, string password, AuthenticationMode authMode)
        {
            IpAddress = ipAddress;
            Port = port;
            Username = username;
            Password = password;
            AuthenticationMode = authMode;
        }

        /// <summary>
        /// Checks if the camera is Hikvision compatible by attempting to access the DeviceInfo API
        /// </summary>
        /// <returns>
        /// CompatibilityResult containing success status, whether it's Hikvision compatible, 
        /// authentication status, and any error messages
        /// </returns>
        public async Task<CompatibilityResult> CheckCompatibilityAsync()
        {
            try
            {
                InitializeHttpClient();

                var deviceInfoUrl = BuildUrl(HikvisionUrl.DeviceInfo);

                // First, try without authentication to check if it's a Hikvision device
                var response = await _httpClient.GetAsync(deviceInfoUrl);

                var result = new CompatibilityResult();

                switch (response.StatusCode)
                {
                    case HttpStatusCode.Unauthorized: // 401
                        // This is what we expect from a Hikvision device - it requires authentication
                        result.IsHikvisionCompatible = true;
                        result.RequiresAuthentication = true;
                        result.Success = true;
                        result.Message = "Hikvision device detected - authentication required";

                        // Now test authentication
                        var authResult = await TestAuthenticationAsync();
                        result.IsAuthenticated = authResult.IsAuthenticated;
                        result.AuthenticationMessage = authResult.Message;
                        break;

                    case HttpStatusCode.OK: // 200
                        // Device responds without authentication - might be Hikvision with auth disabled
                        var content = await response.Content.ReadAsStringAsync();
                        if (IsHikvisionResponse(content))
                        {
                            result.IsHikvisionCompatible = true;
                            result.RequiresAuthentication = false;
                            result.IsAuthenticated = true;
                            result.Success = true;
                            result.Message = "Hikvision device detected - no authentication required";
                        }
                        else
                        {
                            result.IsHikvisionCompatible = false;
                            result.Success = true;
                            result.Message = "Device responds but is not a Hikvision device";
                        }
                        break;

                    case HttpStatusCode.NotFound: // 404
                        result.IsHikvisionCompatible = false;
                        result.Success = true;
                        result.Message = "DeviceInfo API not found - not a Hikvision device";
                        break;

                    case HttpStatusCode.Forbidden: // 403
                        result.IsHikvisionCompatible = true;
                        result.RequiresAuthentication = true;
                        result.Success = true;
                        result.Message = "Hikvision device detected - access forbidden with current credentials";
                        break;

                    default:
                        result.IsHikvisionCompatible = false;
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
                    IsHikvisionCompatible = false,
                    Message = $"Network error: {ex.Message}"
                };
            }
            catch (TaskCanceledException ex)
            {
                return new CompatibilityResult
                {
                    Success = false,
                    IsHikvisionCompatible = false,
                    Message = ex.InnerException is TimeoutException ? "Connection timeout" : "Request cancelled"
                };
            }
            catch (Exception ex)
            {
                return new CompatibilityResult
                {
                    Success = false,
                    IsHikvisionCompatible = false,
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

                var deviceInfoUrl = BuildUrl(HikvisionUrl.DeviceInfo);
                var response = await _httpClient.GetAsync(deviceInfoUrl);

                switch (response.StatusCode)
                {
                    case HttpStatusCode.OK:
                        var content = await response.Content.ReadAsStringAsync();
                        if (IsHikvisionResponse(content))
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
                                Message = "Authentication successful but device is not Hikvision"
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
        /// Synchronous version of CheckCompatibilityAsync for UI compatibility
        /// </summary>
        /// <returns>True if compatible, false otherwise</returns>
        public bool CheckCompatibility()
        {
            try
            {
                var result = CheckCompatibilityAsync().GetAwaiter().GetResult();
                return result.IsHikvisionCompatible;
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
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "HikvisionCompatibilityChecker/1.0");
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

            _httpClient.DefaultRequestHeaders.Add("User-Agent", "HikvisionCompatibilityChecker/1.0");
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

        private static bool IsHikvisionResponse(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return false;

            // Check for common Hikvision XML elements and namespaces
            var hikvisionIndicators = new[]
            {
                "http://www.hikvision.com/ver20/XMLSchema",
                "http://www.hikvision.com/ver10/XMLSchema",
                "<DeviceInfo",
                "hikvision",
                "HIKVISION"
            };

            return hikvisionIndicators.Any(indicator =>
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
    /// Result of compatibility check
    /// </summary>
    public class CompatibilityResult
    {
        public bool Success { get; set; }
        public bool IsHikvisionCompatible { get; set; }
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
