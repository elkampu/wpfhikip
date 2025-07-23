using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

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

                var url = HikvisionUrl.UrlBuilders.BuildGetUrl(_connection.IpAddress, endpoint, _connection.Port == 443);
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

                var url = HikvisionUrl.UrlBuilders.BuildPutUrl(_connection.IpAddress, endpoint, _connection.Port == 443);
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
                return (false, $"Failed to retrieve current configuration: {getError}");
            }

            // Step 2: Check if configuration actually needs updating
            if (!HikvisionXmlTemplates.HasConfigurationChanged(currentXml, camera, endpoint))
            {
                return (true, "Configuration is already up to date");
            }

            // Step 3: Modify XML with new values
            try
            {
                var modifiedXml = HikvisionXmlTemplates.CreatePutXmlFromGetResponse(currentXml, camera, endpoint);

                // Step 4: Validate modified XML
                if (!HikvisionXmlTemplates.ValidateXml(modifiedXml))
                {
                    return (false, "Generated XML is invalid");
                }

                // Step 5: Send PUT request
                var (putSuccess, putError) = await SetConfigurationAsync(endpoint, modifiedXml);
                return (putSuccess, putError);
            }
            catch (Exception ex)
            {
                return (false, $"Failed to modify XML template: {ex.Message}");
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

        /// <summary>
        /// Updates network settings on the camera
        /// </summary>
        public async Task<(bool Success, string ErrorMessage)> UpdateNetworkSettingsAsync(Camera camera)
        {
            return await UpdateConfigurationAsync(HikvisionUrl.NetworkInterfaceIpAddress, camera);
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

                var url = HikvisionUrl.UrlBuilders.BuildPutUrl(_connection.IpAddress, HikvisionUrl.SystemReboot, _connection.Port == 443);
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