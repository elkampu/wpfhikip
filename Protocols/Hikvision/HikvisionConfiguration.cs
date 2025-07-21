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
    class HikvisionConfiguration
    {
    }

    public class HikvisionApiClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public HikvisionApiClient(string ipAddress, string username, string password, bool useHttps = false)
        {
            var protocol = useHttps ? "https" : "http";
            _baseUrl = $"{protocol}://{ipAddress}";

            var credCache = new CredentialCache();
            credCache.Add(new Uri(_baseUrl), "Digest", new NetworkCredential(username, password));

            _httpClient = new HttpClient(new HttpClientHandler { Credentials = credCache });
        }

        /// <summary>
        /// Performs GET request to retrieve current XML configuration
        /// </summary>
        public async Task<(bool Success, string XmlContent, string ErrorMessage)> GetConfigurationAsync(string endpoint)
        {
            try
            {
                var url = HikvisionUrl.UrlBuilders.BuildGetUrl(_baseUrl.Replace("http://", "").Replace("https://", ""), endpoint);
                var response = await _httpClient.GetAsync(url);

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
                var url = HikvisionUrl.UrlBuilders.BuildPutUrl(_baseUrl.Replace("http://", "").Replace("https://", ""), endpoint);
                var content = new StringContent(xmlContent, Encoding.UTF8, ContentTypes.Xml);
                var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = content };

                var response = await _httpClient.SendAsync(request);

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

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}