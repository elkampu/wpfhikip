using System.Net;
using System.Net.Http;

using wpfhikip.Models;
using wpfhikip.Protocols.Common;

namespace wpfhikip.Protocols.Dahua
{
    public sealed class DahuaConnection : IProtocolConnection
    {
        private static readonly string[] DahuaIndicators =
        {
            "table.Network.eth0",
            "table.NTP",
            "table.General",
            "table.Locales",
            "configManager"
        };

        private HttpClient? _httpClient;
        private bool _disposed;

        public string IpAddress { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public AuthenticationMode AuthenticationMode { get; set; } = AuthenticationMode.Digest;
        public CameraProtocol ProtocolType => CameraProtocol.Dahua;

        public DahuaConnection(string ipAddress, int port, string username, string password)
        {
            IpAddress = ipAddress;
            Port = port;
            Username = username;
            Password = password;
        }

        public DahuaConnection(string ipAddress, int port, string username, string password, AuthenticationMode authMode)
            : this(ipAddress, port, username, password)
        {
            AuthenticationMode = authMode;
        }

        public async Task<ProtocolCompatibilityResult> CheckCompatibilityAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                InitializeHttpClient();

                var deviceInfoUrl = BuildUrl(DahuaUrl.DeviceInfo);
                var response = await _httpClient!.GetAsync(deviceInfoUrl, cancellationToken);

                return response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => await HandleUnauthorizedResponse(),
                    HttpStatusCode.OK => await HandleSuccessResponse(response),
                    HttpStatusCode.NotFound => ProtocolCompatibilityResult.CreateFailure("configManager API not found - not a Dahua device"),
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

                var deviceInfoUrl = BuildUrl(DahuaUrl.DeviceInfo);
                var response = await _httpClient!.GetAsync(deviceInfoUrl, cancellationToken);

                return response.StatusCode switch
                {
                    HttpStatusCode.OK when await IsDahuaResponseAsync(response) => AuthenticationResult.CreateSuccess(),
                    HttpStatusCode.OK => AuthenticationResult.CreateFailure("Authentication successful but device is not Dahua"),
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
                InitializeHttpClientWithAuth();

                var setConfigUrl = DahuaUrl.UrlBuilders.BuildNetworkConfigUrl(
                    IpAddress, config.IPAddress, config.SubnetMask, config.DefaultGateway);

                var response = await _httpClient!.GetAsync(setConfigUrl, cancellationToken);
                return response.StatusCode == HttpStatusCode.OK;
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
                InitializeHttpClientWithAuth();

                // Send NTP configuration
                var ntpConfigUrl = DahuaUrl.UrlBuilders.BuildNtpConfigUrl(IpAddress, config.NTPServer);
                var ntpResponse = await _httpClient!.GetAsync(ntpConfigUrl, cancellationToken);

                if (ntpResponse.StatusCode != HttpStatusCode.OK)
                    return false;

                // Send DST configuration
                var dstConfigUrl = DahuaUrl.UrlBuilders.BuildDstConfigUrl(IpAddress);
                var dstResponse = await _httpClient.GetAsync(dstConfigUrl, cancellationToken);

                return dstResponse.StatusCode == HttpStatusCode.OK;
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
                CameraProtocol.Dahua,
                requiresAuth: true,
                isAuthenticated: authResult.IsAuthenticated,
                authMessage: authResult.Message);
        }

        private async Task<ProtocolCompatibilityResult> HandleSuccessResponse(HttpResponseMessage response)
        {
            if (await IsDahuaResponseAsync(response))
            {
                return ProtocolCompatibilityResult.CreateSuccess(
                    CameraProtocol.Dahua,
                    requiresAuth: false,
                    isAuthenticated: true);
            }

            return ProtocolCompatibilityResult.CreateFailure("Device responds but is not a Dahua device");
        }

        private static async Task<bool> IsDahuaResponseAsync(HttpResponseMessage response)
        {
            var content = await response.Content.ReadAsStringAsync();
            return !string.IsNullOrWhiteSpace(content) &&
                   DahuaIndicators.Any(indicator => content.Contains(indicator, StringComparison.OrdinalIgnoreCase));
        }

        private void InitializeHttpClient()
        {
            if (_httpClient != null) return;

            _httpClient = new HttpClient(new HttpClientHandler())
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "DahuaCompatibilityChecker/1.0");
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
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "DahuaCompatibilityChecker/1.0");
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