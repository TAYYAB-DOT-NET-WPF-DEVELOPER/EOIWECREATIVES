using Newtonsoft.Json;
using Serilog;
using System;
using System.Configuration;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace WeCreatives_EOI.Services
{
    public class VendorOrderStatusService
    {
        private readonly HttpClient _httpClient;
        private string? _cachedToken;
        private DateTime _tokenExpiryTime = DateTime.MinValue;
        private DateTime _authBackoffUntil = DateTime.MinValue;
        private readonly object _tokenLock = new object();

        private int _webOrderApiErrorCount = 0;
        private DateTime _webOrderApiBackoffUntil = DateTime.MinValue;
        private readonly object _webOrderApiLock = new object();

        public bool IsWebOrderApiInBackoff(out DateTime backoffUntil)
        {
            lock (_webOrderApiLock)
            {
                backoffUntil = _webOrderApiBackoffUntil;
                return DateTime.UtcNow < _webOrderApiBackoffUntil;
            }
        }

        private void RecordWebOrderApiSuccess()
        {
            lock (_webOrderApiLock)
            {
                if (_webOrderApiErrorCount > 0 || _webOrderApiBackoffUntil != DateTime.MinValue)
                {
                    Log.Information("Web Order API call succeeded. Resetting API error count and clearing backoff.");
                }
                _webOrderApiErrorCount = 0;
                _webOrderApiBackoffUntil = DateTime.MinValue;
            }
        }

        private void RecordWebOrderApiFailure(string apiName, string failureReason)
        {
            lock (_webOrderApiLock)
            {
                _webOrderApiErrorCount++;
                Log.Warning("Web Order API '{ApiName}' failed (Reason: {Reason}). Consecutive error count: {ErrorCount}", apiName, failureReason, _webOrderApiErrorCount);

                if (_webOrderApiErrorCount >= 2)
                {
                    _webOrderApiBackoffUntil = DateTime.UtcNow.AddSeconds(30);
                    Log.Error("Web Order API failed {ErrorCount} consecutive times. Pausing Web Order API calls for 30 seconds until {BackoffUntil}",
                        _webOrderApiErrorCount, _webOrderApiBackoffUntil.ToLocalTime());
                }
            }
        }

        // Order Status API endpoints & credentials (papajohn-web-dev)
        private static string VendorAuthUrl => ConfigurationManager.AppSettings["VendorAuthUrl"]
            ?? "https://www.papajohns.com.pk/api/vendor/auth";

        private static string VendorStatusUrl => ConfigurationManager.AppSettings["VendorStatusUrl"]
            ?? "https://www.papajohns.com.pk/api/pos/webhook/order-status";

        private static string VendorApiKey => ConfigurationManager.AppSettings["VendorApiKey"]
            ?? "93uisySuw3E0ugdAcaLnYo6amg9ud9Vs";

        private static string VendorEmail => ConfigurationManager.AppSettings["VendorEmail"]
            ?? "umair.zahid@papajohns-group.co.uk";

        private static string VendorPassword => ConfigurationManager.AppSettings["VendorPassword"]
            ?? "Zv3v@akovqcqmkc9";

        // Rider Tracking API endpoints & credentials (lamda-hds-stg-lm-3)
        private static string VendorDriverUrl => ConfigurationManager.AppSettings["VendorDriverUrl"]
            ?? "https://rider-app.papajohns.com.pk/api/vendor/driver";

        private static string RiderApiKey => ConfigurationManager.AppSettings["RiderApiKey"]
            ?? "eyiC6S2xaRtwfudqsXpSvjqX";

        public VendorOrderStatusService(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<string?> GetVendorAccessTokenAsync()
        {
            lock (_tokenLock)
            {
                if (!string.IsNullOrWhiteSpace(_cachedToken) && DateTime.UtcNow < _tokenExpiryTime)
                {
                    return _cachedToken;
                }

                if (DateTime.UtcNow < _authBackoffUntil)
                {
                    Log.Warning("Skipping Vendor Auth request due to active rate-limit backoff until {BackoffUntil}", _authBackoffUntil.ToLocalTime());
                    return _cachedToken;
                }
            }

            if (IsWebOrderApiInBackoff(out DateTime backoffUntil))
            {
                Log.Warning("Skipping Vendor Auth request because Web Order API is paused due to consecutive errors until {BackoffUntil}", backoffUntil.ToLocalTime());
                return _cachedToken;
            }

            try
            {
                var requestBody = new
                {
                    email_address = VendorEmail,
                    password = VendorPassword
                };

                using (var request = new HttpRequestMessage(HttpMethod.Post, VendorAuthUrl))
                {
                    request.Headers.TryAddWithoutValidation("User-Agent", "PostmanRuntime/7.56.1");
                    request.Headers.TryAddWithoutValidation("Accept", "*/*");
                    request.Headers.TryAddWithoutValidation("x-api-key", VendorApiKey);
                    request.Content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

                    HttpResponseMessage response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        string jsonStr = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        using var doc = JsonDocument.Parse(jsonStr);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("data", out var dataElement) &&
                            dataElement.TryGetProperty("access_token", out var tokenProp))
                        {
                            string token = tokenProp.GetString() ?? string.Empty;
                            int expiresIn = 3600;
                            if (dataElement.TryGetProperty("expires_in", out var expiresProp) && expiresProp.TryGetInt32(out int exp))
                            {
                                expiresIn = exp;
                            }

                            lock (_tokenLock)
                            {
                                _cachedToken = token;
                                _tokenExpiryTime = DateTime.UtcNow.AddSeconds(expiresIn - 60);
                                _authBackoffUntil = DateTime.MinValue;
                            }

                            RecordWebOrderApiSuccess();
                            Log.Information("Successfully retrieved Vendor Access Token.");
                            return token;
                        }
                    }

                    lock (_tokenLock)
                    {
                        // On error or rate-limit (429), back off for 2 minutes to prevent hammering /vendor/auth
                        _authBackoffUntil = DateTime.UtcNow.AddMinutes(2);
                    }
                    Log.Error("Vendor auth failed with HTTP status code {StatusCode}. Backing off until {BackoffUntil}", response.StatusCode, _authBackoffUntil.ToLocalTime());
                    RecordWebOrderApiFailure("VendorAuth", $"HTTP Status {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                lock (_tokenLock)
                {
                    _authBackoffUntil = DateTime.UtcNow.AddMinutes(2);
                }
                Log.Error(ex, "Exception occurred while obtaining Vendor Access Token.");
                RecordWebOrderApiFailure("VendorAuth", ex.Message);
            }

            return _cachedToken;
        }

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _sentOrderStatusCache =
            new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        public async Task<bool> UpdateOrderStatusAsync(string orderId, string statusCode)
        {
            if (string.IsNullOrWhiteSpace(orderId) || string.IsNullOrWhiteSpace(statusCode))
            {
                Log.Warning("UpdateOrderStatusAsync called with empty orderId or statusCode.");
                return false;
            }

            string cacheKey = $"{orderId.Trim().ToUpperInvariant()}:{statusCode.Trim().ToLowerInvariant()}";
            if (_sentOrderStatusCache.ContainsKey(cacheKey))
            {
                Log.Debug("Skipping duplicate status update for OrderId '{OrderId}' with status '{StatusCode}' as it was already sent.", orderId, statusCode);
                return true;
            }

            if (IsWebOrderApiInBackoff(out DateTime backoffUntil))
            {
                Log.Warning("Skipping UpdateOrderStatusAsync for OrderId '{OrderId}' because Web Order API is paused due to consecutive errors until {BackoffUntil}", orderId, backoffUntil.ToLocalTime());
                return false;
            }

            try
            {
                string? token = await GetVendorAccessTokenAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(token))
                {
                    Log.Error("Cannot update vendor order status for OrderId {OrderId} because token is null.", orderId);
                    RecordWebOrderApiFailure("UpdateOrderStatus", "Token is null");
                    return false;
                }

                var requestBody = new
                {
                    orderId = orderId.Trim(),
                    statusCode = statusCode.Trim()
                };

                using (var request = new HttpRequestMessage(HttpMethod.Put, VendorStatusUrl))
                {
                    request.Headers.TryAddWithoutValidation("User-Agent", "PostmanRuntime/7.56.1");
                    request.Headers.TryAddWithoutValidation("Accept", "*/*");
                    request.Headers.TryAddWithoutValidation("x-api-key", VendorApiKey);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    request.Content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

                    HttpResponseMessage response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                    string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        _sentOrderStatusCache.TryAdd(cacheKey, 0);
                        RecordWebOrderApiSuccess();
                        Log.Information("Successfully updated Vendor Order Status for OrderId {OrderId} to '{StatusCode}'. Response: {Response}", orderId, statusCode, responseBody);
                        return true;
                    }
                    else
                    {
                        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        {
                            lock (_tokenLock) { _cachedToken = null; }
                        }
                        Log.Error("Failed to update Vendor Order Status for OrderId {OrderId} to '{StatusCode}'. Status: {Status}, Response: {Response}", orderId, statusCode, response.StatusCode, responseBody);
                        RecordWebOrderApiFailure("UpdateOrderStatus", $"HTTP Status {response.StatusCode}");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Exception while updating Vendor Order Status for OrderId {OrderId}", orderId);
                RecordWebOrderApiFailure("UpdateOrderStatus", ex.Message);
                return false;
            }
        }

        public async Task<bool> CreateDriverAsync(object driverData)
        {
            if (driverData == null)
            {
                Log.Warning("CreateDriverAsync called with null driverData.");
                return false;
            }

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, VendorDriverUrl))
                {
                    request.Headers.TryAddWithoutValidation("User-Agent", "PostmanRuntime/7.56.1");
                    request.Headers.TryAddWithoutValidation("Accept", "*/*");
                    request.Headers.TryAddWithoutValidation("vendor-api-key", RiderApiKey);

                    string json = JsonConvert.SerializeObject(driverData);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    HttpResponseMessage response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                    string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        Log.Information("Successfully sent Driver data to Rider API ({Url}). Response: {Response}", VendorDriverUrl, responseBody);
                        return true;
                    }
                    else
                    {
                        Log.Error("Failed to send Driver data to Rider API ({Url}). Status: {Status}, Response: {Response}", VendorDriverUrl, response.StatusCode, responseBody);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Exception while sending Driver data to Rider API.");
                return false;
            }
        }

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _driverClockCache =
            new System.Collections.Concurrent.ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _forbiddenDriverCache =
            new System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        public async Task<bool> ClockDriverAsync(string driverPosId, int clockState)
        {
            if (string.IsNullOrWhiteSpace(driverPosId))
            {
                Log.Warning("ClockDriverAsync called with empty driverPosId.");
                return false;
            }

            string cleanDriverId = driverPosId.Trim();
            if (_driverClockCache.TryGetValue(cleanDriverId, out int lastState) && lastState == clockState)
            {
                Log.Debug("Skipping duplicate driver clock call for Driver POS ID '{DriverPosId}' with clockState={ClockState} as it was already sent successfully.", cleanDriverId, clockState);
                return true;
            }

            if (_forbiddenDriverCache.TryGetValue(cleanDriverId, out DateTime forbiddenUntil) && DateTime.UtcNow < forbiddenUntil)
            {
                Log.Debug("Skipping driver clock call for Driver POS ID '{DriverPosId}' because previous attempt failed with Forbidden/mismatch until {Until}", cleanDriverId, forbiddenUntil.ToLocalTime());
                return false;
            }

            try
            {
                string clockUrl = $"{VendorDriverUrl.TrimEnd('/')}/clock/{Uri.EscapeDataString(cleanDriverId)}";

                var requestBody = new
                {
                    clock = clockState
                };

                using (var request = new HttpRequestMessage(new HttpMethod("PATCH"), clockUrl))
                {
                    request.Headers.TryAddWithoutValidation("User-Agent", "PostmanRuntime/7.56.1");
                    request.Headers.TryAddWithoutValidation("Accept", "*/*");
                    request.Headers.TryAddWithoutValidation("vendor-api-key", RiderApiKey);

                    string json = JsonConvert.SerializeObject(requestBody);
                    request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                    HttpResponseMessage response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                    string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        _driverClockCache[cleanDriverId] = clockState;
                        _forbiddenDriverCache.TryRemove(cleanDriverId, out _);
                        Log.Information("Successfully updated Driver Clock state for Driver POS ID {DriverPosId} to clock={ClockState}. Response: {Response}", driverPosId, clockState, responseBody);
                        return true;
                    }
                    else
                    {
                        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden || (responseBody != null && responseBody.Contains("driver_pos_code not match")))
                        {
                            _forbiddenDriverCache[cleanDriverId] = DateTime.UtcNow.AddMinutes(15);
                            Log.Warning("Driver POS ID {DriverPosId} returned Forbidden/mismatch. Pausing clock attempts for 15 minutes. Status: {Status}, Response: {Response}", driverPosId, response.StatusCode, responseBody);
                        }
                        else
                        {
                            Log.Error("Failed to update Driver Clock state for Driver POS ID {DriverPosId} to clock={ClockState}. Status: {Status}, Response: {Response}", driverPosId, clockState, response.StatusCode, responseBody);
                        }
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Exception while updating Driver Clock state for Driver POS ID {DriverPosId}", driverPosId);
                return false;
            }
        }

        private static string VendorDriverAssignmentUrl => ConfigurationManager.AppSettings["VendorDriverAssignmentUrl"]
            ?? "https://www.papajohns.com.pk/api/pos/webhook/driver-order-assignment";

        public async Task<bool> AssignDriverOrdersAsync(string driverPosCode, System.Collections.Generic.List<object> orderIds)
        {
            if (string.IsNullOrWhiteSpace(driverPosCode) || orderIds == null || orderIds.Count == 0)
            {
                Log.Warning("AssignDriverOrdersAsync called with empty driverPosCode or orderIds.");
                return false;
            }

            try
            {
                string? token = await GetVendorAccessTokenAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(token))
                {
                    Log.Error("Cannot assign driver orders for DriverPosCode {DriverPosCode} because token is null.", driverPosCode);
                    return false;
                }

                var requestBody = new
                {
                    driver_pos_code = driverPosCode.Trim(),
                    order = orderIds
                };

                using (var request = new HttpRequestMessage(HttpMethod.Put, VendorDriverAssignmentUrl))
                {
                    request.Headers.TryAddWithoutValidation("User-Agent", "PostmanRuntime/7.56.1");
                    request.Headers.TryAddWithoutValidation("Accept", "*/*");
                    request.Headers.TryAddWithoutValidation("x-api-key", VendorApiKey);
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    request.Content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

                    HttpResponseMessage response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                    string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        Log.Information("Successfully assigned orders to driver {DriverPosCode}. Response: {Response}", driverPosCode, responseBody);
                        return true;
                    }
                    else
                    {
                        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                        {
                            lock (_tokenLock) { _cachedToken = null; }
                        }
                        Log.Error("Failed to assign orders to driver {DriverPosCode}. Status: {Status}, Response: {Response}", driverPosCode, response.StatusCode, responseBody);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Exception while assigning orders to driver {DriverPosCode}", driverPosCode);
                return false;
            }
        }
    }
}
