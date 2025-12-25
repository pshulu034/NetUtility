using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace EthernetUtility
{
    public class HttpClientEx : IDisposable
    {
        private readonly System.Net.Http.HttpClient _client;
        private readonly JsonSerializerOptions _jsonOptions;

        public HttpClientEx(string baseUrl = "", int timeoutSeconds = 10, Dictionary<string, string> headers = null)
        {
            _client = new System.Net.Http.HttpClient();
            if (!string.IsNullOrEmpty(baseUrl))
            {
                _client.BaseAddress = new Uri(baseUrl);
            }
            _client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    _client.DefaultRequestHeaders.Add(header.Key, header.Value);
                }
            }

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
        }

        // Allow injecting a custom HttpClient for testing or advanced usage
        public HttpClientEx(System.Net.Http.HttpClient client)
        {
            _client = client;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
        }

        public void SetHeader(string key, string value)
        {
            if (_client.DefaultRequestHeaders.Contains(key))
            {
                _client.DefaultRequestHeaders.Remove(key);
            }
            _client.DefaultRequestHeaders.Add(key, value);
        }

        public async Task<T> GetAsync<T>(string endpoint, Dictionary<string, string> queryParams = null)
        {
            string url = BuildUrl(endpoint, queryParams);
            try
            {
                var response = await _client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<T>(_jsonOptions);
            }
            catch (Exception ex)
            {
                // Log error here if needed
                throw new HttpRequestException($"Error fetching data from {url}: {ex.Message}", ex);
            }
        }

        public async Task<TResponse> PostAsync<TRequest, TResponse>(string endpoint, TRequest data)
        {
            try
            {
                var response = await _client.PostAsJsonAsync(endpoint, data, _jsonOptions);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions);
            }
            catch (Exception ex)
            {
                throw new HttpRequestException($"Error posting data to {endpoint}: {ex.Message}", ex);
            }
        }

        public async Task<TResponse> PutAsync<TRequest, TResponse>(string endpoint, TRequest data)
        {
            try
            {
                var response = await _client.PutAsJsonAsync(endpoint, data, _jsonOptions);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<TResponse>(_jsonOptions);
            }
            catch (Exception ex)
            {
                throw new HttpRequestException($"Error putting data to {endpoint}: {ex.Message}", ex);
            }
        }

        public async Task DeleteAsync(string endpoint)
        {
            try
            {
                var response = await _client.DeleteAsync(endpoint);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                throw new HttpRequestException($"Error deleting resource at {endpoint}: {ex.Message}", ex);
            }
        }

        private string BuildUrl(string endpoint, Dictionary<string, string> queryParams)
        {
            if (queryParams == null || queryParams.Count == 0)
            {
                return endpoint;
            }

            var builder = new StringBuilder(endpoint);
            builder.Append("?");
            foreach (var kvp in queryParams)
            {
                builder.Append($"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}&");
            }
            return builder.ToString().TrimEnd('&');
        }

        public void Dispose()
        {
            _client?.Dispose();
        }
    }
}
