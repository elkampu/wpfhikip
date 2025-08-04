using wpfhikip.Models;
using wpfhikip.Protocols.Common;
using System.Diagnostics;
using System.Net;

namespace wpfhikip.Protocols.Onvif
{
    /// <summary>
    /// Configuration management for ONVIF cameras with enhanced network information retrieval and robust DHCP handling
    /// </summary>
    public sealed class OnvifConfiguration : IProtocolConfiguration
    {
        private readonly OnvifConnection _connection;
        private bool _disposed;
        private static readonly ActivitySource ActivitySource = new("OnvifConfiguration");

        public OnvifConfiguration(OnvifConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        /// <summary>
        /// Gets device information from the ONVIF camera
        /// </summary>
        public async Task<ProtocolOperationResult<Dictionary<string, object>>> GetDeviceInfoAsync(CancellationToken cancellationToken = default)
        {
            using var activity = ActivitySource.StartActivity("GetDeviceInfo");
            activity?.SetTag("device.ip", _connection.IpAddress);
            activity?.SetTag("device.port", _connection.Port);

            try
            {
                var deviceInfoRequest = OnvifSoapTemplates.CreateGetDeviceInformationRequest(_connection.Username, _connection.Password);
                var response = await _connection.SendSoapToDeviceServiceAsync(deviceInfoRequest, OnvifUrl.SoapActions.GetDeviceInformation);

                activity?.SetTag("response.success", response.Success);
                activity?.SetTag("response.status_code", response.StatusCode.ToString());

                if (!response.Success)
                {
                    var errorMsg = $"Failed to get device info: {response.StatusCode}";
                    activity?.SetStatus(ActivityStatusCode.Error, errorMsg);
                    return ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(errorMsg);
                }

                if (OnvifSoapTemplates.IsSoapFault(response.Content))
                {
                    var faultString = OnvifSoapTemplates.ExtractSoapFaultString(response.Content);
                    var errorMsg = $"SOAP fault: {faultString}";
                    activity?.SetStatus(ActivityStatusCode.Error, errorMsg);
                    return ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(errorMsg);
                }

                var deviceInfo = OnvifSoapTemplates.ExtractDeviceInfo(response.Content);
                var objectData = deviceInfo.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);

                activity?.SetTag("device_info.count", objectData.Count);
                return ProtocolOperationResult<Dictionary<string, object>>.CreateSuccess(objectData);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                return ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(ex.Message);
            }
        }

        /// <summary>
        /// Gets comprehensive network configuration from the ONVIF device
        /// </summary>
        public async Task<ProtocolOperationResult<Dictionary<string, object>>> GetNetworkInfoAsync(CancellationToken cancellationToken = default)
        {
            using var activity = ActivitySource.StartActivity("GetNetworkInfo");
            activity?.SetTag("device.ip", _connection.IpAddress);

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
                        activity?.SetTag("network_interfaces.success", true);
                    }
                    else if (OnvifSoapTemplates.IsSoapFault(networkResponse.Content))
                    {
                        var faultMsg = OnvifSoapTemplates.ExtractSoapFaultString(networkResponse.Content);
                        errors.Add($"Network interfaces: {faultMsg}");
                        activity?.SetTag("network_interfaces.fault", faultMsg);
                    }
                    else
                    {
                        errors.Add($"Network interfaces failed: {networkResponse.StatusCode}");
                        activity?.SetTag("network_interfaces.error", networkResponse.StatusCode.ToString());
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Network interfaces error: {ex.Message}");
                    activity?.SetTag("network_interfaces.exception", ex.Message);
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
                        activity?.SetTag("dns.success", true);
                    }
                    else if (OnvifSoapTemplates.IsSoapFault(dnsResponse.Content))
                    {
                        var faultMsg = OnvifSoapTemplates.ExtractSoapFaultString(dnsResponse.Content);
                        errors.Add($"DNS: {faultMsg}");
                        activity?.SetTag("dns.fault", faultMsg);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"DNS error: {ex.Message}");
                    activity?.SetTag("dns.exception", ex.Message);
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
                        activity?.SetTag("gateway.success", true);
                    }
                    else if (OnvifSoapTemplates.IsSoapFault(gatewayResponse.Content))
                    {
                        var faultMsg = OnvifSoapTemplates.ExtractSoapFaultString(gatewayResponse.Content);
                        errors.Add($"Gateway: {faultMsg}");
                        activity?.SetTag("gateway.fault", faultMsg);
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Gateway error: {ex.Message}");
                    activity?.SetTag("gateway.exception", ex.Message);
                }

                var errorMessage = errors.Any() ? string.Join("; ", errors) : string.Empty;
                var success = networkConfig.Any();

                activity?.SetTag("network_config.count", networkConfig.Count);
                activity?.SetTag("errors.count", errors.Count);

