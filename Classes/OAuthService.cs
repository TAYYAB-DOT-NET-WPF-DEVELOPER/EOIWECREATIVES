using Newtonsoft.Json;
using Serilog;
using System;
using System.Configuration;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace WeCreatives_EOI.Classes
{
    public static class OAuthService
    {
        private static readonly HttpClient SharedClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public static async Task<string?> GetAccessTokenAsync(HttpClient? httpClient = null)
        {
            try
            {
                HttpClient client = httpClient ?? SharedClient;
                string tokenEndpoint = "https://manager.hubrise.com/oauth2/v1/token";

                string authCode = ConfigurationManager.AppSettings["AuthCode"] ?? string.Empty;
                string clientId = ConfigurationManager.AppSettings["ClientId"] ?? string.Empty;
                string clientSecret = ConfigurationManager.AppSettings["ClientSecret"] ?? string.Empty;

                if (string.IsNullOrWhiteSpace(authCode) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
                {
                    Log.Error("OAuth configuration missing: AuthCode, ClientId, or ClientSecret is empty.");
                    return null;
                }

                var requestBody = new
                {
                    code = authCode,
                    client_id = clientId,
                    client_secret = clientSecret
                };

                var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync(tokenEndpoint, content).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    string jsonString = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var jsonDocument = JsonDocument.Parse(jsonString);
                    if (jsonDocument.RootElement.TryGetProperty("access_token", out var tokenProp))
                    {
                        return tokenProp.GetString();
                    }
                }
                else
                {
                    Log.Error("OAuthService failed to fetch token. HTTP Status: {StatusCode}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Exception in OAuthService.GetAccessTokenAsync");
            }
            return null;
        }

        [Obsolete("Use GetAccessTokenAsync instead.")]
        public static string? GetAccessToken()
        {
            return GetAccessTokenAsync().GetAwaiter().GetResult();
        }
    }
}

