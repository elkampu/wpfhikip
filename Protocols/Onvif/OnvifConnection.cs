using System.Net;
using System.Net.Http;
using System.Text;

using wpfhikip.Models;
using wpfhikip.Protocols.Common;

namespace wpfhikip.Protocols.Onvif
{
    public sealed class OnvifConnection : IProtocolConnection
    {
        private const int HttpTimeoutSeconds = 5;

        private HttpClient? _httpClient;
        private bool _disposed;
        private string? _deviceServiceUrl;

        public string IpAddress { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public AuthenticationMode AuthenticationMode { get; set; } = AuthenticationMode.WSUsernameToken;
        public CameraProtocol ProtocolType => CameraProtocol.Onvif;

        public OnvifConnection(string ipAddress, int port, string username, string password)
        {
            IpAddress = ipAddress;
            Port = port;
            Username = username;
            Password = password;
        }

        public OnvifConnection(string ipAddress, int port, string username, string password, AuthenticationMode authMode)
            : this(ipAddress, port, username, password)
        {
            AuthenticationMode = authMode;
        }

        public async Task<ProtocolCompatibilityResult> CheckCompatibilityAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                InitializeHttpClient();

                var deviceServiceFound = await DiscoverDeviceServiceAsync();
                if (!deviceServiceFound)
                {
                    return ProtocolCompatibilityResult.CreateFailure("ONVIF device service not found");
                }

                var deviceInfoRequest = OnvifSoapTemplates.CreateGetDeviceInformationRequest();
                var response = await SendSoapRequestAsync(_deviceServiceUrl!, deviceInfoRequest);

                if (response.Success)
                {
                    if (OnvifSoapTemplates.ValidateOnvifResponse(response.Content))
                    {
                        return ProtocolCompatibilityResult.CreateSuccess(
                            CameraProtocol.Onvif,
                            requiresAuth: false,
                            isAuthenticated: true);
                    }

                    return ProtocolCompatibilityResult.CreateFailure("Device responds but is not ONVIF compatible");
                }

                if (response.StatusCode == HttpStatusCode.Unauthorized || OnvifSoapTemplates.IsSoapFault(response.Content))
                {
                    var authResult = await TestAuthenticationAsync();
                    return ProtocolCompatibilityResult.CreateSuccess(
                        CameraProtocol.Onvif,
                        requiresAuth: true,
                        isAuthenticated: authResult.IsAuthenticated,
                        authMessage: authResult.Message);
                }

                return ProtocolCompatibilityResult.CreateFailure($"Unexpected response: {response.StatusCode}");
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
                if (string.IsNullOrEmpty(_deviceServiceUrl))
                {
                    var found = await DiscoverDeviceServiceAsync();
                    if (!found)
                    {
                        return AuthenticationResult.CreateError("Device service not found");
                    }
                }

                var deviceInfoRequest = OnvifSoapTemplates.CreateGetDeviceInformationRequest(Username, Password);
                var response = await SendSoapRequestAsync(_deviceServiceUrl!, deviceInfoRequest);

                if (response.Success && OnvifSoapTemplates.ValidateOnvifResponse(response.Content))
                {
                    return AuthenticationResult.CreateSuccess();
                }

                if (OnvifSoapTemplates.IsSoapFault(response.Content))
                {
                    return AuthenticationResult.CreateFailure("Authentication failed - invalid credentials");
                }

                return AuthenticationResult.CreateError($"Unexpected response during authentication: {response.StatusCode}");
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
                if (string.IsNullOrEmpty(_deviceServiceUrl))
                {
                    var found = await DiscoverDeviceServiceAsync();
                    if (!found) return false;
                }

                // Get current network interfaces
                var getNetworkRequest = OnvifSoapTemplates.CreateGetNetworkInterfacesRequest(Username, Password);
                var getResponse = await SendSoapRequestAsync(_deviceServiceUrl!, getNetworkRequest);

                if (!getResponse.Success) return false;

                var interfaceToken = OnvifSoapTemplates.ExtractNetworkInterfaceToken(getResponse.Content);

                // Create temporary Camera object for existing API compatibility
                var tempCamera = new Camera
                {
                    NewIP = config.IPAddress,
                    NewMask = config.SubnetMask,
                    NewGateway = config.DefaultGateway
                };

                var setNetworkRequest = OnvifSoapTemplates.CreateSetNetworkInterfacesRequest(tempCamera, interfaceToken, Username, Password);
                var setResponse = await SendSoapRequestAsync(_deviceServiceUrl!, setNetworkRequest);

                return setResponse.Success && !OnvifSoapTemplates.IsSoapFault(setResponse.Content);
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
                if (string.IsNullOrEmpty(_deviceServiceUrl))
                {
                    var found = await DiscoverDeviceServiceAsync();
                    if (!found) return false;
                }

                // Create temporary Camera object for existing API compatibility
                var tempCamera = new Camera
                {
                    NewNTPServer = config.NTPServer
                };

                var setNtpRequest = OnvifSoapTemplates.CreateSetNtpRequest(tempCamera, Username, Password);
                var response = await SendSoapRequestAsync(_deviceServiceUrl!, setNtpRequest);

                return response.Success && !OnvifSoapTemplates.IsSoapFault(response.Content);
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> DiscoverDeviceServiceAsync()
        {
            // Use only the user-defined port instead of scanning all common ports
            var urls = OnvifUrl.UrlBuilders.GetPossibleDeviceServiceUrls(IpAddress, Port);

            foreach (var url in urls)
            {
                try
                {
                    var testRequest = OnvifSoapTemplates.CreateGetDeviceInformationRequest();
                    var response = await SendSoapRequestAsync(url, testRequest).ConfigureAwait(false);

                    if (response.Success || OnvifSoapTemplates.IsSoapFault(response.Content))
                    {
                        _deviceServiceUrl = url;
                        return true;
                    }
                }
                catch
                {
                    // Continue trying other URLs on the same port
                }
            }

            return false;
        }

        private async Task<(bool Success, string Content, HttpStatusCode StatusCode)> SendSoapRequestAsync(string url, string soapRequest)
        {
            try
            {
                var content = new StringContent(soapRequest, Encoding.UTF8, OnvifContentTypes.Soap);
                var response = await _httpClient!.PostAsync(url, content).ConfigureAwait(false);
                var responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                return (response.IsSuccessStatusCode, responseContent, response.StatusCode);
            }
            catch (Exception)
            {
                return (false, string.Empty, HttpStatusCode.InternalServerError);
            }
        }

        private void InitializeHttpClient()
        {
            if (_httpClient != null) return;

            _httpClient = new HttpClient(new HttpClientHandler())
            {
                Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds)
            };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "OnvifCompatibilityChecker/1.0");
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