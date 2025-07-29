using System.Net;
using System.Net.Http;

using wpfhikip.Models;
using wpfhikip.Protocols.Common;

namespace wpfhikip.Protocols.Axis
{
    public sealed class AxisConnection : IProtocolConnection
    {
        private static readonly string[] AxisIndicators =
        {
            "Properties.System",
            "Network.IPAddress",
            "axis-cgi",
            "AXIS",
            "apiVersion"
        };

        private HttpClient? _httpClient;
        private bool _disposed;

        public string IpAddress { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public AuthenticationMode AuthenticationMode { get; set; } = AuthenticationMode.Basic;
        public CameraProtocol ProtocolType => CameraProtocol.Axis;

        public AxisConnection(string ipAddress, int port, string username, string password)
        {
            IpAddress = ipAddress;
            Port = port;
            Username = username;
            Password = password;
        }

        public AxisConnection(string ipAddress, int port, string username, string password, AuthenticationMode authMode)
            : this(ipAddress, port, username, password)
        {
            AuthenticationMode = authMode;
        }

        /// <summary>
        /// Creates an authenticated HttpClient for external use (needed by AxisConfiguration and AxisOperation)
        /// </summary>
        public HttpClient CreateAuthenticatedHttpClient()
        {
            var handler = new HttpClientHandler();
            ConfigureAuthentication(handler);

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            client.DefaultRequestHeaders.Add("User-Agent", "AxisAPI/1.0");
            return client;
        }

        public async Task<ProtocolCompatibilityResult> CheckCompatibilityAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                InitializeHttpClient();

                var deviceInfoUrl = BuildUrl(AxisUrl.DeviceInfo);
                var response = await _httpClient!.GetAsync(deviceInfoUrl, cancellationToken);

                return response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => await HandleUnauthorizedResponse(),
                    HttpStatusCode.OK => await HandleSuccessResponse(response),
                    HttpStatusCode.NotFound => ProtocolCompatibilityResult.CreateFailure("Axis API not found - not an Axis device"),
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

                var deviceInfoUrl = BuildUrl(AxisUrl.DeviceInfo);
                var response = await _httpClient!.GetAsync(deviceInfoUrl, cancellationToken);

                return response.StatusCode switch
                {
                    HttpStatusCode.OK when await IsAxisResponseAsync(response) => AuthenticationResult.CreateSuccess(),
                    HttpStatusCode.OK => AuthenticationResult.CreateFailure("Authentication successful but device is not Axis"),
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
                // Create a temporary Camera object to use with AxisConfiguration
                var tempCamera = new Camera
                {
                    NewIP = config.IPAddress,
                    NewMask = config.SubnetMask,
                    NewGateway = config.DefaultGateway
                };

                // Use the existing AxisConfiguration class
                using var axisConfig = new AxisConfiguration(this);
                var result = await axisConfig.SetNetworkConfigurationAsync(tempCamera);

                return result.Success;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> SendNTPConfigAsync(NTPConfiguration config, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(config);

            if (!config.IsValid)
                return false;

            try
            {
                // For now, simulate NTP configuration
                // TODO: Implement actual NTP configuration using AxisConfiguration or AxisOperation
                await Task.Delay(1000, cancellationToken);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<ProtocolCompatibilityResult> HandleUnauthorizedResponse()
        {
            var authResult = await TestAuthenticationAsync();
            return ProtocolCompatibilityResult.CreateSuccess(
                CameraProtocol.Axis,
                requiresAuth: true,
                isAuthenticated: authResult.IsAuthenticated,
                authMessage: authResult.Message);
        }

        private async Task<ProtocolCompatibilityResult> HandleSuccessResponse(HttpResponseMessage response)
        {
            if (await IsAxisResponseAsync(response))
            {
                return ProtocolCompatibilityResult.CreateSuccess(
                    CameraProtocol.Axis,
                    requiresAuth: false,
                    isAuthenticated: true);
            }

            return ProtocolCompatibilityResult.CreateFailure("Device responds but is not an Axis device");
        }

        private static async Task<bool> IsAxisResponseAsync(HttpResponseMessage response)
        {
            var content = await response.Content.ReadAsStringAsync();
            return !string.IsNullOrWhiteSpace(content) &&
                   AxisIndicators.Any(indicator => content.Contains(indicator, StringComparison.OrdinalIgnoreCase));
        }

        private void InitializeHttpClient()
        {
            if (_httpClient != null) return;

            _httpClient = new HttpClient(new HttpClientHandler())
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "AxisCompatibilityChecker/1.0");
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
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "AxisCompatibilityChecker/1.0");
        }

        private void ConfigureAuthentication(HttpClientHandler handler)
        {
            var credentials = new NetworkCredential(Username, Password);

            // Axis typically uses Basic authentication
            switch (AuthenticationMode)
            {
                case AuthenticationMode.Basic:
                default:
                    handler.Credentials = credentials;
                    break;

                case AuthenticationMode.Digest:
                    var credCache = new CredentialCache();
                    credCache.Add(new Uri(BuildBaseUrl()), "Digest", credentials);
                    handler.Credentials = credCache;
                    break;

                case AuthenticationMode.NTLM:
                    var ntlmCredCache = new CredentialCache();
                    ntlmCredCache.Add(new Uri(BuildBaseUrl()), "NTLM", credentials);
                    handler.Credentials = ntlmCredCache;
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