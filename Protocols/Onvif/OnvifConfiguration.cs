using wpfhikip.Models;

namespace wpfhikip.Protocols.Onvif
{
    /// <summary>
    /// Configuration management for ONVIF cameras
    /// </summary>
    public sealed class OnvifConfiguration : IDisposable
    {
        private readonly OnvifConnection _connection;
        private bool _disposed;

        public OnvifConfiguration(OnvifConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        /// <summary>
        /// Gets device information from the ONVIF camera
        /// </summary>
        public async Task<(bool Success, Dictionary<string, object> DeviceInfo, string ErrorMessage)> GetDeviceInfoAsync()
        {
            try
            {
                var deviceInfoRequest = OnvifSoapTemplates.CreateGetDeviceInformationRequest(_connection.Username, _connection.Password);
                var response = await SendSoapRequestAsync(deviceInfoRequest, OnvifUrl.SoapActions.GetDeviceInformation);

                if (!response.Success)
                {
                    return (false, new Dictionary<string, object>(), $"Failed to get device info: {response.StatusCode}");
                }

                if (OnvifSoapTemplates.IsSoapFault(response.Content))
                {
                    var faultString = OnvifSoapTemplates.ExtractSoapFaultString(response.Content);
                    return (false, new Dictionary<string, object>(), $"SOAP fault: {faultString}");
                }

                var deviceInfo = OnvifSoapTemplates.ExtractDeviceInfo(response.Content);
                var objectData = deviceInfo.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);

                return (true, objectData, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, new Dictionary<string, object>(), ex.Message);
            }
        }

        /// <summary>
        /// Gets capabilities from the ONVIF camera
        /// </summary>
        public async Task<(bool Success, Dictionary<string, object> Capabilities, string ErrorMessage)> GetCapabilitiesAsync()
        {
            try
            {
                var capabilitiesRequest = OnvifSoapTemplates.CreateGetCapabilitiesRequest(_connection.Username, _connection.Password);
                var response = await SendSoapRequestAsync(capabilitiesRequest, OnvifUrl.SoapActions.GetCapabilities);

                if (!response.Success)
                {
                    return (false, new Dictionary<string, object>(), $"Failed to get capabilities: {response.StatusCode}");
                }

                if (OnvifSoapTemplates.IsSoapFault(response.Content))
                {
                    var faultString = OnvifSoapTemplates.ExtractSoapFaultString(response.Content);
                    return (false, new Dictionary<string, object>(), $"SOAP fault: {faultString}");
                }

                var capabilities = OnvifSoapTemplates.ParseSoapResponse(response.Content);
                var objectData = capabilities.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);

                return (true, objectData, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, new Dictionary<string, object>(), ex.Message);
            }
        }

