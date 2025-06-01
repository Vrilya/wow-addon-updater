using System;
using System.Net.Http;

namespace WowAddonUpdater.Services
{
    public class HttpClientService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private bool _disposed = false;
        private const string DEFAULT_USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";

        public HttpClientService(string customUserAgent = null, bool useCustom = false)
        {
            _httpClient = new HttpClient();

            string userAgent = DEFAULT_USER_AGENT; // standardvärde

            if (useCustom && !string.IsNullOrWhiteSpace(customUserAgent))
            {
                userAgent = customUserAgent.Trim();
            }

            _httpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        public HttpClient Client => _httpClient;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _httpClient?.Dispose();
            }
            _disposed = true;
        }
    }
}