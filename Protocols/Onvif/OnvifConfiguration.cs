using wpfhikip.Models;
using wpfhikip.Protocols.Common;

namespace wpfhikip.Protocols.Onvif
{
    /// <summary>
    /// Configuration management for ONVIF cameras with enhanced network information retrieval
    /// </summary>
    public sealed class OnvifConfiguration : IProtocolConfiguration
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
        public async Task<ProtocolOperationResult<Dictionary<string, object>>> GetDeviceInfoAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var deviceInfoRequest = OnvifSoapTemplates.CreateGetDeviceInformationRequest(_connection.Username, _connection.Password);
                var response = await _connection.SendSoapToDeviceServiceAsync(deviceInfoRequest, OnvifUrl.SoapActions.GetDeviceInformation);

                if (!response.Success)
                {
                    return ProtocolOperationResult<Dictionary<string, object>>.CreateFailure($"Failed to get device info: {response.StatusCode}");
                }

                if (OnvifSoapTemplates.IsSoapFault(response.Content))
                {
                    var faultString = OnvifSoapTemplates.ExtractSoapFaultString(response.Content);
                    return ProtocolOperationResult<Dictionary<string, object>>.CreateFailure($"SOAP fault: {faultString}");
                }

                var deviceInfo = OnvifSoapTemplates.ExtractDeviceInfo(response.Content);
                var objectData = deviceInfo.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);

                return ProtocolOperationResult<Dictionary<string, object>>.CreateSuccess(objectData);
            }
            catch (Exception ex)
            {
                return ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(ex.Message);
            }
        }

        /// <summary>
        /// Gets comprehensive network configuration from the ONVIF device
        /// </summary>
        public async Task<ProtocolOperationResult<Dictionary<string, object>>> GetNetworkInfoAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var networkConfig = new Dictionary<string, object>();
                var errors = new List<string>();

                // Get network interfaces (IP, subnet mask, MAC address)
                try
                {
                    var networkRequest = OnvifSoapTemplates.CreateGetNetworkInterfacesRequest(_connection.Username, _connection.Password);
                    var networkResponse = await _connection.SendSoapToDeviceServiceAsync(networkRequest, OnvifUrl.SoapActions.GetNetworkInterfaces);

                    if (networkResponse.Success && !OnvifSoapTemplates.IsSoapFault(networkResponse.Content))
                    {
                        var networkInfo = OnvifSoapTemplates.ExtractNetworkInfo(networkResponse.Content);
                        foreach (var info in networkInfo)
                        {
                            networkConfig[info.Key] = info.Value;
                        }
                    }
                    else if (OnvifSoapTemplates.IsSoapFault(networkResponse.Content))
                    {
                        errors.Add($"Network interfaces: {OnvifSoapTemplates.ExtractSoapFaultString(networkResponse.Content)}");
                    }
                    else
                    {
                        errors.Add($"Network interfaces failed: {networkResponse.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Network interfaces error: {ex.Message}");
                }

                // Get DNS configuration
                try
                {
                    var dnsRequest = OnvifSoapTemplates.CreateGetDNSRequest(_connection.Username, _connection.Password);
                    var dnsResponse = await _connection.SendSoapToDeviceServiceAsync(dnsRequest, OnvifUrl.SoapActions.GetDNS);

                    if (dnsResponse.Success && !OnvifSoapTemplates.IsSoapFault(dnsResponse.Content))
                    {
                        var dnsInfo = OnvifSoapTemplates.ExtractDNSInfo(dnsResponse.Content);
                        foreach (var info in dnsInfo)
                        {
                            networkConfig.TryAdd(info.Key, info.Value);
                        }
                    }
                    else if (OnvifSoapTemplates.IsSoapFault(dnsResponse.Content))
                    {
                        errors.Add($"DNS: {OnvifSoapTemplates.ExtractSoapFaultString(dnsResponse.Content)}");
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"DNS error: {ex.Message}");
                }

                // Get default gateway
                try
                {
                    var gatewayRequest = OnvifSoapTemplates.CreateGetNetworkDefaultGatewayRequest(_connection.Username, _connection.Password);
                    var gatewayResponse = await _connection.SendSoapToDeviceServiceAsync(gatewayRequest, OnvifUrl.SoapActions.GetNetworkDefaultGateway);

                    if (gatewayResponse.Success && !OnvifSoapTemplates.IsSoapFault(gatewayResponse.Content))
                    {
                        var gatewayInfo = OnvifSoapTemplates.ExtractGatewayInfo(gatewayResponse.Content);
                        foreach (var info in gatewayInfo)
                        {
                            networkConfig.TryAdd(info.Key, info.Value);
                        }
                    }
                    else if (OnvifSoapTemplates.IsSoapFault(gatewayResponse.Content))
                    {
                        errors.Add($"Gateway: {OnvifSoapTemplates.ExtractSoapFaultString(gatewayResponse.Content)}");
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Gateway error: {ex.Message}");
                }

                var errorMessage = errors.Any() ? string.Join("; ", errors) : string.Empty;
                var success = networkConfig.Any();

                return success
                    ? ProtocolOperationResult<Dictionary<string, object>>.CreateSuccess(networkConfig)
                    : ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(errorMessage);
            }
            catch (Exception ex)
            {
                return ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(ex.Message);
            }
        }

        /// <summary>
        /// Gets video configuration information from ONVIF device
        /// </summary>
        public async Task<ProtocolOperationResult<Dictionary<string, object>>> GetVideoInfoAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var config = new Dictionary<string, object>();

                // Get media service URL
                var mediaServiceUrl = await _connection.GetMediaServiceUrlAsync();
                if (string.IsNullOrEmpty(mediaServiceUrl))
                {
                    return ProtocolOperationResult<Dictionary<string, object>>.CreateFailure("Media service not available");
                }

                // Get media profiles
                var profilesRequest = OnvifSoapTemplates.CreateGetProfilesRequest(_connection.Username, _connection.Password);
                var profilesResponse = await _connection.SendSoapToMediaServiceAsync(profilesRequest, OnvifUrl.SoapActions.GetProfiles);

                if (profilesResponse.Success && !OnvifSoapTemplates.IsSoapFault(profilesResponse.Content))
                {
                    var profiles = OnvifSoapTemplates.ExtractMediaProfiles(profilesResponse.Content);

                    if (profiles.Any())
                    {
                        var mainProfile = profiles.First(); // Use the first profile as main

                        // Extract video configuration from the main profile
                        config["codecType"] = mainProfile.Encoding;
                        config["resolution"] = mainProfile.Resolution;
                        config["frameRate"] = mainProfile.FrameRate;
                        config["bitRate"] = mainProfile.BitRate;
                        config["quality"] = mainProfile.Quality;
                        config["govLength"] = mainProfile.GovLength;
                        config["profileName"] = mainProfile.Name;
                        config["profileToken"] = mainProfile.Token;

                        // Try to get stream URIs for the profiles
                        for (int i = 0; i < Math.Min(profiles.Count, 2); i++) // Get up to 2 streams
                        {
                            var profile = profiles[i];
                            var streamUriRequest = OnvifSoapTemplates.CreateGetStreamUriRequest(
                                profile.Token, _connection.Username, _connection.Password);

                            var streamResponse = await _connection.SendSoapToMediaServiceAsync(streamUriRequest, OnvifUrl.SoapActions.GetStreamUri);

                            if (streamResponse.Success && !OnvifSoapTemplates.IsSoapFault(streamResponse.Content))
                            {
                                var streamUri = OnvifSoapTemplates.ExtractStreamUri(streamResponse.Content);
                                if (!string.IsNullOrEmpty(streamUri))
                                {
                                    config[$"streamUri{i + 1}"] = streamUri;
                                }
                            }
                        }

                        // Set quality control type based on available information
                        if (config.ContainsKey("bitRate") && !string.IsNullOrEmpty(config["bitRate"].ToString()))
                        {
                            config["qualityControlType"] = "CBR"; // Assume CBR if bitrate is specified
                        }
                        else if (config.ContainsKey("quality") && !string.IsNullOrEmpty(config["quality"].ToString()))
                        {
                            config["qualityControlType"] = "VBR"; // Assume VBR if quality is specified
                        }
                    }
                }

                return config.Any()
                    ? ProtocolOperationResult<Dictionary<string, object>>.CreateSuccess(config)
                    : ProtocolOperationResult<Dictionary<string, object>>.CreateFailure("No video configuration available");
            }
            catch (Exception ex)
            {
                return ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(ex.Message);
            }
        }

        /// <summary>
        /// Sets network configuration on the ONVIF device
        /// </summary>
        public async Task<ProtocolOperationResult<bool>> SetNetworkConfigurationAsync(Camera camera, CancellationToken cancellationToken = default)
        {
            try
            {
                // Get current network interfaces to find the token
                var getNetworkRequest = OnvifSoapTemplates.CreateGetNetworkInterfacesRequest(_connection.Username, _connection.Password);
                var getResponse = await _connection.SendSoapToDeviceServiceAsync(getNetworkRequest, OnvifUrl.SoapActions.GetNetworkInterfaces);

                if (!getResponse.Success)
                {
                    return ProtocolOperationResult<bool>.CreateFailure($"Failed to get network interfaces: {getResponse.StatusCode}");
                }

                var interfaceToken = OnvifSoapTemplates.ExtractNetworkInterfaceToken(getResponse.Content);

                // Set new network configuration
                var setNetworkRequest = OnvifSoapTemplates.CreateSetNetworkInterfacesRequest(camera, interfaceToken, _connection.Username, _connection.Password);
                var setResponse = await _connection.SendSoapToDeviceServiceAsync(setNetworkRequest, OnvifUrl.SoapActions.SetNetworkInterfaces);

                if (!setResponse.Success)
                {
                    return ProtocolOperationResult<bool>.CreateFailure($"Failed to set network config: {setResponse.StatusCode}");
                }

                if (OnvifSoapTemplates.IsSoapFault(setResponse.Content))
                {
                    var faultString = OnvifSoapTemplates.ExtractSoapFaultString(setResponse.Content);
                    return ProtocolOperationResult<bool>.CreateFailure($"SOAP fault: {faultString}");
                }

                return ProtocolOperationResult<bool>.CreateSuccess(true);
            }
            catch (Exception ex)
            {
                return ProtocolOperationResult<bool>.CreateFailure(ex.Message);
            }
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