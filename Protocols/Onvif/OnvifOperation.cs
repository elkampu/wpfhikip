using wpfhikip.Models;
using wpfhikip.Protocols.Common;

namespace wpfhikip.Protocols.Onvif
{
    /// <summary>
    /// ONVIF operation management for camera control with Media Service support
    /// </summary>
    public sealed class OnvifOperation : IProtocolOperation
    {
        private readonly OnvifConnection _connection;
        private bool _disposed;

        public OnvifOperation(OnvifConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        public string GetMainStreamUrl(int channel = 1)
        {
            // ONVIF stream URLs are typically discovered through GetProfiles and GetStreamUri calls
            // For now, return a basic RTSP URL format that can be refined with proper ONVIF media service calls
            var port = _connection.Port == 80 ? 554 : _connection.Port; // Default to RTSP port if using HTTP port
            return $"rtsp://{_connection.Username}:{_connection.Password}@{_connection.IpAddress}:{port}/onvif1";
        }

        public string GetSubStreamUrl(int channel = 1)
        {
            // Similar to main stream but typically a lower quality profile
            var port = _connection.Port == 80 ? 554 : _connection.Port;
            return $"rtsp://{_connection.Username}:{_connection.Password}@{_connection.IpAddress}:{port}/onvif2";
        }

        public async Task<ProtocolOperationResult<Dictionary<string, object>>> GetCameraStatusAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var status = new Dictionary<string, object>();

                // Get device information for status
                var deviceInfoRequest = OnvifSoapTemplates.CreateGetDeviceInformationRequest(_connection.Username, _connection.Password);
                var response = await _connection.SendSoapToDeviceServiceAsync(deviceInfoRequest, OnvifUrl.SoapActions.GetDeviceInformation);

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
                var dateTimeResponse = await _connection.SendSoapToDeviceServiceAsync(dateTimeRequest, OnvifUrl.SoapActions.GetSystemDateAndTime);

                if (dateTimeResponse.Success && !OnvifSoapTemplates.IsSoapFault(dateTimeResponse.Content))
                {
                    var dateTimeInfo = OnvifSoapTemplates.ParseSoapResponse(dateTimeResponse.Content);
                    foreach (var info in dateTimeInfo)
                    {
                        status.TryAdd($"DateTime_{info.Key}", info.Value);
                    }
                }

                return status.Any()
                    ? ProtocolOperationResult<Dictionary<string, object>>.CreateSuccess(status)
                    : ProtocolOperationResult<Dictionary<string, object>>.CreateFailure("No status information available");
            }
            catch (Exception ex)
            {
                return ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(ex.Message);
            }
        }

        public async Task<ProtocolOperationResult<Dictionary<string, object>>> GetVideoConfigurationAsync(CancellationToken cancellationToken = default)
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

                // If we couldn't get profiles, try basic capabilities as fallback
                if (!config.Any())
                {
                    var capabilitiesRequest = OnvifSoapTemplates.CreateGetCapabilitiesRequest(_connection.Username, _connection.Password);
                    var capabilitiesResponse = await _connection.SendSoapToDeviceServiceAsync(capabilitiesRequest, OnvifUrl.SoapActions.GetCapabilities);

                    if (capabilitiesResponse.Success && !OnvifSoapTemplates.IsSoapFault(capabilitiesResponse.Content))
                    {
                        var capabilities = OnvifSoapTemplates.ParseSoapResponse(capabilitiesResponse.Content);

                        // Extract media-related capabilities
                        foreach (var capability in capabilities.Where(c =>
                            c.Key.Contains("Media", StringComparison.OrdinalIgnoreCase) ||
                            c.Key.Contains("Video", StringComparison.OrdinalIgnoreCase) ||
                            c.Key.Contains("Streaming", StringComparison.OrdinalIgnoreCase)))
                        {
                            config[capability.Key] = capability.Value;
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

        public async Task<ProtocolOperationResult<bool>> RebootCameraAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var rebootRequest = OnvifSoapTemplates.CreateSimpleSoapEnvelope("<tds:SystemReboot/>", OnvifUrl.SoapActions.SystemReboot);
                if (!string.IsNullOrEmpty(_connection.Username) && !string.IsNullOrEmpty(_connection.Password))
                {
                    rebootRequest = OnvifSoapTemplates.CreateAuthenticatedSoapEnvelope("<tds:SystemReboot/>", _connection.Username, _connection.Password, OnvifUrl.SoapActions.SystemReboot);
                }

                var response = await _connection.SendSoapToDeviceServiceAsync(rebootRequest, OnvifUrl.SoapActions.SystemReboot);

                if (!response.Success)
                {
                    return ProtocolOperationResult<bool>.CreateFailure($"Failed to reboot camera: {response.StatusCode}");
                }

                if (OnvifSoapTemplates.IsSoapFault(response.Content))
                {
                    var faultString = OnvifSoapTemplates.ExtractSoapFaultString(response.Content);
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