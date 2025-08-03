using System.Net;
using System.Net.Http;
using System.Text;
using System.Xml.Linq;
using System.Diagnostics;

using wpfhikip.Models;
using wpfhikip.Protocols.Common;

namespace wpfhikip.Protocols.Onvif
{
    public sealed class OnvifConnection : IProtocolConnection
    {
        private const int HttpTimeoutSeconds = 15;
        private static readonly ActivitySource ActivitySource = new("OnvifConnection");

        private HttpClient? _httpClient;
        private bool _disposed;
        private string? _deviceServiceUrl;
        private string? _mediaServiceUrl;

        // Add debug logging flag
        private bool _enableDetailedLogging = true; // Can be made configurable

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

            LogDebug("Connection", $"Created ONVIF connection for {ipAddress}:{port} with user '{username}'");
        }

        public OnvifConnection(string ipAddress, int port, string username, string password, AuthenticationMode authMode)
            : this(ipAddress, port, username, password)
        {
            AuthenticationMode = authMode;
        }

        public async Task<ProtocolCompatibilityResult> CheckCompatibilityAsync(CancellationToken cancellationToken = default)
        {
            using var activity = ActivitySource.StartActivity("CheckCompatibility");
            activity?.SetTag("ip.address", IpAddress);
            activity?.SetTag("port", Port);

            try
            {
                LogDebug("Compatibility", "Starting ONVIF compatibility check");

                InitializeHttpClient();
                LogDebug("Compatibility", "HTTP client initialized");

                var deviceServiceFound = await DiscoverDeviceServiceAsync();
                if (!deviceServiceFound)
                {
                    LogDebug("Compatibility", "Device service discovery failed");
                    return ProtocolCompatibilityResult.CreateFailure("ONVIF device service not found");
                }

                LogDebug("Compatibility", $"Device service discovered at: {_deviceServiceUrl}");

                // Try without authentication first
                var deviceInfoRequest = OnvifSoapTemplates.CreateGetDeviceInformationRequest();
                LogDebug("SOAP", $"Sending unauthenticated GetDeviceInformation request");

                if (_enableDetailedLogging)
                {
                    LogSoapRequest("GetDeviceInformation (No Auth)", deviceInfoRequest);
                }

                var response = await SendSoapRequestAsync(_deviceServiceUrl!, deviceInfoRequest, OnvifUrl.SoapActions.GetDeviceInformation);

                if (_enableDetailedLogging)
                {
                    LogSoapResponse("GetDeviceInformation (No Auth)", response);
                }

                // Check if the response is successful and valid
                if (response.Success && OnvifSoapTemplates.ValidateOnvifResponse(response.Content) && !OnvifSoapTemplates.IsSoapFault(response.Content))
                {
                    LogDebug("Compatibility", "Device responds without authentication - ONVIF compatible");
                    return ProtocolCompatibilityResult.CreateSuccess(
                        CameraProtocol.Onvif,
                        requiresAuth: false,
                        isAuthenticated: true);
                }

                LogDebug("Compatibility", "Authentication required - testing with credentials");

                // Authentication is required
                if (!string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password))
                {
                    var authResult = await TestAuthenticationAsync();
                    LogDebug("Compatibility", $"Authentication test result: Success={authResult.Success}, IsAuthenticated={authResult.IsAuthenticated} - {authResult.Message}");

                    return ProtocolCompatibilityResult.CreateSuccess(
                        CameraProtocol.Onvif,
                        requiresAuth: true,
                        isAuthenticated: authResult.IsAuthenticated, // THIS WAS THE BUG - was using authResult.Success instead
                        authMessage: authResult.Message);
                }
                else
                {
                    LogDebug("Compatibility", "No credentials provided for authentication test");
                    return ProtocolCompatibilityResult.CreateSuccess(
                        CameraProtocol.Onvif,
                        requiresAuth: true,
                        isAuthenticated: false,
                        authMessage: "Credentials required for authentication");
                }
            }
            catch (Exception ex)
            {
                LogError("Compatibility", $"ONVIF compatibility check failed: {ex}");
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                return ProtocolCompatibilityResult.CreateFailure($"ONVIF compatibility check failed: {ex.Message}");
            }
        }

        public async Task<AuthenticationResult> TestAuthenticationAsync(CancellationToken cancellationToken = default)
        {
            using var activity = ActivitySource.StartActivity("TestAuthentication");

            try
            {
                LogDebug("Authentication", "Starting authentication test");

                if (string.IsNullOrEmpty(_deviceServiceUrl))
                {
                    var discovered = await DiscoverDeviceServiceAsync();
                    if (!discovered)
                    {
                        LogDebug("Authentication", "Device service not found during auth test");
                        return AuthenticationResult.CreateFailure("Device service not found");
                    }
                }

                // Test authentication with credentials
                var deviceInfoRequest = OnvifSoapTemplates.CreateGetDeviceInformationRequest(Username, Password);
                LogDebug("Authentication", "Sending authenticated GetDeviceInformation request");

                if (_enableDetailedLogging)
                {
                    LogSoapRequest("GetDeviceInformation (Auth)", deviceInfoRequest);
                }

                var response = await SendSoapRequestAsync(_deviceServiceUrl!, deviceInfoRequest, OnvifUrl.SoapActions.GetDeviceInformation);

                if (_enableDetailedLogging)
                {
                    LogSoapResponse("GetDeviceInformation (Auth)", response);
                }

                // Check for explicit authentication failure
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    LogDebug("Authentication", "HTTP 401 - Authentication rejected");
                    return AuthenticationResult.CreateFailure("HTTP 401: Authentication credentials rejected");
                }

                // Check for SOAP faults FIRST - this is the primary indicator of auth failure
                if (OnvifSoapTemplates.IsSoapFault(response.Content))
                {
                    var faultString = OnvifSoapTemplates.ExtractSoapFaultString(response.Content);
                    LogDebug("Authentication", $"SOAP fault received: {faultString}");

                    // Check if this is an authentication-related fault
                    if (IsAuthenticationFault(faultString))
                    {
                        LogDebug("Authentication", "SOAP fault indicates authentication failure");
                        return AuthenticationResult.CreateFailure($"Authentication failed: {faultString}");
                    }

                    // Even non-auth faults should be considered failures for ONVIF
                    // because ONVIF typically returns faults when auth fails
                    LogDebug("Authentication", "SOAP fault indicates request failure - treating as auth failure");
                    return AuthenticationResult.CreateFailure($"SOAP fault: {faultString}");
                }

                // Only if NO SOAP fault, check if response is valid and contains device data
                if (response.Success && OnvifSoapTemplates.ValidateOnvifResponse(response.Content))
                {
                    if (ContainsValidDeviceInfo(response.Content))
                    {
                        LogDebug("Authentication", "Authentication successful - valid device information received");
                        return AuthenticationResult.CreateSuccess("ONVIF authentication successful");
                    }
                    else
                    {
                        LogDebug("Authentication", "Response valid but no device information - authentication failed");
                        return AuthenticationResult.CreateFailure("Authentication failed - no valid device information returned");
                    }
                }

                LogDebug("Authentication", $"Authentication failed with status: {response.StatusCode}");
                return AuthenticationResult.CreateFailure($"Authentication failed with status: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                LogError("Authentication", $"Authentication test error: {ex}");
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                return AuthenticationResult.CreateFailure($"Authentication error: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends SOAP request to device service
        /// </summary>
        public async Task<(bool Success, string Content, HttpStatusCode StatusCode)> SendSoapToDeviceServiceAsync(string soapRequest, string soapAction)
        {
            if (string.IsNullOrEmpty(_deviceServiceUrl))
            {
                var discovered = await DiscoverDeviceServiceAsync();
                if (!discovered)
                {
                    return (false, string.Empty, HttpStatusCode.ServiceUnavailable);
                }
            }

            return await SendSoapRequestAsync(_deviceServiceUrl!, soapRequest, soapAction);
        }

        /// <summary>
        /// Sends SOAP request to media service
        /// </summary>
        public async Task<(bool Success, string Content, HttpStatusCode StatusCode)> SendSoapToMediaServiceAsync(string soapRequest, string soapAction)
        {
            if (string.IsNullOrEmpty(_mediaServiceUrl))
            {
                await DiscoverMediaServiceAsync();
            }

            if (string.IsNullOrEmpty(_mediaServiceUrl))
            {
                return (false, string.Empty, HttpStatusCode.ServiceUnavailable);
            }

            return await SendSoapRequestAsync(_mediaServiceUrl, soapRequest, soapAction);
        }

        /// <summary>
        /// Gets the Media Service URL, discovering it if necessary
        /// </summary>
        public async Task<string?> GetMediaServiceUrlAsync()
        {
            if (string.IsNullOrEmpty(_mediaServiceUrl))
            {
                await DiscoverMediaServiceAsync();
            }
            return _mediaServiceUrl;
        }

        public async Task<bool> SendNetworkConfigAsync(NetworkConfiguration config, CancellationToken cancellationToken = default)
        {
            // Implementation for network configuration
            return false; // Placeholder
        }

        public async Task<bool> SendNTPConfigAsync(NTPConfiguration config, CancellationToken cancellationToken = default)
        {
            // Implementation for NTP configuration
            return false; // Placeholder
        }

        private async Task<bool> DiscoverDeviceServiceAsync()
        {
            using var activity = ActivitySource.StartActivity("DiscoverDeviceService");

            var possibleUrls = OnvifUrl.UrlBuilders.GetPossibleDeviceServiceUrls(IpAddress, Port);
            LogDebug("Discovery", $"Testing {possibleUrls.Length} possible device service URLs");

            for (int i = 0; i < possibleUrls.Length; i++)
            {
                var url = possibleUrls[i];
                LogDebug("Discovery", $"Testing URL {i + 1}/{possibleUrls.Length}: {url}");

                try
                {
                    var testRequest = OnvifSoapTemplates.CreateGetSystemDateAndTimeRequest();
                    var response = await SendSoapRequestAsync(url, testRequest, OnvifUrl.SoapActions.GetSystemDateAndTime);

                    LogDebug("Discovery", $"URL {url} responded with status: {response.StatusCode}, Success: {response.Success}");

                    if (response.Success || response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        _deviceServiceUrl = url;
                        LogDebug("Discovery", $"Device service found at: {url}");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    LogDebug("Discovery", $"URL {url} failed: {ex.Message}");
                }
            }

            LogDebug("Discovery", "No device service URL found");
            return false;
        }

        private async Task<bool> DiscoverMediaServiceAsync()
        {
            // First try to get media service URL from capabilities
            try
            {
                if (!string.IsNullOrEmpty(_deviceServiceUrl))
                {
                    var capabilitiesRequest = OnvifSoapTemplates.CreateGetCapabilitiesRequest(Username, Password);
                    var response = await SendSoapRequestAsync(_deviceServiceUrl, capabilitiesRequest, OnvifUrl.SoapActions.GetCapabilities);

                    if (response.Success && !OnvifSoapTemplates.IsSoapFault(response.Content))
                    {
                        var mediaUrl = ExtractMediaServiceUrlFromCapabilities(response.Content);
                        if (!string.IsNullOrEmpty(mediaUrl))
                        {
                            _mediaServiceUrl = mediaUrl;
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // Fall back to trying standard URLs
            }

            // Fall back to trying possible media service URLs
            var possibleUrls = OnvifUrl.UrlBuilders.GetPossibleMediaServiceUrls(IpAddress, Port);

            foreach (var url in possibleUrls)
            {
                try
                {
                    var testRequest = OnvifSoapTemplates.CreateGetProfilesRequest(Username, Password);
                    var response = await SendSoapRequestAsync(url, testRequest, OnvifUrl.SoapActions.GetProfiles);

                    if (response.Success || response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        _mediaServiceUrl = url;
                        return true;
                    }
                }
                catch
                {
                    // Try next URL
                }
            }

            return false;
        }

        private string? ExtractMediaServiceUrlFromCapabilities(string capabilitiesResponse)
        {
            try
            {
                var doc = XDocument.Parse(capabilitiesResponse);

                // Look for Media service XAddr
                var mediaElement = doc.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "Media");

                if (mediaElement != null)
                {
                    var xAddrElement = mediaElement.Descendants()
                        .FirstOrDefault(e => e.Name.LocalName == "XAddr");

                    return xAddrElement?.Value;
                }
            }
            catch
            {
                // Ignore parsing errors
            }

            return null;
        }

        private async Task<(bool Success, string Content, HttpStatusCode StatusCode)> SendSoapRequestAsync(string url, string soapRequest, string soapAction = "")
        {
            using var activity = ActivitySource.StartActivity("SendSoapRequest");
            activity?.SetTag("url", url);
            activity?.SetTag("action", soapAction);

            try
            {
                InitializeHttpClient();

                using var content = new StringContent(soapRequest, Encoding.UTF8, "application/soap+xml");

                if (!string.IsNullOrEmpty(soapAction))
                {
                    content.Headers.Add("SOAPAction", soapAction);
                }

                LogDebug("HTTP", $"POST {url} with action: {soapAction}");

                var stopwatch = Stopwatch.StartNew();
                var response = await _httpClient!.PostAsync(url, content);
                stopwatch.Stop();

                var responseContent = await response.Content.ReadAsStringAsync();

                LogDebug("HTTP", $"Response received in {stopwatch.ElapsedMilliseconds}ms - Status: {response.StatusCode}, Length: {responseContent.Length}");

                if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Unauthorized)
                {
                    LogDebug("HTTP", $"Non-success status code: {response.StatusCode}");
                }

                return (response.IsSuccessStatusCode, responseContent, response.StatusCode);
            }
            catch (Exception ex)
            {
                LogError("HTTP", $"Request to {url} failed: {ex}");
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                return (false, string.Empty, HttpStatusCode.InternalServerError);
            }
        }

        private void InitializeHttpClient()
        {
            if (_httpClient == null)
            {
                var handler = new HttpClientHandler()
                {
                    ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) => true
                };

                _httpClient = new HttpClient(handler)
                {
                    Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds)
                };

                _httpClient.DefaultRequestHeaders.Add("User-Agent", "ONVIF Client/1.0");
            }
        }

        /// <summary>
        /// Checks if a SOAP fault string indicates an authentication-related error
        /// </summary>
        private static bool IsAuthenticationFault(string faultString)
        {
            if (string.IsNullOrEmpty(faultString))
                return false;

            var authFaultIndicators = new[]
            {
                "NotAuthorized",
                "InvalidCredentials",
                "Unauthorized",
                "Authentication",
                "CredentialsNotValid",
                "InvalidUserName",
                "InvalidPassword",
                "AccessDenied",
                "Forbidden",
                "ter:NotAuthorized",
                "ter:InvalidCredentials",
                "sender:NotAuthorized",
                "sender:InvalidCredentials",
                "locked", // For the "device is locked" message
                "wrong username/password" // For explicit credential errors
            };

            return authFaultIndicators.Any(indicator =>
                faultString.Contains(indicator, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Checks if the response contains valid device information indicating successful authentication
        /// </summary>
        private static bool ContainsValidDeviceInfo(string responseContent)
        {
            try
            {
                var doc = XDocument.Parse(responseContent);

                // Look for GetDeviceInformationResponse with actual content
                var deviceInfoResponse = doc.Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "GetDeviceInformationResponse");

                if (deviceInfoResponse == null)
                    return false;

                // Check for common device info fields that should be present in a successful response
                var hasManufacturer = deviceInfoResponse.Descendants()
                    .Any(e => e.Name.LocalName == "Manufacturer" && !string.IsNullOrWhiteSpace(e.Value));
                var hasModel = deviceInfoResponse.Descendants()
                    .Any(e => e.Name.LocalName == "Model" && !string.IsNullOrWhiteSpace(e.Value));
                var hasSerial = deviceInfoResponse.Descendants()
                    .Any(e => e.Name.LocalName == "SerialNumber");

                // If we have at least manufacturer or model, consider it valid
                return hasManufacturer || hasModel || hasSerial;
            }
            catch
            {
                return false;
            }
        }

        // Logging methods
        private void LogDebug(string category, string message)
        {
            if (_enableDetailedLogging)
            {
                Debug.WriteLine($"[ONVIF-{category}] {IpAddress}:{Port} - {message}");
            }
        }

        private void LogError(string category, string message)
        {
            Debug.WriteLine($"[ONVIF-ERROR-{category}] {IpAddress}:{Port} - {message}");
        }

        private void LogSoapRequest(string operation, string soapContent)
        {
            if (_enableDetailedLogging)
            {
                Debug.WriteLine($"[ONVIF-SOAP-REQUEST] {IpAddress}:{Port} - {operation}");
                Debug.WriteLine($"Request XML:\n{FormatXml(soapContent)}");
            }
        }

        private void LogSoapResponse(string operation, (bool Success, string Content, HttpStatusCode StatusCode) response)
        {
            if (_enableDetailedLogging)
            {
                Debug.WriteLine($"[ONVIF-SOAP-RESPONSE] {IpAddress}:{Port} - {operation}");
                Debug.WriteLine($"Status: {response.StatusCode}, Success: {response.Success}, Length: {response.Content.Length}");
                if (!string.IsNullOrEmpty(response.Content))
                {
                    Debug.WriteLine($"Response XML:\n{FormatXml(response.Content)}");
                }
            }
        }

        private static string FormatXml(string xml)
        {
            try
            {
                var doc = XDocument.Parse(xml);
                return doc.ToString();
            }
            catch
            {
                return xml; // Return original if parsing fails
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                LogDebug("Lifecycle", "Disposing ONVIF connection");
                _httpClient?.Dispose();
                ActivitySource.Dispose();
                _disposed = true;
            }
        }
    }
}