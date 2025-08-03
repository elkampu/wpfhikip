using System.Net;
using System.Net.Http;
using System.Text;

using wpfhikip.Models;
using wpfhikip.Protocols.Common;

namespace wpfhikip.Protocols.Onvif
{
    public sealed class OnvifConnection : IProtocolConnection
    {
        private const int HttpTimeoutSeconds = 15;

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

                // Try without authentication first (many ONVIF devices allow GetDeviceInformation without auth)
                var deviceInfoRequest = OnvifSoapTemplates.CreateGetDeviceInformationRequest();
                var response = await SendSoapRequestAsync(_deviceServiceUrl!, deviceInfoRequest, OnvifUrl.SoapActions.GetDeviceInformation);

                if (response.Success && OnvifSoapTemplates.ValidateOnvifResponse(response.Content))
                {
                    return ProtocolCompatibilityResult.CreateSuccess(
                        CameraProtocol.Onvif,
                        requiresAuth: false,
                        isAuthenticated: true);
                }

                // Check if authentication is required
                if (response.StatusCode == HttpStatusCode.Unauthorized || OnvifSoapTemplates.IsSoapFault(response.Content))
                {
                    if (!string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password))
                    {
                        var authResult = await TestAuthenticationAsync();
                        return ProtocolCompatibilityResult.CreateSuccess(
                            CameraProtocol.Onvif,
                            requiresAuth: true,
                            isAuthenticated: authResult.Success,
                            authMessage: authResult.Message);
                    }
                    else
                    {
                        return ProtocolCompatibilityResult.CreateSuccess(
                            CameraProtocol.Onvif,
                            requiresAuth: true,
                            isAuthenticated: false,
                            authMessage: "Credentials required for authentication");
                    }
                }

                // Try GetSystemDateAndTime as a fallback (usually always available without auth)
                var dateTimeRequest = OnvifSoapTemplates.CreateGetSystemDateAndTimeRequest();
                var dateTimeResponse = await SendSoapRequestAsync(_deviceServiceUrl!, dateTimeRequest, OnvifUrl.SoapActions.GetSystemDateAndTime);

                if (dateTimeResponse.Success && OnvifSoapTemplates.ValidateOnvifResponse(dateTimeResponse.Content))
                {
                    return ProtocolCompatibilityResult.CreateSuccess(
                        CameraProtocol.Onvif,
                        requiresAuth: false,
                        isAuthenticated: true);
                }

                return ProtocolCompatibilityResult.CreateFailure($"Device responds but is not ONVIF compatible. Status: {response.StatusCode}");
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
                var response = await SendSoapRequestAsync(_deviceServiceUrl!, deviceInfoRequest, OnvifUrl.SoapActions.GetDeviceInformation);

                if (response.Success && OnvifSoapTemplates.ValidateOnvifResponse(response.Content))
                {
                    return AuthenticationResult.CreateSuccess("Authentication successful");
                }

                if (OnvifSoapTemplates.IsSoapFault(response.Content))
                {
                    var faultString = OnvifSoapTemplates.ExtractSoapFaultString(response.Content);
                    return AuthenticationResult.CreateFailure($"Authentication failed: {faultString}");
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
                var getResponse = await SendSoapRequestAsync(_deviceServiceUrl!, getNetworkRequest, OnvifUrl.SoapActions.GetNetworkInterfaces);

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
                var setResponse = await SendSoapRequestAsync(_deviceServiceUrl!, setNetworkRequest, OnvifUrl.SoapActions.SetNetworkInterfaces);

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
                var response = await SendSoapRequestAsync(_deviceServiceUrl!, setNtpRequest, OnvifUrl.SoapActions.SetNTP);

                return response.Success && !OnvifSoapTemplates.IsSoapFault(response.Content);
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> DiscoverDeviceServiceAsync()
        {
            // Try common ONVIF ports
            var portsToTry = Port != 80 ? new[] { Port, 80, 8080, 8000, 554, 8554 } : new[] { 80, 8080, 8000 };

            foreach (var port in portsToTry)
            {
                var urls = OnvifUrl.UrlBuilders.GetPossibleDeviceServiceUrls(IpAddress, port);

                foreach (var url in urls)
                {
                    try
                    {
                        // ONVIF requires POST requests - try with GetSystemDateAndTime (most basic ONVIF call)
                        var testRequest = OnvifSoapTemplates.CreateGetSystemDateAndTimeRequest();
                        var soapResponse = await SendSoapRequestAsync(url, testRequest, OnvifUrl.SoapActions.GetSystemDateAndTime).ConfigureAwait(false);

                        // Consider it found if we get any ONVIF-like response
                        if (soapResponse.Success ||
                            soapResponse.StatusCode == HttpStatusCode.Unauthorized ||
                            OnvifSoapTemplates.IsSoapFault(soapResponse.Content) ||
                            OnvifSoapTemplates.ValidateOnvifResponse(soapResponse.Content))
                        {
                            _deviceServiceUrl = url;
                            return true;
                        }
                    }
                    catch
                    {
                        // Continue trying other URLs
                    }
                }
            }

            return false;
        }

        private async Task<(bool Success, string Content, HttpStatusCode StatusCode)> SendSoapRequestAsync(string url, string soapRequest, string soapAction = "")
        {
            try
            {
                // Ensure HTTP client is initialized
                InitializeHttpClient();

                // Use proper ONVIF content type
                var content = new StringContent(soapRequest, Encoding.UTF8, "text/xml");
                content.Headers.Remove("Content-Type");
                content.Headers.Add("Content-Type", "text/xml; charset=utf-8");

                // Add required SOAP action header
                if (!string.IsNullOrEmpty(soapAction))
                {
                    content.Headers.Add("SOAPAction", $"\"{soapAction}\"");
                }

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

            var handler = new HttpClientHandler()
            {
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds)
            };

            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ONVIFClient/1.0");
            _httpClient.DefaultRequestHeaders.Add("Connection", "keep-alive");
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