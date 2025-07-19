using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using wpfhikip.Models;

namespace wpfhikip.Protocols.Onvif
{
    public class OnvifConnection : IDisposable
    {
        public string IpAddress { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public AuthenticationMode AuthenticationMode { get; set; } = AuthenticationMode.WSUsernameToken;

        private HttpClient? _httpClient;
        private bool _disposed = false;
        private string? _deviceServiceUrl;

        public OnvifConnection(string ipAddress, int port, string username, string password)
        {
            IpAddress = ipAddress;
            Port = port;
            Username = username;
            Password = password;
        }

        public OnvifConnection(string ipAddress, int port, string username, string password, AuthenticationMode authMode)
        {
            IpAddress = ipAddress;
            Port = port;
            Username = username;
            Password = password;
            AuthenticationMode = authMode;
        }

        /// <summary>
        /// Checks if the camera is ONVIF compatible by attempting to access ONVIF device services
        /// </summary>
        /// <returns>
        /// CompatibilityResult containing success status, whether it's ONVIF compatible, 
        /// authentication status, and any error messages
        /// </returns>
        public async Task<CompatibilityResult> CheckCompatibilityAsync()
        {
            try
            {
                InitializeHttpClient();

                // Try to find ONVIF device service on common ports and paths
                var deviceServiceFound = await DiscoverDeviceServiceAsync();

                if (!deviceServiceFound)
                {
                    return new CompatibilityResult
                    {
                        Success = true,
                        IsOnvifCompatible = false,
                        Message = OnvifStatusMessages.DeviceNotFound
                    };
                }

                var result = new CompatibilityResult();

                // Try to get device information without authentication first
                var deviceInfoRequest = OnvifSoapTemplates.CreateGetDeviceInformationRequest();
                var response = await SendSoapRequestAsync(_deviceServiceUrl, deviceInfoRequest);

                if (response.Success)
                {
                    if (OnvifSoapTemplates.ValidateOnvifResponse(response.Content))
                    {
                        result.IsOnvifCompatible = true;
                        result.RequiresAuthentication = false;
                        result.IsAuthenticated = true;
                        result.Success = true;
                        result.Message = "ONVIF device detected - no authentication required";
                    }
                    else
                    {
                        result.IsOnvifCompatible = false;
                        result.Success = true;
                        result.Message = "Device responds but is not ONVIF compatible";
                    }
                }
                else if (response.StatusCode == HttpStatusCode.Unauthorized || OnvifSoapTemplates.IsSoapFault(response.Content))
                {
                    // Try with authentication
                    result.IsOnvifCompatible = true;
                    result.RequiresAuthentication = true;
                    result.Success = true;
                    result.Message = "ONVIF device detected - authentication required";

                    var authResult = await TestAuthenticationAsync();
                    result.IsAuthenticated = authResult.IsAuthenticated;
                    result.AuthenticationMessage = authResult.Message;
                }
                else
                {
                    result.IsOnvifCompatible = false;
                    result.Success = false;
                    result.Message = $"Unexpected response: {response.StatusCode}";
                }

                return result;
            }
            catch (HttpRequestException ex)
            {
                return new CompatibilityResult
                {
                    Success = false,
                    IsOnvifCompatible = false,
                    Message = $"Network error: {ex.Message}"
                };
            }
            catch (TaskCanceledException ex)
            {
                return new CompatibilityResult
                {
                    Success = false,
                    IsOnvifCompatible = false,
                    Message = ex.InnerException is TimeoutException ? "Connection timeout" : "Request cancelled"
                };
            }
            catch (Exception ex)
            {
                return new CompatibilityResult
                {
                    Success = false,
                    IsOnvifCompatible = false,
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
                if (string.IsNullOrEmpty(_deviceServiceUrl))
                {
                    var found = await DiscoverDeviceServiceAsync();
                    if (!found)
                    {
                        return new AuthenticationResult
                        {
                            IsAuthenticated = false,
                            Success = false,
                            Message = "Device service not found"
                        };
                    }
                }

                var deviceInfoRequest = OnvifSoapTemplates.CreateGetDeviceInformationRequest(Username, Password);
                var response = await SendSoapRequestAsync(_deviceServiceUrl, deviceInfoRequest);

                if (response.Success && OnvifSoapTemplates.ValidateOnvifResponse(response.Content))
                {
                    return new AuthenticationResult
                    {
                        IsAuthenticated = true,
                        Success = true,
                        Message = "Authentication successful"
                    };
                }
                else if (OnvifSoapTemplates.IsSoapFault(response.Content))
                {
                    return new AuthenticationResult
                    {
                        IsAuthenticated = false,
                        Success = true,
                        Message = "Authentication failed - invalid credentials"
                    };
                }
                else
                {
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
        /// Sends network configuration to the ONVIF device
        /// </summary>
        /// <param name="config">Network configuration to apply</param>
        /// <returns>Operation result</returns>
        public async Task<OnvifOperationResult> SendNetworkConfigurationAsync(NetworkConfiguration config)
        {
            try
            {
                if (string.IsNullOrEmpty(_deviceServiceUrl))
                {
                    var found = await DiscoverDeviceServiceAsync();
                    if (!found)
                    {
                        return new OnvifOperationResult
                        {
                            Success = false,
                            Message = "Device service not found"
                        };
                    }
                }

                // First, get current network interfaces to get the interface token
                var getNetworkRequest = OnvifSoapTemplates.CreateGetNetworkInterfacesRequest(Username, Password);
                var getResponse = await SendSoapRequestAsync(_deviceServiceUrl, getNetworkRequest);

                if (!getResponse.Success)
                {
                    return new OnvifOperationResult
                    {
                        Success = false,
                        Message = "Failed to retrieve current network configuration"
                    };
                }

                var interfaceToken = OnvifSoapTemplates.ExtractNetworkInterfaceToken(getResponse.Content);

                // Now set the new network configuration
                var setNetworkRequest = OnvifSoapTemplates.CreateSetNetworkInterfacesRequest(config, interfaceToken, Username, Password);
                var setResponse = await SendSoapRequestAsync(_deviceServiceUrl, setNetworkRequest);

                if (setResponse.Success && !OnvifSoapTemplates.IsSoapFault(setResponse.Content))
                {
                    return new OnvifOperationResult
                    {
                        Success = true,
                        Message = OnvifStatusMessages.NetworkSettingsSent
                    };
                }
                else
                {
                    return new OnvifOperationResult
                    {
                        Success = false,
                        Message = OnvifSoapTemplates.IsSoapFault(setResponse.Content)
                            ? OnvifStatusMessages.SoapFault
                            : $"Failed to send network configuration: {setResponse.StatusCode}"
                    };
                }
            }
            catch (Exception ex)
            {
                return new OnvifOperationResult
                {
                    Success = false,
                    Message = $"Error sending network configuration: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Sends NTP configuration to the ONVIF device
        /// </summary>
        /// <param name="config">Configuration containing NTP settings</param>
        /// <returns>Operation result</returns>
        public async Task<OnvifOperationResult> SendNtpConfigurationAsync(NetworkConfiguration config)
        {
            try
            {
                if (string.IsNullOrEmpty(_deviceServiceUrl))
                {
                    var found = await DiscoverDeviceServiceAsync();
                    if (!found)
                    {
                        return new OnvifOperationResult
                        {
                            Success = false,
                            Message = "Device service not found"
                        };
                    }
                }

                var setNtpRequest = OnvifSoapTemplates.CreateSetNtpRequest(config, Username, Password);
                var response = await SendSoapRequestAsync(_deviceServiceUrl, setNtpRequest);

                if (response.Success && !OnvifSoapTemplates.IsSoapFault(response.Content))
                {
                    return new OnvifOperationResult
                    {
                        Success = true,
                        Message = OnvifStatusMessages.NtpServerSent
                    };
                }
                else
                {
                    return new OnvifOperationResult
                    {
                        Success = false,
                        Message = OnvifSoapTemplates.IsSoapFault(response.Content)
                            ? OnvifStatusMessages.SoapFault
                            : OnvifStatusMessages.NtpServerError
                    };
                }
            }
            catch (Exception ex)
            {
                return new OnvifOperationResult
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
                return result.IsOnvifCompatible;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Discovers ONVIF device service URL by trying common endpoints and ports
        /// </summary>
        private async Task<bool> DiscoverDeviceServiceAsync()
        {
            var ports = OnvifUrl.UrlBuilders.GetCommonOnvifPorts();

            foreach (var port in ports)
            {
                var urls = OnvifUrl.UrlBuilders.GetPossibleDeviceServiceUrls(IpAddress, port);

                foreach (var url in urls)
                {
                    try
                    {
                        var testRequest = OnvifSoapTemplates.CreateGetDeviceInformationRequest();
                        var response = await SendSoapRequestAsync(url, testRequest);

                        if (response.Success || OnvifSoapTemplates.IsSoapFault(response.Content))
                        {
                            _deviceServiceUrl = url;
                            Port = port; // Update the port if we found it on a different one
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

        /// <summary>
        /// Sends SOAP request to the specified URL
        /// </summary>
        private async Task<(bool Success, string Content, HttpStatusCode StatusCode)> SendSoapRequestAsync(string url, string soapRequest)
        {
            try
            {
                var content = new StringContent(soapRequest, Encoding.UTF8, OnvifContentTypes.Soap);
                var response = await _httpClient.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                return (response.IsSuccessStatusCode, responseContent, response.StatusCode);
            }
            catch (Exception)
            {
                return (false, string.Empty, HttpStatusCode.InternalServerError);
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

            // Add common headers for ONVIF
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "OnvifCompatibilityChecker/1.0");
        }

        private string BuildBaseUrl()
        {
            var portSuffix = Port != 80 && Port != 443 ? $":{Port}" : "";
            var protocol = Port == 443 ? "https" : "http";
            return $"{protocol}://{IpAddress}{portSuffix}";
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
    /// Result of ONVIF operation
    /// </summary>
    public class OnvifOperationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public Dictionary<string, string>? Data { get; set; }
    }

    /// <summary>
    /// Extended compatibility result for ONVIF devices
    /// </summary>
    public class CompatibilityResult
    {
        public bool Success { get; set; }
        public bool IsOnvifCompatible { get; set; }
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
