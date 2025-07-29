using System.Net;
using System.Net.Http;

using wpfhikip.Models;
using wpfhikip.Protocols.Common;

namespace wpfhikip.Protocols.Hikvision
{
    public sealed class HikvisionConnection : IProtocolConnection
    {
        private static readonly string[] HikvisionIndicators =
        {
            "http://www.hikvision.com/ver20/XMLSchema",
            "http://www.hikvision.com/ver10/XMLSchema",
            "<DeviceInfo",
            "hikvision",
            "HIKVISION"
        };

        private HttpClient? _httpClient;
        private bool _disposed;

        public string IpAddress { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public AuthenticationMode AuthenticationMode { get; set; } = AuthenticationMode.Digest;
        public CameraProtocol ProtocolType => CameraProtocol.Hikvision;

        public HikvisionConnection(string ipAddress, int port, string username, string password)
        {
            IpAddress = ipAddress;
            Port = port;
            Username = username;
            Password = password;
        }

        public HikvisionConnection(string ipAddress, int port, string username, string password, AuthenticationMode authMode)
            : this(ipAddress, port, username, password)
        {
            AuthenticationMode = authMode;
        }

        public async Task<ProtocolCompatibilityResult> CheckCompatibilityAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                InitializeHttpClient();

                var deviceInfoUrl = BuildUrl(HikvisionUrl.DeviceInfo);
                var response = await _httpClient!.GetAsync(deviceInfoUrl, cancellationToken);

                return response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => await HandleUnauthorizedResponse(),
                    HttpStatusCode.OK => await HandleSuccessResponse(response),
                    HttpStatusCode.NotFound => ProtocolCompatibilityResult.CreateFailure("DeviceInfo API not found - not a Hikvision device"),
                    HttpStatusCode.Forbidden => await HandleUnauthorizedResponse(),
                    _ => ProtocolCompatibilityResult.CreateFailure($"Unexpected response: {response.StatusCode} - {response.ReasonPhrase}")
                };
            }
            catch (HttpRequestException ex)
            {
                return ProtocolCompatibilityResult.CreateFailure($"Network error: {ex.Message}");
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                return ProtocolCompatibilityResult.CreateFailure("Connection timeout");
            }
            catch (TaskCanceledException)
            {
                return ProtocolCompatibilityResult.CreateFailure("Request cancelled");
            }
            catch (Exception ex)
            {
                return ProtocolCompatibilityResult.CreateFailure($"Error checking compatibility: {ex.Message}");
            }
        }

        public async Task<AuthenticationResult> TestAuthenticationAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                InitializeHttpClientWithAuth();

                var deviceInfoUrl = BuildUrl(HikvisionUrl.DeviceInfo);
                var response = await _httpClient!.GetAsync(deviceInfoUrl, cancellationToken);

                return response.StatusCode switch
                {
                    HttpStatusCode.OK when await IsHikvisionResponseAsync(response) => AuthenticationResult.CreateSuccess(),
                    HttpStatusCode.OK => AuthenticationResult.CreateFailure("Authentication successful but device is not Hikvision"),
                    HttpStatusCode.Unauthorized => AuthenticationResult.CreateFailure("Authentication failed - invalid credentials"),
                    HttpStatusCode.Forbidden => AuthenticationResult.CreateFailure("Authentication failed - access forbidden"),
                    _ => AuthenticationResult.CreateError($"Unexpected response during authentication: {response.StatusCode}")
                };
            }
            catch (Exception ex)
            {
                return AuthenticationResult.CreateError($"Error during authentication: {ex.Message}");
            }
        }

        public async Task<bool> SendNetworkConfigAsync(NetworkConfiguration config, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(config);

            if (!config.IsValid)
                return false;

            try
            {
                // Create a minimal temporary Camera object for the configuration API
                var tempCamera = new Camera
                {
                    NewIP = config.IPAddress,
                    NewMask = config.SubnetMask,
                    NewGateway = config.DefaultGateway
                };

                // Use the existing HikvisionConfiguration class
                using var hikvisionConfig = new HikvisionConfiguration(this);
                var (success, errorMessage) = await hikvisionConfig.UpdateNetworkSettingsAsync(tempCamera);

                if (!success && !string.IsNullOrEmpty(errorMessage))
                {
                    throw new InvalidOperationException($"Hikvision configuration failed: {errorMessage}");
                }

                return success;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to send Hikvision network configuration: {ex.Message}", ex);
            }
        }

        public async Task<bool> SendNTPConfigAsync(NTPConfiguration config, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(config);

            if (!config.IsValid)
                return false;

            try
            {
                // Create a temporary Camera object to use with HikvisionConfiguration
                var tempCamera = new Camera
                {
                    NewNTPServer = config.NTPServer
                };

                // Use the existing HikvisionConfiguration class for proper implementation
                using var hikvisionConfig = new HikvisionConfiguration(this);
                var (success, errorMessage) = await hikvisionConfig.UpdateNtpSettingsAsync(tempCamera);

                if (!success && !string.IsNullOrEmpty(errorMessage))
                {
                    throw new InvalidOperationException($"Hikvision NTP configuration failed: {errorMessage}");
                }

                return success;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to send Hikvision NTP configuration: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Creates a new HttpClient instance with authentication configured
        /// </summary>
        /// <returns>A new HttpClient instance with proper authentication</returns>
        public HttpClient CreateAuthenticatedHttpClient()
        {
            var handler = new HttpClientHandler();
            ConfigureAuthentication(handler);

            var httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            httpClient.DefaultRequestHeaders.Add("User-Agent", "HikvisionClient/1.0");

            return httpClient;
        }

        private async Task<ProtocolCompatibilityResult> HandleUnauthorizedResponse()
        {
            var authResult = await TestAuthenticationAsync();
            return ProtocolCompatibilityResult.CreateSuccess(
                CameraProtocol.Hikvision,
                requiresAuth: true,
                isAuthenticated: authResult.IsAuthenticated,
                authMessage: authResult.Message);
        }

        private async Task<ProtocolCompatibilityResult> HandleSuccessResponse(HttpResponseMessage response)
        {
            if (await IsHikvisionResponseAsync(response))
            {
                return ProtocolCompatibilityResult.CreateSuccess(
                    CameraProtocol.Hikvision,
                    requiresAuth: false,
                    isAuthenticated: true);
            }

            return ProtocolCompatibilityResult.CreateFailure("Device responds but is not a Hikvision device");
        }

        private static async Task<bool> IsHikvisionResponseAsync(HttpResponseMessage response)
        {
            var content = await response.Content.ReadAsStringAsync();
            return !string.IsNullOrWhiteSpace(content) &&
                   HikvisionIndicators.Any(indicator => content.Contains(indicator, StringComparison.OrdinalIgnoreCase));
        }

        private void InitializeHttpClient()
        {
            if (_httpClient != null) return;

            _httpClient = new HttpClient(new HttpClientHandler())
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "HikvisionCompatibilityChecker/1.0");
        }

        private void InitializeHttpClientWithAuth()
        {
            _httpClient?.Dispose();

            var handler = new HttpClientHandler();
            ConfigureAuthentication(handler);

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "HikvisionCompatibilityChecker/1.0");
        }

        private void ConfigureAuthentication(HttpClientHandler handler)
        {
            var baseUri = new Uri(BuildBaseUrl());
            var credentials = new NetworkCredential(Username, Password);

            switch (AuthenticationMode)
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

        private string BuildBaseUrl()
        {
            var protocol = Port == 443 ? "https" : "http";
            var portSuffix = Port is not (80 or 443) ? $":{Port}" : "";
            return $"{protocol}://{IpAddress}{portSuffix}";
        }

        private string BuildUrl(string endpoint) => $"{BuildBaseUrl()}{endpoint}";

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