                return success
                    ? ProtocolOperationResult<Dictionary<string, object>>.CreateSuccess(networkConfig)
                    : ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(errorMessage);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                return ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(ex.Message);
            }
        }

        /// <summary>
        /// Gets video configuration information from ONVIF device
        /// </summary>
        public async Task<ProtocolOperationResult<Dictionary<string, object>>> GetVideoInfoAsync(CancellationToken cancellationToken = default)
        {
            using var activity = ActivitySource.StartActivity("GetVideoInfo");
            activity?.SetTag("device.ip", _connection.IpAddress);

            try
            {
                var config = new Dictionary<string, object>();

                // Get media service URL
                var mediaServiceUrl = await _connection.GetMediaServiceUrlAsync();
                if (string.IsNullOrEmpty(mediaServiceUrl))
                {
                    var errorMsg = "Media service not available";
                    activity?.SetStatus(ActivityStatusCode.Error, errorMsg);
                    return ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(errorMsg);
                }

                activity?.SetTag("media_service.url", mediaServiceUrl);

                // Get media profiles
                var profilesRequest = OnvifSoapTemplates.CreateGetProfilesRequest(_connection.Username, _connection.Password);
                var profilesResponse = await _connection.SendSoapToMediaServiceAsync(profilesRequest, OnvifUrl.SoapActions.GetProfiles);

                if (profilesResponse.Success && !OnvifSoapTemplates.IsSoapFault(profilesResponse.Content))
                {
                    var profiles = OnvifSoapTemplates.ExtractMediaProfiles(profilesResponse.Content);
                    activity?.SetTag("profiles.count", profiles.Count);

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

                activity?.SetTag("video_config.count", config.Count);
                return config.Any()
                    ? ProtocolOperationResult<Dictionary<string, object>>.CreateSuccess(config)
                    : ProtocolOperationResult<Dictionary<string, object>>.CreateFailure("No video configuration available");
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                return ProtocolOperationResult<Dictionary<string, object>>.CreateFailure(ex.Message);
            }
        }

        /// <summary>
        /// Sets network configuration on the ONVIF device with enhanced DHCP disabling strategies
        /// </summary>
        public async Task<ProtocolOperationResult<bool>> SetNetworkConfigurationAsync(Camera camera, CancellationToken cancellationToken = default)
        {
            using var activity = ActivitySource.StartActivity("SetNetworkConfiguration");
            activity?.SetTag("device.ip", camera.CurrentIP);
            activity?.SetTag("target.ip", camera.NewIP);
            activity?.SetTag("target.mask", camera.NewMask);
            activity?.SetTag("target.gateway", camera.NewGateway);

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Enhanced input validation with detailed logging
                var validationResult = ValidateNetworkConfigurationInputs(camera);
                if (!validationResult.IsValid)
                {
                    var errorMsg = $"Validation failed: {string.Join(", ", validationResult.Errors)}";
                    camera.AddProtocolLog("ONVIF", "Network Config Validation", errorMsg, ProtocolLogLevel.Error);
                    activity?.SetStatus(ActivityStatusCode.Error, errorMsg);
                    return ProtocolOperationResult<bool>.CreateFailure(errorMsg);
                }

                camera.AddProtocolLog("ONVIF", "Network Config Validation", "All input parameters validated successfully", ProtocolLogLevel.Info);

                // Log the network configuration we're about to send with timing
                camera.AddProtocolLog("ONVIF", "Network Config Setup",
                    $"Starting enhanced network configuration - IP: {camera.NewIP}, Mask: {camera.NewMask ?? "255.255.255.0"}, Gateway: {camera.NewGateway}, DNS1: {camera.NewDNS1}, DNS2: {camera.NewDNS2}");

                // Step 1: Get current network interfaces to find the token
                camera.AddProtocolLog("ONVIF", "Network Config", "Step 1: Retrieving current network interface information...");
                var interfaceStopwatch = Stopwatch.StartNew();

                var getNetworkRequest = OnvifSoapTemplates.CreateGetNetworkInterfacesRequest(_connection.Username, _connection.Password);
                var getResponse = await _connection.SendSoapToDeviceServiceAsync(getNetworkRequest, OnvifUrl.SoapActions.GetNetworkInterfaces);

                interfaceStopwatch.Stop();
                camera.AddProtocolLog("ONVIF", "Network Interface Query",
                    $"Network interface query completed in {interfaceStopwatch.ElapsedMilliseconds}ms - Success: {getResponse.Success}",
                    getResponse.Success ? ProtocolLogLevel.Info : ProtocolLogLevel.Error);

                if (!getResponse.Success)
                {
                    var errorMsg = $"Failed to get network interfaces: {getResponse.StatusCode}";
                    camera.AddProtocolLog("ONVIF", "Network Config Error", errorMsg, ProtocolLogLevel.Error);
                    LogHttpResponseDetails(camera, "GetNetworkInterfaces", getResponse);
                    activity?.SetStatus(ActivityStatusCode.Error, errorMsg);
                    return ProtocolOperationResult<bool>.CreateFailure(errorMsg);
                }

                // Enhanced SOAP fault handling
                if (OnvifSoapTemplates.IsSoapFault(getResponse.Content))
                {
                    var faultString = OnvifSoapTemplates.ExtractSoapFaultString(getResponse.Content);
                    var faultDetails = AnalyzeSoapFault(getResponse.Content);

                    camera.AddProtocolLog("ONVIF", "Network Config SOAP Fault",
                        $"SOAP fault getting interfaces: {faultString}", ProtocolLogLevel.Error);
                    camera.AddProtocolLog("ONVIF", "SOAP Fault Details", faultDetails, ProtocolLogLevel.Error);

                    activity?.SetStatus(ActivityStatusCode.Error, faultString);
                    return ProtocolOperationResult<bool>.CreateFailure($"SOAP fault getting interfaces: {faultString}");
                }

                // Extract and validate interface token
                var interfaceToken = OnvifSoapTemplates.ExtractNetworkInterfaceToken(getResponse.Content);
                if (string.IsNullOrEmpty(interfaceToken) || interfaceToken == "eth0")
                {
                    camera.AddProtocolLog("ONVIF", "Network Config Warning",
                        $"Using default/fallback interface token: {interfaceToken}. This may indicate issues with interface detection.",
                        ProtocolLogLevel.Warning);
                }
                else
                {
                    camera.AddProtocolLog("ONVIF", "Network Config",
                        $"Successfully extracted interface token: {interfaceToken}");
                }

                // Log current network configuration before changes
                LogCurrentNetworkState(camera, getResponse.Content);

                // Check if currently using DHCP
                var currentNetworkInfo = OnvifSoapTemplates.ExtractNetworkInfo(getResponse.Content);
                var isDhcpCurrentlyEnabled = currentNetworkInfo.GetValueOrDefault("dhcpEnabled", "false").Equals("true", StringComparison.OrdinalIgnoreCase);

                camera.AddProtocolLog("ONVIF", "DHCP Status Check",
                    $"Current DHCP status: {(isDhcpCurrentlyEnabled ? "ENABLED" : "DISABLED")}",
                    isDhcpCurrentlyEnabled ? ProtocolLogLevel.Warning : ProtocolLogLevel.Info);

                // Step 2: Enhanced DHCP disabling strategy
                bool dhcpDisabled = await DisableDhcpWithRobustStrategy(camera, interfaceToken, cancellationToken);
                if (!dhcpDisabled)
                {
                    camera.AddProtocolLog("ONVIF", "Network Config Error",
                        "Failed to disable DHCP. Static configuration may not work properly.", ProtocolLogLevel.Error);
                    // Continue anyway, some cameras might still accept the configuration
                }

                // Step 3: Apply static network configuration
                bool staticConfigSuccess = await ApplyStaticNetworkConfiguration(camera, interfaceToken, cancellationToken);
                if (!staticConfigSuccess)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, "Failed to apply static network configuration");
                    return ProtocolOperationResult<bool>.CreateFailure("Failed to apply static network configuration");
                }

                // Step 4: Verify configuration was applied
                await Task.Delay(2000, cancellationToken); // Give camera time to apply changes
                bool verificationSuccess = await VerifyNetworkConfiguration(camera, cancellationToken);

                // Step 5: Configure additional network settings
                var additionalConfigResults = await ConfigureAdditionalNetworkSettings(camera, cancellationToken);

                stopwatch.Stop();
                var overallResult = GenerateConfigurationSummary(camera, additionalConfigResults, stopwatch.Elapsed, verificationSuccess);

                activity?.SetTag("configuration.success", overallResult.Success);
                activity?.SetTag("configuration.duration_ms", stopwatch.ElapsedMilliseconds);
                activity?.SetTag("dhcp.disabled", dhcpDisabled);
                activity?.SetTag("verification.success", verificationSuccess);

                return overallResult.Success
                    ? ProtocolOperationResult<bool>.CreateSuccess(true)
                    : ProtocolOperationResult<bool>.CreateFailure(overallResult.ErrorMessage ?? "Configuration partially failed");
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                var errorMsg = $"Network configuration cancelled after {stopwatch.ElapsedMilliseconds}ms";
                camera.AddProtocolLog("ONVIF", "Network Config Cancelled", errorMsg, ProtocolLogLevel.Warning);
                activity?.SetStatus(ActivityStatusCode.Error, "Cancelled");
                return ProtocolOperationResult<bool>.CreateFailure(errorMsg);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var errorMsg = $"Exception during network configuration after {stopwatch.ElapsedMilliseconds}ms: {ex.Message}";
                camera.AddProtocolLog("ONVIF", "Network Config Exception", errorMsg, ProtocolLogLevel.Error);

                // Log stack trace for debugging
                if (ex.StackTrace != null)
                {
                    camera.AddProtocolLog("ONVIF", "Exception Stack Trace", ex.StackTrace, ProtocolLogLevel.Error);
                }

                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                return ProtocolOperationResult<bool>.CreateFailure(ex.Message);
            }
        }

        /// <summary>
        /// Implements a robust strategy to disable DHCP using multiple approaches
        /// </summary>
        private async Task<bool> DisableDhcpWithRobustStrategy(Camera camera, string interfaceToken, CancellationToken cancellationToken)
        {
            camera.AddProtocolLog("ONVIF", "DHCP Disabling", "Step 2: Starting robust DHCP disabling strategy...");

            // Strategy 1: Send DHCP-only disable request first
            camera.AddProtocolLog("ONVIF", "DHCP Strategy 1", "Attempting DHCP-only disable request...");
            var dhcpOnlyDisableSuccess = await SendDhcpOnlyDisableRequest(camera, interfaceToken, cancellationToken);

            if (dhcpOnlyDisableSuccess)
            {
                camera.AddProtocolLog("ONVIF", "DHCP Strategy 1 Success", "✓ DHCP disabled successfully with dedicated request", ProtocolLogLevel.Success);

                // Wait for change to take effect
                await Task.Delay(1500, cancellationToken);
                return true;
            }

            camera.AddProtocolLog("ONVIF", "DHCP Strategy 1 Failed", "DHCP-only disable request failed, trying alternative approaches...", ProtocolLogLevel.Warning);

            // Strategy 2: Send interface disable/enable cycle
            camera.AddProtocolLog("ONVIF", "DHCP Strategy 2", "Attempting interface disable/enable cycle...");
            var interfaceCycleSuccess = await CycleNetworkInterface(camera, interfaceToken, cancellationToken);

            if (interfaceCycleSuccess)
            {
                camera.AddProtocolLog("ONVIF", "DHCP Strategy 2 Success", "✓ Interface cycling completed", ProtocolLogLevel.Success);
                await Task.Delay(1500, cancellationToken);
            }

            // Strategy 3: Force network configuration with explicit DHCP=false
            camera.AddProtocolLog("ONVIF", "DHCP Strategy 3", "Forcing network configuration with explicit DHCP=false...");
            return await SendForcedDhcpDisableRequest(camera, interfaceToken, cancellationToken);
        }

        /// <summary>
        /// Sends a dedicated request to disable DHCP only
        /// </summary>
        private async Task<bool> SendDhcpOnlyDisableRequest(Camera camera, string interfaceToken, CancellationToken cancellationToken)
        {
            try
            {
                var dhcpDisableRequest = OnvifSoapTemplates.CreateDhcpOnlyDisableRequest(interfaceToken, _connection.Username, _connection.Password);
                LogSoapRequest(camera, "DHCP-Disable", dhcpDisableRequest);

                var response = await _connection.SendSoapToDeviceServiceAsync(dhcpDisableRequest, OnvifUrl.SoapActions.SetNetworkInterfaces);

                if (!response.Success)
                {
                    camera.AddProtocolLog("ONVIF", "DHCP Disable Error", $"HTTP error disabling DHCP: {response.StatusCode}", ProtocolLogLevel.Warning);
                    return false;
                }

                if (OnvifSoapTemplates.IsSoapFault(response.Content))
                {
                    var faultString = OnvifSoapTemplates.ExtractSoapFaultString(response.Content);
                    camera.AddProtocolLog("ONVIF", "DHCP Disable SOAP Fault", $"SOAP fault disabling DHCP: {faultString}", ProtocolLogLevel.Warning);
                    return false;
                }

                LogSoapResponse(camera, "DHCP-Disable", response.Content);
                return true;
            }
            catch (Exception ex)
            {
                camera.AddProtocolLog("ONVIF", "DHCP Disable Exception", $"Exception disabling DHCP: {ex.Message}", ProtocolLogLevel.Warning);
                return false;
            }
        }

        /// <summary>
        /// Cycles the network interface (disable/enable) to reset DHCP state
        /// </summary>
        private async Task<bool> CycleNetworkInterface(Camera camera, string interfaceToken, CancellationToken cancellationToken)
        {
            try
            {
                // Disable interface
                camera.AddProtocolLog("ONVIF", "Interface Cycle", "Disabling network interface...");
                var disableRequest = OnvifSoapTemplates.CreateNetworkInterfaceToggleRequest(interfaceToken, false, _connection.Username, _connection.Password);
                var disableResponse = await _connection.SendSoapToDeviceServiceAsync(disableRequest, OnvifUrl.SoapActions.SetNetworkInterfaces);

                if (disableResponse.Success && !OnvifSoapTemplates.IsSoapFault(disableResponse.Content))
                {
                    camera.AddProtocolLog("ONVIF", "Interface Cycle", "✓ Interface disabled successfully", ProtocolLogLevel.Info);
                    await Task.Delay(1000, cancellationToken); // Wait for disable to take effect

                    // Re-enable interface
                    camera.AddProtocolLog("ONVIF", "Interface Cycle", "Re-enabling network interface...");
                    var enableRequest = OnvifSoapTemplates.CreateNetworkInterfaceToggleRequest(interfaceToken, true, _connection.Username, _connection.Password);
                    var enableResponse = await _connection.SendSoapToDeviceServiceAsync(enableRequest, OnvifUrl.SoapActions.SetNetworkInterfaces);

                    if (enableResponse.Success && !OnvifSoapTemplates.IsSoapFault(enableResponse.Content))
                    {
                        camera.AddProtocolLog("ONVIF", "Interface Cycle", "✓ Interface re-enabled successfully", ProtocolLogLevel.Info);
                        return true;
                    }
                }

                camera.AddProtocolLog("ONVIF", "Interface Cycle", "Interface cycling failed", ProtocolLogLevel.Warning);
                return false;
            }
            catch (Exception ex)
            {
                camera.AddProtocolLog("ONVIF", "Interface Cycle Exception", $"Exception during interface cycling: {ex.Message}", ProtocolLogLevel.Warning);
                return false;
            }
        }

        /// <summary>
        /// Sends a forced DHCP disable request with minimum manual configuration
        /// </summary>
        private async Task<bool> SendForcedDhcpDisableRequest(Camera camera, string interfaceToken, CancellationToken cancellationToken)
        {
            try
            {
                var forcedRequest = OnvifSoapTemplates.CreateForcedDhcpDisableRequest(camera, interfaceToken, _connection.Username, _connection.Password);
                LogSoapRequest(camera, "Forced-DHCP-Disable", forcedRequest);

                var response = await _connection.SendSoapToDeviceServiceAsync(forcedRequest, OnvifUrl.SoapActions.SetNetworkInterfaces);

                if (!response.Success)
                {
                    camera.AddProtocolLog("ONVIF", "Forced DHCP Disable Error", $"HTTP error with forced DHCP disable: {response.StatusCode}", ProtocolLogLevel.Error);
                    return false;
                }

                if (OnvifSoapTemplates.IsSoapFault(response.Content))
                {
                    var faultString = OnvifSoapTemplates.ExtractSoapFaultString(response.Content);
                    camera.AddProtocolLog("ONVIF", "Forced DHCP Disable SOAP Fault", $"SOAP fault with forced DHCP disable: {faultString}", ProtocolLogLevel.Error);
                    return false;
                }

                camera.AddProtocolLog("ONVIF", "Forced DHCP Disable Success", "✓ Forced DHCP disable request completed", ProtocolLogLevel.Success);
                LogSoapResponse(camera, "Forced-DHCP-Disable", response.Content);
                return true;
            }
            catch (Exception ex)
            {
                camera.AddProtocolLog("ONVIF", "Forced DHCP Disable Exception", $"Exception with forced DHCP disable: {ex.Message}", ProtocolLogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// Applies static network configuration after DHCP is disabled
        /// </summary>
        private async Task<bool> ApplyStaticNetworkConfiguration(Camera camera, string interfaceToken, CancellationToken cancellationToken)
        {
            camera.AddProtocolLog("ONVIF", "Static Config", "Step 3: Applying static network configuration...");

            try
            {
                var setNetworkRequest = OnvifSoapTemplates.CreateSetNetworkInterfacesRequest(camera, interfaceToken, _connection.Username, _connection.Password);
                var prefixLength = OnvifSoapTemplates.CalculatePrefixLength(camera.NewMask ?? "255.255.255.0");

                camera.AddProtocolLog("ONVIF", "Static Config Details",
                    $"Configuration details - DHCP: false, Manual IP: {camera.NewIP}, PrefixLength: {prefixLength}, Interface: {interfaceToken}");

                LogSoapRequest(camera, "SetNetworkInterfaces", setNetworkRequest);

                var configStopwatch = Stopwatch.StartNew();
                var setResponse = await _connection.SendSoapToDeviceServiceAsync(setNetworkRequest, OnvifUrl.SoapActions.SetNetworkInterfaces);
                configStopwatch.Stop();

                camera.AddProtocolLog("ONVIF", "Static Config Timing",
                    $"Static configuration request completed in {configStopwatch.ElapsedMilliseconds}ms - Success: {setResponse.Success}",
                    setResponse.Success ? ProtocolLogLevel.Info : ProtocolLogLevel.Error);

                if (!setResponse.Success)
                {
                    var errorMsg = $"Failed to apply static network config: {setResponse.StatusCode}";
                    camera.AddProtocolLog("ONVIF", "Static Config Error", errorMsg, ProtocolLogLevel.Error);
                    LogHttpResponseDetails(camera, "SetNetworkInterfaces", setResponse);
                    return false;
                }

                if (OnvifSoapTemplates.IsSoapFault(setResponse.Content))
                {
                    var faultString = OnvifSoapTemplates.ExtractSoapFaultString(setResponse.Content);
                    var faultDetails = AnalyzeSoapFault(setResponse.Content);

                    camera.AddProtocolLog("ONVIF", "Static Config SOAP Fault",
                        $"SOAP fault applying static config: {faultString}", ProtocolLogLevel.Error);
                    camera.AddProtocolLog("ONVIF", "Static Config Fault Analysis", faultDetails, ProtocolLogLevel.Error);
                    return false;
                }

                camera.AddProtocolLog("ONVIF", "Static Config Success",
                    "✓ Static network configuration applied successfully", ProtocolLogLevel.Success);
                LogSoapResponse(camera, "SetNetworkInterfaces", setResponse.Content);
                return true;
            }
            catch (Exception ex)
            {
                camera.AddProtocolLog("ONVIF", "Static Config Exception",
                    $"Exception applying static config: {ex.Message}", ProtocolLogLevel.Error);
                return false;
            }
        }

        /// <summary>
        /// Verifies that the network configuration was actually applied
        /// </summary>
        private async Task<bool> VerifyNetworkConfiguration(Camera camera, CancellationToken cancellationToken)
        {
            camera.AddProtocolLog("ONVIF", "Config Verification", "Step 4: Verifying network configuration was applied...");

            try
            {
                var getNetworkRequest = OnvifSoapTemplates.CreateGetNetworkInterfacesRequest(_connection.Username, _connection.Password);
                var getResponse = await _connection.SendSoapToDeviceServiceAsync(getNetworkRequest, OnvifUrl.SoapActions.GetNetworkInterfaces);

                if (!getResponse.Success || OnvifSoapTemplates.IsSoapFault(getResponse.Content))
                {
                    camera.AddProtocolLog("ONVIF", "Config Verification Error",
                        "Failed to retrieve network configuration for verification", ProtocolLogLevel.Warning);
                    return false;
                }

                var currentNetworkInfo = OnvifSoapTemplates.ExtractNetworkInfo(getResponse.Content);
                var currentIp = currentNetworkInfo.GetValueOrDefault("currentIp", "");
                var isDhcpEnabled = currentNetworkInfo.GetValueOrDefault("dhcpEnabled", "false").Equals("true", StringComparison.OrdinalIgnoreCase);

                camera.AddProtocolLog("ONVIF", "Config Verification Result",
                    $"Verification - Current IP: {currentIp}, DHCP Enabled: {isDhcpEnabled}, Expected IP: {camera.NewIP}");

                if (isDhcpEnabled)
                {
                    camera.AddProtocolLog("ONVIF", "Config Verification Warning",
                        "⚠️ DHCP is still enabled after configuration attempt", ProtocolLogLevel.Warning);
                    return false;
                }

                if (!string.IsNullOrEmpty(currentIp) && currentIp.Equals(camera.NewIP, StringComparison.OrdinalIgnoreCase))
                {
                    camera.AddProtocolLog("ONVIF", "Config Verification Success",
                        "✓ Configuration verified - IP address matches and DHCP is disabled", ProtocolLogLevel.Success);
                    return true;
                }

                camera.AddProtocolLog("ONVIF", "Config Verification Partial",
                    "DHCP disabled but IP may not have updated yet (this is normal for some cameras)", ProtocolLogLevel.Info);
                return true; // Consider partial success if DHCP is disabled
            }
            catch (Exception ex)
            {
                camera.AddProtocolLog("ONVIF", "Config Verification Exception",
                    $"Exception during verification: {ex.Message}", ProtocolLogLevel.Warning);
                return false;
            }
        }

        /// <summary>
        /// Configures additional network settings (gateway, DNS) with detailed logging
        /// </summary>
        private async Task<AdditionalConfigResults> ConfigureAdditionalNetworkSettings(Camera camera, CancellationToken cancellationToken)
        {
            var results = new AdditionalConfigResults();

            // Configure Gateway
            if (!string.IsNullOrEmpty(camera.NewGateway))
            {
                camera.AddProtocolLog("ONVIF", "Gateway Config", $"Step 5: Setting gateway to: {camera.NewGateway}");
                var gatewayStopwatch = Stopwatch.StartNew();

                results.GatewayAttempted = true;
                results.GatewaySuccess = await SetDefaultGatewayAsync(camera, camera.NewGateway, cancellationToken);

                gatewayStopwatch.Stop();
                camera.AddProtocolLog("ONVIF", "Gateway Config Timing",
                    $"Gateway configuration completed in {gatewayStopwatch.ElapsedMilliseconds}ms - Success: {results.GatewaySuccess}",
                    results.GatewaySuccess ? ProtocolLogLevel.Success : ProtocolLogLevel.Warning);
            }

            // Configure DNS
            if (!string.IsNullOrEmpty(camera.NewDNS1) || !string.IsNullOrEmpty(camera.NewDNS2))
            {
                camera.AddProtocolLog("ONVIF", "DNS Config",
                    $"Step 6: Setting DNS servers - Primary: {camera.NewDNS1 ?? "not set"}, Secondary: {camera.NewDNS2 ?? "not set"}");
                var dnsStopwatch = Stopwatch.StartNew();

                results.DnsAttempted = true;
                results.DnsSuccess = await SetDNSConfigurationAsync(camera, cancellationToken);

                dnsStopwatch.Stop();
                camera.AddProtocolLog("ONVIF", "DNS Config Timing",
                    $"DNS configuration completed in {dnsStopwatch.ElapsedMilliseconds}ms - Success: {results.DnsSuccess}",
                    results.DnsSuccess ? ProtocolLogLevel.Success : ProtocolLogLevel.Warning);
            }

            return results;
        }

        /// <summary>
        /// Enhanced gateway configuration with detailed error handling
        /// </summary>
        private async Task<bool> SetDefaultGatewayAsync(Camera camera, string gatewayIP, CancellationToken cancellationToken = default)
        {
            try
            {
                // Validate gateway IP format
                if (!IPAddress.TryParse(gatewayIP, out var _))
                {
                    camera.AddProtocolLog("ONVIF", "Gateway Config Error",
                        $"Invalid gateway IP format: {gatewayIP}", ProtocolLogLevel.Error);
                    return false;
                }

                var setGatewayRequest = OnvifSoapTemplates.CreateSetNetworkDefaultGatewayRequest(gatewayIP, _connection.Username, _connection.Password);
                LogSoapRequest(camera, "SetNetworkDefaultGateway", setGatewayRequest);

                var response = await _connection.SendSoapToDeviceServiceAsync(setGatewayRequest, OnvifUrl.SoapActions.SetNetworkDefaultGateway);

                if (!response.Success)
                {
                    camera.AddProtocolLog("ONVIF", "Gateway Config Error",
                        $"Failed to set gateway: {response.StatusCode}", ProtocolLogLevel.Warning);
                    LogHttpResponseDetails(camera, "SetNetworkDefaultGateway", response);
                    return false;
                }

                if (OnvifSoapTemplates.IsSoapFault(response.Content))
                {
                    var faultString = OnvifSoapTemplates.ExtractSoapFaultString(response.Content);
                    var faultDetails = AnalyzeSoapFault(response.Content);

                    camera.AddProtocolLog("ONVIF", "Gateway Config SOAP Fault",
                        $"SOAP fault setting gateway: {faultString}", ProtocolLogLevel.Warning);
                    camera.AddProtocolLog("ONVIF", "Gateway SOAP Fault Details", faultDetails, ProtocolLogLevel.Warning);
                    return false;
                }

                camera.AddProtocolLog("ONVIF", "Gateway Config Success",
                    $"✓ Gateway {gatewayIP} configured successfully", ProtocolLogLevel.Success);
                LogSoapResponse(camera, "SetNetworkDefaultGateway", response.Content);
                return true;
            }
            catch (Exception ex)
            {
                camera.AddProtocolLog("ONVIF", "Gateway Config Exception",
                    $"Exception setting gateway: {ex.GetType().Name} - {ex.Message}", ProtocolLogLevel.Warning);
                return false;
            }
        }

        /// <summary>
        /// Enhanced DNS configuration with detailed error handling
        /// </summary>
        private async Task<bool> SetDNSConfigurationAsync(Camera camera, CancellationToken cancellationToken = default)
        {
            try
            {
                // Validate DNS server formats
                var validationErrors = new List<string>();
                if (!string.IsNullOrEmpty(camera.NewDNS1) && !IPAddress.TryParse(camera.NewDNS1, out var _))
                {
                    validationErrors.Add($"Invalid DNS1 IP format: {camera.NewDNS1}");
                }

                if (!string.IsNullOrEmpty(camera.NewDNS2) && !IPAddress.TryParse(camera.NewDNS2, out var _))
                {
                    validationErrors.Add($"Invalid DNS2 IP format: {camera.NewDNS2}");
                }

                if (validationErrors.Any())
                {
                    camera.AddProtocolLog("ONVIF", "DNS Config Validation Error",
                        string.Join(", ", validationErrors), ProtocolLogLevel.Error);
                    return false;
                }

                var setDNSRequest = OnvifSoapTemplates.CreateSetDNSRequest(camera, _connection.Username, _connection.Password);
                LogSoapRequest(camera, "SetDNS", setDNSRequest);

                var response = await _connection.SendSoapToDeviceServiceAsync(setDNSRequest, OnvifUrl.SoapActions.SetDNS);

                if (!response.Success)
                {
                    camera.AddProtocolLog("ONVIF", "DNS Config Error",
                        $"Failed to set DNS: {response.StatusCode}", ProtocolLogLevel.Warning);
                    LogHttpResponseDetails(camera, "SetDNS", response);
                    return false;
                }

                if (OnvifSoapTemplates.IsSoapFault(response.Content))
                {
                    var faultString = OnvifSoapTemplates.ExtractSoapFaultString(response.Content);
                    var faultDetails = AnalyzeSoapFault(response.Content);

                    camera.AddProtocolLog("ONVIF", "DNS Config SOAP Fault",
                        $"SOAP fault setting DNS: {faultString}", ProtocolLogLevel.Warning);
                    camera.AddProtocolLog("ONVIF", "DNS SOAP Fault Details", faultDetails, ProtocolLogLevel.Warning);
                    return false;
                }

                camera.AddProtocolLog("ONVIF", "DNS Config Success",
                    $"✓ DNS servers configured successfully - Primary: {camera.NewDNS1 ?? "not set"}, Secondary: {camera.NewDNS2 ?? "not set"}",
                    ProtocolLogLevel.Success);
                LogSoapResponse(camera, "SetDNS", response.Content);
                return true;
            }
            catch (Exception ex)
            {
                camera.AddProtocolLog("ONVIF", "DNS Config Exception",
                    $"Exception setting DNS: {ex.GetType().Name} - {ex.Message}", ProtocolLogLevel.Warning);
                return false;
            }
        }

        #region Enhanced Logging and Analysis Methods

        /// <summary>
        /// Validates network configuration inputs with detailed error reporting
        /// </summary>
        private ValidationResult ValidateNetworkConfigurationInputs(Camera camera)
        {
            var errors = new List<string>();

            // Validate IP address
            if (string.IsNullOrEmpty(camera.NewIP))
            {
                errors.Add("Target IP address is required");
            }
            else if (!IPAddress.TryParse(camera.NewIP, out var _))
            {
                errors.Add($"Invalid IP address format: {camera.NewIP}");
            }

            // Validate subnet mask
            if (!string.IsNullOrEmpty(camera.NewMask))
            {
                if (!IPAddress.TryParse(camera.NewMask, out var maskIp))
                {
                    errors.Add($"Invalid subnet mask format: {camera.NewMask}");
                }
                else if (!IsValidSubnetMask(camera.NewMask))
                {
                    errors.Add($"Invalid subnet mask - must be consecutive 1s followed by 0s: {camera.NewMask}");
                }
            }

            // Validate gateway
            if (!string.IsNullOrEmpty(camera.NewGateway) && !IPAddress.TryParse(camera.NewGateway, out var _))
            {
                errors.Add($"Invalid gateway IP format: {camera.NewGateway}");
            }

            // Validate DNS servers
            if (!string.IsNullOrEmpty(camera.NewDNS1) && !IPAddress.TryParse(camera.NewDNS1, out var _))
            {
                errors.Add($"Invalid DNS1 IP format: {camera.NewDNS1}");
            }

            if (!string.IsNullOrEmpty(camera.NewDNS2) && !IPAddress.TryParse(camera.NewDNS2, out var _))
            {
                errors.Add($"Invalid DNS2 IP format: {camera.NewDNS2}");
            }

            return new ValidationResult(errors.Count == 0, errors);
        }

        /// <summary>
        /// Logs current network state before making changes
        /// </summary>
        private void LogCurrentNetworkState(Camera camera, string networkInterfacesResponse)
        {
            try
            {
                var currentNetworkInfo = OnvifSoapTemplates.ExtractNetworkInfo(networkInterfacesResponse);
                var logMessage = "Current network state: ";

                foreach (var info in currentNetworkInfo.Take(10)) // Log key network info
                {
                    logMessage += $"{info.Key}={info.Value}, ";
                }

                camera.AddProtocolLog("ONVIF", "Current Network State", logMessage.TrimEnd(',', ' '), ProtocolLogLevel.Info);
            }
            catch (Exception ex)
            {
                camera.AddProtocolLog("ONVIF", "Network State Logging Error",
                    $"Failed to extract current network state: {ex.Message}", ProtocolLogLevel.Warning);
            }
        }

        /// <summary>
        /// Logs SOAP request details for debugging
        /// </summary>
        private void LogSoapRequest(Camera camera, string operation, string soapRequest)
        {
            // Log truncated SOAP request for readability
            var truncatedRequest = soapRequest.Length > 1000
                ? soapRequest.Substring(0, 1000) + $"... (truncated, total length: {soapRequest.Length})"
                : soapRequest;

            camera.AddProtocolLog("ONVIF", $"{operation} SOAP Request", truncatedRequest, ProtocolLogLevel.Info);
        }

        /// <summary>
        /// Logs SOAP response details for debugging
        /// </summary>
        private void LogSoapResponse(Camera camera, string operation, string soapResponse)
        {
            // Log truncated SOAP response for readability
            var truncatedResponse = soapResponse.Length > 800
                ? soapResponse.Substring(0, 800) + $"... (truncated, total length: {soapResponse.Length})"
                : soapResponse;

            camera.AddProtocolLog("ONVIF", $"{operation} SOAP Response", truncatedResponse, ProtocolLogLevel.Info);
        }

        /// <summary>
        /// Logs detailed HTTP response information for troubleshooting
        /// </summary>
        private void LogHttpResponseDetails(Camera camera, string operation, (bool Success, string Content, HttpStatusCode StatusCode) response)
        {
            camera.AddProtocolLog("ONVIF", $"{operation} HTTP Details",
                $"Status: {response.StatusCode} ({(int)response.StatusCode}), Content-Length: {response.Content.Length}, Success: {response.Success}",
                ProtocolLogLevel.Error);

            if (!string.IsNullOrEmpty(response.Content) && response.Content.Length < 2000)
            {
                camera.AddProtocolLog("ONVIF", $"{operation} HTTP Response Body", response.Content, ProtocolLogLevel.Error);
            }
        }

        /// <summary>
        /// Analyzes SOAP fault for common issues and provides detailed information
        /// </summary>
        private string AnalyzeSoapFault(string soapResponse)
        {
            var analysis = new List<string>();

            try
            {
                if (soapResponse.Contains("ter:InvalidCredentials", StringComparison.OrdinalIgnoreCase))
                {
                    analysis.Add("Authentication failed - check username/password");
                }

                if (soapResponse.Contains("ter:NotAuthorized", StringComparison.OrdinalIgnoreCase))
                {
                    analysis.Add("User not authorized for this operation - check user permissions");
                }

                if (soapResponse.Contains("ter:ActionNotSupported", StringComparison.OrdinalIgnoreCase))
                {
                    analysis.Add("Operation not supported by this device");
                }

                if (soapResponse.Contains("ter:BadConfiguration", StringComparison.OrdinalIgnoreCase))
                {
                    analysis.Add("Invalid configuration parameters provided");
                }

                if (soapResponse.Contains("InterfaceToken", StringComparison.OrdinalIgnoreCase))
                {
                    analysis.Add("Interface token issue - the network interface may not exist or be accessible");
                }

                // Check for network-specific errors
                if (soapResponse.Contains("DHCP", StringComparison.OrdinalIgnoreCase))
                {
                    analysis.Add("DHCP-related configuration issue - camera may not support DHCP disable");
                }

                if (soapResponse.Contains("Manual", StringComparison.OrdinalIgnoreCase))
                {
                    analysis.Add("Manual network configuration issue - check IP address ranges and conflicts");
                }
            }
            catch (Exception ex)
            {
                analysis.Add($"Error analyzing SOAP fault: {ex.Message}");
            }

            return analysis.Any()
                ? $"SOAP Fault Analysis: {string.Join("; ", analysis)}"
                : "SOAP fault detected but no specific analysis available";
        }

        /// <summary>
        /// Provides troubleshooting guidance based on HTTP status codes
        /// </summary>
        private string GetTroubleshootingMessage(HttpStatusCode statusCode)
        {
            return statusCode switch
            {
                HttpStatusCode.Unauthorized => "HTTP 401: Check ONVIF credentials and ensure user has network configuration permissions",
                HttpStatusCode.Forbidden => "HTTP 403: User authenticated but lacks permissions for network configuration",
                HttpStatusCode.NotFound => "HTTP 404: ONVIF service endpoint not found - verify device supports ONVIF",
                HttpStatusCode.InternalServerError => "HTTP 500: Device internal error - check device logs or restart device",
                HttpStatusCode.BadRequest => "HTTP 400: Invalid request format - check SOAP message structure",
                HttpStatusCode.RequestTimeout => "HTTP 408: Request timeout - check network connectivity and device responsiveness",
                HttpStatusCode.ServiceUnavailable => "HTTP 503: Device service unavailable - device may be overloaded or restarting",
                _ => ""
            };
        }

        /// <summary>
        /// Generates comprehensive configuration summary with verification results
        /// </summary>
        private ConfigurationSummaryResult GenerateConfigurationSummary(Camera camera, AdditionalConfigResults additionalResults, TimeSpan totalTime, bool verificationSuccess)
        {
            var summary = new List<string> { "Network interface configuration: ✓ SUCCESS" };
            var allSuccessful = true;

            if (!verificationSuccess)
            {
                summary.Add("Configuration verification: ⚠️ PARTIAL");
                camera.AddProtocolLog("ONVIF", "Configuration Warning",
                    "Network configuration sent successfully but verification indicates DHCP may still be enabled. " +
                    "Some cameras take time to apply changes or may require a reboot.", ProtocolLogLevel.Warning);
            }
            else
            {
                summary.Add("Configuration verification: ✓ SUCCESS");
            }

            if (additionalResults.GatewayAttempted)
            {
                var status = additionalResults.GatewaySuccess ? "✓ SUCCESS" : "✗ FAILED";
                summary.Add($"Gateway configuration: {status}");
                if (!additionalResults.GatewaySuccess) allSuccessful = false;
            }

            if (additionalResults.DnsAttempted)
            {
                var status = additionalResults.DnsSuccess ? "✓ SUCCESS" : "✗ FAILED";
                summary.Add($"DNS configuration: {status}");
                if (!additionalResults.DnsSuccess) allSuccessful = false;
            }

            var summaryMessage = $"Enhanced Configuration Summary (Total time: {totalTime.TotalMilliseconds:F0}ms): {string.Join(", ", summary)}";

            // Provide additional guidance if DHCP is still enabled
            if (!verificationSuccess)
            {
                camera.AddProtocolLog("ONVIF", "DHCP Troubleshooting",
                    "If DHCP remains enabled: 1) Camera may need manual reboot, 2) Check if camera supports static IP, " +
                    "3) Try configuring via camera web interface, 4) Contact camera manufacturer for ONVIF compliance",
                    ProtocolLogLevel.Info);
            }

            camera.AddProtocolLog("ONVIF", "Configuration Summary", summaryMessage,
                allSuccessful && verificationSuccess ? ProtocolLogLevel.Success : ProtocolLogLevel.Warning);

            return new ConfigurationSummaryResult(allSuccessful && verificationSuccess,
                (!allSuccessful || !verificationSuccess) ? "Configuration completed with issues - see logs for details" : null);
        }

        /// <summary>
        /// Validates if a string is a valid subnet mask
        /// </summary>
        private static bool IsValidSubnetMask(string mask)
        {
            if (!IPAddress.TryParse(mask, out var ipAddress))
                return false;

            // Convert to binary and check if it's a valid subnet mask
            var bytes = ipAddress.GetAddressBytes();
            uint maskValue = (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);

            // Check if mask has consecutive 1s followed by consecutive 0s
            bool foundZero = false;
            for (int i = 31; i >= 0; i--)
            {
                bool bit = ((maskValue >> i) & 1) == 1;
                if (!bit)
                {
                    foundZero = true;
                }
                else if (foundZero)
                {
                    return false; // Found 1 after 0
                }
            }

            return true;
        }

        #endregion

        #region Helper Classes

        /// <summary>
        /// Validation result for input parameters
        /// </summary>
        private sealed class ValidationResult
        {
            public bool IsValid { get; }
            public IReadOnlyList<string> Errors { get; }

            public ValidationResult(bool isValid, IEnumerable<string> errors)
            {
                IsValid = isValid;
                Errors = errors.ToList().AsReadOnly();
            }
        }

        /// <summary>
        /// Results of additional network configuration operations
        /// </summary>
        private sealed class AdditionalConfigResults
        {
            public bool GatewayAttempted { get; set; }
            public bool GatewaySuccess { get; set; }
            public bool DnsAttempted { get; set; }
            public bool DnsSuccess { get; set; }
        }

        /// <summary>
        /// Overall configuration summary result
        /// </summary>
        private sealed class ConfigurationSummaryResult
        {
            public bool Success { get; }
            public string? ErrorMessage { get; }

            public ConfigurationSummaryResult(bool success, string? errorMessage = null)
            {
                Success = success;
                ErrorMessage = errorMessage;
            }
        }

        #endregion

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }
}