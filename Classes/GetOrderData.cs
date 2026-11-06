using Newtonsoft.Json;
using Serilog;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using WeCreatives_EOI.Services;

namespace WeCreatives_EOI.Classes
{
    public class GetOrderData
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly TokenManager _tokenmanager;
        private readonly HttpClient _httpClient;

        public GetOrderData(IServiceProvider serviceProvider, TokenManager tokenmanager, HttpClient httpClient)
        {
            _serviceProvider = serviceProvider;
            _tokenmanager = tokenmanager;
            _httpClient = httpClient;
        }

        public async Task<Root?> GetOrderDatafororder(string orderId, int snum)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(orderId))
                {
                    Log.Warning("GetOrderDatafororder called with null or empty orderId.");
                    return null;
                }

                string requestUri = $"https://api.hubrise.com/v1/location/orders/{orderId}";
                string token = await _tokenmanager.GetAccessToken(snum).ConfigureAwait(false);

                using (var httpRequest = new HttpRequestMessage(HttpMethod.Get, requestUri))
                {
                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        httpRequest.Headers.TryAddWithoutValidation("X-Access-Token", token);
                    }

                    HttpResponseMessage response = await _httpClient.SendAsync(httpRequest).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        string content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return JsonConvert.DeserializeObject<Root>(content);
                    }
                    else
                    {
                        Log.Warning("HubRise GetOrderData failed for OrderId: {OrderId}, Status: {Status}", orderId, response.StatusCode);
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error fetching HubRise order data for OrderId: {OrderId}", orderId);
                return null;
            }
        }
    }
}