        /// <summary>
        /// Gets current network configuration from the ONVIF device
        /// </summary>
        public async Task<(bool Success, Dictionary<string, object> Configuration, string ErrorMessage)> GetNetworkConfigurationAsync()
        {
            try
            {
                var networkRequest = OnvifSoapTemplates.CreateGetNetworkInterfacesRequest(_connection.Username, _connection.Password);
                var response = await SendSoapRequestAsync(networkRequest, OnvifUrl.SoapActions.GetNetworkInterfaces);

                if (!response.Success)
                {
                    return (false, new Dictionary<string, object>(), $"Failed to get network config: {response.StatusCode}");
                }

                if (OnvifSoapTemplates.IsSoapFault(response.Content))
                {
                    var faultString = OnvifSoapTemplates.ExtractSoapFaultString(response.Content);
                    return (false, new Dictionary<string, object>(), $"SOAP fault: {faultString}");
                }

                var networkConfig = OnvifSoapTemplates.ParseSoapResponse(response.Content);
                var objectData = networkConfig.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);

                return (true, objectData, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, new Dictionary<string, object>(), ex.Message);
            }
        }

        /// <summary>
        /// Gets NTP configuration from the ONVIF device
        /// </summary>
        public async Task<(bool Success, Dictionary<string, object> Configuration, string ErrorMessage)> GetNtpConfigurationAsync()
        {
            try
            {
                var ntpRequest = OnvifSoapTemplates.CreateGetNtpRequest(_connection.Username, _connection.Password);
                var response = await SendSoapRequestAsync(ntpRequest, OnvifUrl.SoapActions.GetNTP);

                if (!response.Success)
                {
                    return (false, new Dictionary<string, object>(), $"Failed to get NTP config: {response.StatusCode}");
                }

                if (OnvifSoapTemplates.IsSoapFault(response.Content))
                {
                    var faultString = OnvifSoapTemplates.ExtractSoapFaultString(response.Content);
                    return (false, new Dictionary<string, object>(), $"SOAP fault: {faultString}");
                }

                var ntpConfig = OnvifSoapTemplates.ParseSoapResponse(response.Content);
                var objectData = ntpConfig.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);

                return (true, objectData, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, new Dictionary<string, object>(), ex.Message);
            }
        }

        /// <summary>
        /// Updates network configuration with validation
        /// </summary>
        public async Task<OnvifOperationResult> UpdateNetworkConfigurationAsync(Camera camera)
        {
            try
            {
                // Get current network interfaces to find the token
                var getNetworkRequest = OnvifSoapTemplates.CreateGetNetworkInterfacesRequest(_connection.Username, _connection.Password);
                var getResponse = await SendSoapRequestAsync(getNetworkRequest, OnvifUrl.SoapActions.GetNetworkInterfaces);

                if (!getResponse.Success)
                {
                    return OnvifOperationResult.CreateFailure($"Failed to get network interfaces: {getResponse.StatusCode}");
                }

                var interfaceToken = OnvifSoapTemplates.ExtractNetworkInterfaceToken(getResponse.Content);

                // Set new network configuration
                var setNetworkRequest = OnvifSoapTemplates.CreateSetNetworkInterfacesRequest(camera, interfaceToken, _connection.Username, _connection.Password);
                var setResponse = await SendSoapRequestAsync(setNetworkRequest, OnvifUrl.SoapActions.SetNetworkInterfaces);

                if (!setResponse.Success)
                {
                    return OnvifOperationResult.CreateFailure($"Failed to set network config: {setResponse.StatusCode}");
                }

                if (OnvifSoapTemplates.IsSoapFault(setResponse.Content))
                {
                    var faultString = OnvifSoapTemplates.ExtractSoapFaultString(setResponse.Content);
                    return OnvifOperationResult.CreateFailure($"SOAP fault: {faultString}");
                }

                return OnvifOperationResult.CreateSuccess("Network configuration updated successfully");
            }
            catch (Exception ex)
            {
                return OnvifOperationResult.CreateFailure(ex.Message);
            }
        }

        private async Task<(bool Success, string Content, System.Net.HttpStatusCode StatusCode)> SendSoapRequestAsync(string soapRequest, string soapAction)
        {
            // Discover device service URL if not already done
            if (string.IsNullOrEmpty(_connection.GetType().GetField("_deviceServiceUrl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(_connection) as string))
            {
                var discoverMethod = _connection.GetType().GetMethod("DiscoverDeviceServiceAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var discovered = await (Task<bool>)discoverMethod!.Invoke(_connection, null)!;
                if (!discovered)
                {
                    return (false, string.Empty, System.Net.HttpStatusCode.ServiceUnavailable);
                }
            }

            var deviceServiceUrl = (string?)_connection.GetType().GetField("_deviceServiceUrl", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(_connection);
            if (string.IsNullOrEmpty(deviceServiceUrl))
            {
                return (false, string.Empty, System.Net.HttpStatusCode.ServiceUnavailable);
            }

            var sendMethod = _connection.GetType().GetMethod("SendSoapRequestAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
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