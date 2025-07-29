using System.Net;
using System.Net.Http;
using System.Text;

using wpfhikip.Models;

namespace wpfhikip.Protocols.Hikvision
{
    public class HikvisionConfiguration : IDisposable
    {
        private readonly HikvisionConnection _connection;
        private HttpClient? _httpClient;
        private bool _disposed = false;

        public HikvisionConfiguration(HikvisionConnection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        /// <summary>
        /// Performs GET request to retrieve current XML configuration
        /// </summary>
        public async Task<(bool Success, string XmlContent, string ErrorMessage)> GetConfigurationAsync(string endpoint)
        {
            try
            {
                EnsureHttpClient();

                var url = HikvisionUrl.UrlBuilders.BuildGetUrl(_connection.IpAddress, endpoint, _connection.Port == 443, _connection.Port);
                var response = await _httpClient!.GetAsync(url);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    return (true, content, string.Empty);
                }
                else if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return (false, string.Empty, StatusMessages.LoginFailed);
                }
                else
                {
                    return (false, string.Empty, $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                return (false, string.Empty, ex.Message);
            }
        }

        /// <summary>
        /// Performs PUT request with modified XML content
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> SetConfigurationAsync(string endpoint, string xmlContent)
        {
            try
            {
                EnsureHttpClient();

                var url = HikvisionUrl.UrlBuilders.BuildPutUrl(_connection.IpAddress, endpoint, _connection.Port == 443, _connection.Port);
                var content = new StringContent(xmlContent, Encoding.UTF8, ContentTypes.Xml);
                var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = content };

                var response = await _httpClient!.SendAsync(request);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return (true, string.Empty);
                }
                else if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return (false, StatusMessages.LoginFailed);
                }
                else
                {
                    return (false, $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Performs the full GET-modify-PUT workflow using Camera object
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> UpdateConfigurationAsync(string endpoint, Camera camera)
        {
            // Step 1: Get current configuration
            var (getSuccess, currentXml, getError) = await GetConfigurationAsync(endpoint);
            if (!getSuccess)
            {
                camera.AddProtocolLog("Hikvision", "GET Config Error",
                    $"Failed to retrieve current configuration: {getError}", ProtocolLogLevel.Error);
                return (false, $"Failed to retrieve current configuration: {getError}");
            }

            // Log the current configuration (for debugging)
            camera.AddProtocolLog("Hikvision", "GET Config",
                $"Retrieved current XML configuration (length: {currentXml.Length})", ProtocolLogLevel.Info);

            // Log first 200 chars of XML for debugging
            var xmlPreview = currentXml.Length > 200 ? currentXml.Substring(0, 200) + "..." : currentXml;
            camera.AddProtocolLog("Hikvision", "XML Preview",
                $"Current XML: {xmlPreview}", ProtocolLogLevel.Info);

            // Step 2: Check if configuration actually needs updating
            if (!HikvisionXmlTemplates.HasConfigurationChanged(currentXml, camera, endpoint))
            {
                camera.AddProtocolLog("Hikvision", "Config Check",
                    "Configuration is already up to date", ProtocolLogLevel.Info);
                return (true, "Configuration is already up to date");
            }

            camera.AddProtocolLog("Hikvision", "Config Check",
                "Configuration changes detected, proceeding with update", ProtocolLogLevel.Info);

            // Step 3: Modify XML with new values
            try
            {
                var modifiedXml = HikvisionXmlTemplates.CreatePutXmlFromGetResponse(currentXml, camera, endpoint);

                // Log the modified XML (for debugging)
                camera.AddProtocolLog("Hikvision", "XML Modify",
                    $"Modified XML created (length: {modifiedXml.Length})", ProtocolLogLevel.Info);

                // Log first 200 chars of modified XML for debugging
                var modifiedXmlPreview = modifiedXml.Length > 200 ? modifiedXml.Substring(0, 200) + "..." : modifiedXml;
                camera.AddProtocolLog("Hikvision", "Modified XML Preview",
                    $"Modified XML: {modifiedXmlPreview}", ProtocolLogLevel.Info);

                // Step 4: Validate modified XML
                if (!HikvisionXmlTemplates.ValidateXml(modifiedXml))
                {
                    camera.AddProtocolLog("Hikvision", "XML Validation",
                        "Generated XML is invalid", ProtocolLogLevel.Error);
                    return (false, "Generated XML is invalid");
                }

                camera.AddProtocolLog("Hikvision", "XML Validation",
                    "XML validation successful", ProtocolLogLevel.Success);

                // Step 5: Send PUT request
                var url = HikvisionUrl.UrlBuilders.BuildPutUrl(_connection.IpAddress, endpoint, _connection.Port == 443, _connection.Port);
                camera.AddProtocolLog("Hikvision", "PUT Request",
                    $"Sending PUT request to {url}", ProtocolLogLevel.Info);

                var (putSuccess, putError) = await SetConfigurationAsync(endpoint, modifiedXml);

                camera.AddProtocolLog("Hikvision", "PUT Response",
                    putSuccess ? "PUT request successful" : $"PUT request failed: {putError}",
                    putSuccess ? ProtocolLogLevel.Success : ProtocolLogLevel.Error);

                return (putSuccess, putError);
            }
            catch (Exception ex)
            {
                var errorMsg = $"Failed to modify XML template: {ex.Message}";
                camera.AddProtocolLog("Hikvision", "XML Error", errorMsg, ProtocolLogLevel.Error);
                return (false, errorMsg);
            }
        }

        /// <summary>
        /// Updates network settings on the camera and reboots to apply changes
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> UpdateNetworkSettingsAsync(Camera camera)
        {
            // Step 1: Update network configuration
            var (configSuccess, configError) = await UpdateConfigurationAsync(HikvisionUrl.NetworkInterfaceIpAddress, camera);

            if (!configSuccess)
            {
                return (false, configError);
            }

            // Step 2: Reboot camera to apply network changes
            camera.AddProtocolLog("Hikvision", "Reboot",
                "Network configuration sent successfully, rebooting camera to apply changes", ProtocolLogLevel.Info);

            try
            {
                // Wait a moment for the configuration to be processed
                await Task.Delay(2000);

                var (rebootSuccess, rebootError) = await RebootCameraAsync();

                if (rebootSuccess)
                {
                    camera.AddProtocolLog("Hikvision", "Reboot",
                        "Camera reboot command sent successfully", ProtocolLogLevel.Success);

                    camera.AddProtocolLog("Hikvision", "Network Config",
                        "Network configuration complete. Camera will reboot and apply new IP settings.", ProtocolLogLevel.Success);
                }
                else
                {
                    camera.AddProtocolLog("Hikvision", "Reboot Warning",
                        $"Network config sent but reboot failed: {rebootError}. You may need to manually reboot the camera.", ProtocolLogLevel.Warning);
                }

                // Return success regardless of reboot result since the config was sent successfully
                return (true, "Network configuration sent and reboot initiated");
            }
            catch (Exception ex)
            {
                camera.AddProtocolLog("Hikvision", "Reboot Warning",
                    $"Network config sent but reboot failed: {ex.Message}. You may need to manually reboot the camera.", ProtocolLogLevel.Warning);

                // Return success since the config was sent successfully
                return (true, "Network configuration sent but reboot failed");
            }
        }

        /// <summary>
        /// Updates NTP server settings on the camera
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> UpdateNtpSettingsAsync(Camera camera)
        {
            return await UpdateConfigurationAsync(HikvisionUrl.NtpServers, camera);
        }

        /// <summary>
        /// Updates system time settings on the camera
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> UpdateTimeSettingsAsync(Camera camera)
        {
            return await UpdateConfigurationAsync(HikvisionUrl.SystemTime, camera);
        }

        /// <summary>
        /// Reboots the camera
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> RebootCameraAsync()
        {
            try
            {
                EnsureHttpClient();

                var url = HikvisionUrl.UrlBuilders.BuildPutUrl(_connection.IpAddress, HikvisionUrl.SystemReboot, _connection.Port == 443, _connection.Port);
                var request = new HttpRequestMessage(HttpMethod.Put, url);

                var response = await _httpClient!.SendAsync(request);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return (true, StatusMessages.Rebooting);
                }
                else if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    return (false, StatusMessages.LoginFailed);
                }
                else
                {
                    return (false, StatusMessages.RebootError);
                }
            }
            catch (Exception ex)
            {
                return (false, $"{StatusMessages.RebootError}: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets device information from the camera
        /// </summary>
        public async Task<(bool Success, Dictionary<string, string> DeviceInfo, string ErrorMessage)> GetDeviceInfoAsync()
        {
            var (success, xmlContent, errorMessage) = await GetConfigurationAsync(HikvisionUrl.DeviceInfo);
            if (!success)
            {
                return (false, new Dictionary<string, string>(), errorMessage);
            }

            var deviceInfo = HikvisionXmlTemplates.ParseResponseXml(xmlContent);
            return (true, deviceInfo, string.Empty);
        }

        /// <summary>
        /// Gets system capabilities from the camera
        /// </summary>
        public async Task<(bool Success, Dictionary<string, string> Capabilities, string ErrorMessage)> GetSystemCapabilitiesAsync()
        {
            var (success, xmlContent, errorMessage) = await GetConfigurationAsync(HikvisionUrl.SystemCapabilities);
            if (!success)
            {
                return (false, new Dictionary<string, string>(), errorMessage);
            }

            var capabilities = HikvisionXmlTemplates.ParseResponseXml(xmlContent);
            return (true, capabilities, string.Empty);
        }

        private void EnsureHttpClient()
        {
            if (_httpClient == null)
            {
                _httpClient = _connection.CreateAuthenticatedHttpClient();
            }
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
}