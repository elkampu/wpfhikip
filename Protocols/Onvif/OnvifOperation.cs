using wpfhikip.Models;

namespace wpfhikip.Protocols.Onvif
{
    /// <summary>
    /// ONVIF operation management for camera control
    /// </summary>
    public sealed class OnvifOperation : IDisposable
    {
        private readonly OnvifConnection _connection;
        private bool _disposed;

        public OnvifOperation(OnvifConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        /// <summary>
        /// Gets camera status information
        /// </summary>
        public async Task<(bool Success, Dictionary<string, object> Status, string ErrorMessage)> GetCameraStatusAsync()
        {
            try
            {
                var status = new Dictionary<string, object>();

                // Get device information for status
                var deviceInfoRequest = OnvifSoapTemplates.CreateGetDeviceInformationRequest(_connection.Username, _connection.Password);
                var response = await SendSoapRequestAsync(deviceInfoRequest, OnvifUrl.SoapActions.GetDeviceInformation);

                if (response.Success && !OnvifSoapTemplates.IsSoapFault(response.Content))
                {
                    var deviceInfo = OnvifSoapTemplates.ParseSoapResponse(response.Content);
                    foreach (var info in deviceInfo)
                    {
                        status[info.Key] = info.Value;
                    }
                }

                // Get system date and time
                var dateTimeRequest = OnvifSoapTemplates.CreateGetSystemDateAndTimeRequest(_connection.Username, _connection.Password);
                var dateTimeResponse = await SendSoapRequestAsync(dateTimeRequest, OnvifUrl.SoapActions.GetSystemDateAndTime);

                if (dateTimeResponse.Success && !OnvifSoapTemplates.IsSoapFault(dateTimeResponse.Content))
                {
                    var dateTimeInfo = OnvifSoapTemplates.ParseSoapResponse(dateTimeResponse.Content);
                    foreach (var info in dateTimeInfo)
                    {
                        status.TryAdd($"DateTime_{info.Key}", info.Value);
                    }
                }

                return status.Any() 
                    ? (true, status, string.Empty)
                    : (false, new Dictionary<string, object>(), "No status information available");
            }
            catch (Exception ex)
            {
                return (false, new Dictionary<string, object>(), ex.Message);
            }
        }

        /// <summary>
        /// Gets video configuration information
        /// </summary>
        public async Task<(bool Success, Dictionary<string, object> Configuration, string ErrorMessage)> GetVideoConfigurationAsync()
        {
            try
            {
                var config = new Dictionary<string, object>();

                // Get capabilities first to see what's available
                var capabilitiesRequest = OnvifSoapTemplates.CreateGetCapabilitiesRequest(_connection.Username, _connection.Password);
                var response = await SendSoapRequestAsync(capabilitiesRequest, OnvifUrl.SoapActions.GetCapabilities);

                if (response.Success && !OnvifSoapTemplates.IsSoapFault(response.Content))
                {
                    var capabilities = OnvifSoapTemplates.ParseSoapResponse(response.Content);
                    
                    // Extract video-related capabilities
                    foreach (var capability in capabilities.Where(c => c.Key.Contains("Media", StringComparison.OrdinalIgnoreCase) || 
                                                                      c.Key.Contains("Video", StringComparison.OrdinalIgnoreCase) ||
                                                                      c.Key.Contains("Streaming", StringComparison.OrdinalIgnoreCase)))
                    {
                        config[capability.Key] = capability.Value;
                    }
                }

                return config.Any()
                    ? (true, config, string.Empty)
                    : (false, new Dictionary<string, object>(), "No video configuration available");
            }
            catch (Exception ex)
            {
                return (false, new Dictionary<string, object>(), ex.Message);
            }
        }

        /// <summary>
        /// Reboots the ONVIF camera
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> RebootCameraAsync()
        {
            try
            {
                var rebootRequest = OnvifSoapTemplates.CreateSimpleSoapEnvelope("<tds:SystemReboot/>", OnvifUrl.SoapActions.SystemReboot);
                if (!string.IsNullOrEmpty(_connection.Username) && !string.IsNullOrEmpty(_connection.Password))
                {
                    rebootRequest = OnvifSoapTemplates.CreateAuthenticatedSoapEnvelope("<tds:SystemReboot/>", _connection.Username, _connection.Password, OnvifUrl.SoapActions.SystemReboot);
                }

                var response = await SendSoapRequestAsync(rebootRequest, OnvifUrl.SoapActions.SystemReboot);

                if (!response.Success)
                {
                    return (false, $"Failed to reboot camera: {response.StatusCode}");
                }

                if (OnvifSoapTemplates.IsSoapFault(response.Content))
                {
                    var faultString = OnvifSoapTemplates.ExtractSoapFaultString(response.Content);
                    return (false, $"SOAP fault: {faultString}");
                }

                return (true, "Reboot command sent successfully");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private async Task<(bool Success, string Content, System.Net.HttpStatusCode StatusCode)> SendSoapRequestAsync(string soapRequest, string soapAction)
        {
            // Use reflection to access private methods from OnvifConnection
            var sendMethod = _connection.GetType().GetMethod("SendSoapRequestAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var discoverMethod = _connection.GetType().GetMethod("DiscoverDeviceServiceAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            // Ensure device service URL is discovered
            var deviceServiceUrlField = _connection.GetType().GetField("_deviceServiceUrl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var deviceServiceUrl = (string?)deviceServiceUrlField?.GetValue(_connection);

            if (string.IsNullOrEmpty(deviceServiceUrl))
            {
                var discovered = await (Task<bool>)discoverMethod!.Invoke(_connection, null)!;
                if (!discovered)
                {
                    return (false, string.Empty, System.Net.HttpStatusCode.ServiceUnavailable);
                }
                deviceServiceUrl = (string?)deviceServiceUrlField?.GetValue(_connection);
            }

            if (string.IsNullOrEmpty(deviceServiceUrl))
            {
                return (false, string.Empty, System.Net.HttpStatusCode.ServiceUnavailable);
            }

            var result = await (Task<(bool Success, string Content, System.Net.HttpStatusCode StatusCode)>)sendMethod!.Invoke(_connection, new object[] { deviceServiceUrl, soapRequest, soapAction })!;
            return result;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }
}