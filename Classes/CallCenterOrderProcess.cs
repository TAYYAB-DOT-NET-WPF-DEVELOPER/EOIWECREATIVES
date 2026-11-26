using PixelPointPOS;
using Serilog;
using System;
using System.Data.Odbc;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace WeCreatives_EOI.Classes
{
    public static class CallCenterOrderProcess
    {
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(120)
        };
        private static readonly ConcurrentDictionary<string, byte> _sentStatusCache = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        private const string NewCallCenterStatusUrl = "https://pjcrescent.com/api/HQEOI/CCORDERSTATUS";
        private const string OldCallCenterStatusUrl = "https://api.pjcrescent.com/api/public/orders/status";

        /// <summary>
        /// Posts status update for Call Center orders to both endpoints:
        /// 1. Updated API: https://pjcrescent.com/api/HQEOI/CCORDERSTATUS (POST)
        /// 2. Old API: https://api.pjcrescent.com/api/public/orders/status (PATCH)
        /// </summary>
        public static async Task<bool> PostCallCenterOrderStatusAsync(string orderNumber, string status, string ccTransact, int retryCount = 5)
        {
            if (string.IsNullOrWhiteSpace(orderNumber) || string.IsNullOrWhiteSpace(status))
            {
                Log.Warning("PostCallCenterOrderStatusAsync called with empty orderNumber or status.");
                return false;
            }

            string cleanOrderNumber = orderNumber.Trim();
            string cleanStatus = status.Trim().ToLowerInvariant();
            string cleanTransact = (ccTransact ?? string.Empty).Trim();

            string cacheKey = $"{cleanOrderNumber.ToUpperInvariant()}:{cleanStatus}:{cleanTransact}";
            if (_sentStatusCache.ContainsKey(cacheKey))
            {
                Log.Debug("Skipping duplicate Call Center order status update for OrderNumber '{OrderNumber}' with status '{Status}'.", cleanOrderNumber, cleanStatus);
                return true;
            }

            if (retryCount < 1) retryCount = 1;

            var payload = new
            {
                order_number = cleanOrderNumber,
                status = cleanStatus,
                cctransact = cleanTransact
            };

            string jsonPayload = JsonSerializer.Serialize(payload);

            // 1. First call Updated API: https://pjcrescent.com/api/HQEOI/CCORDERSTATUS (POST)
            bool newApiSuccess = await SendCallCenterStatusRequestAsync(
                NewCallCenterStatusUrl,
                HttpMethod.Post,
                "Updated CC API",
                jsonPayload,
                cleanOrderNumber,
                cleanStatus,
                cleanTransact,
                retryCount).ConfigureAwait(false);

            // 2. Then call Old API: https://api.pjcrescent.com/api/public/orders/status (PATCH)
            bool oldApiSuccess = await SendCallCenterStatusRequestAsync(
                OldCallCenterStatusUrl,
                new HttpMethod("PATCH"),
                "Old CC API",
                jsonPayload,
                cleanOrderNumber,
                cleanStatus,
                cleanTransact,
                retryCount).ConfigureAwait(false);

            if (newApiSuccess || oldApiSuccess)
            {
                _sentStatusCache.TryAdd(cacheKey, 0);
                return true;
            }

            return false;
        }

        private static async Task<bool> SendCallCenterStatusRequestAsync(
            string endpoint,
            HttpMethod method,
            string apiLabel,
            string jsonPayload,
            string cleanOrderNumber,
            string cleanStatus,
            string cleanTransact,
            int retryCount)
        {
            int attempt = 0;
            while (attempt < retryCount)
            {
                attempt++;
                var sw = Stopwatch.StartNew();
                try
                {
                    using (var request = new HttpRequestMessage(method, endpoint))
                    {
                        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                        request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                        Log.Information("[{ApiLabel}] Sending {Method} to {Endpoint} (Attempt {Attempt}/{MaxRetries}). Payload: {Payload}",
                            apiLabel, method.Method, endpoint, attempt, retryCount, jsonPayload);

                        HttpResponseMessage response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                        sw.Stop();
                        string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                        Log.Information("[{ApiLabel}] Server Response received (Attempt {Attempt}/{MaxRetries}) -> OrderNumber: {OrderNumber}, Status: {Status}, StatusCode: {StatusCode} ({(int)StatusCode}), Latency: {Latency}ms, Body: {ResponseBody}",
                            apiLabel, attempt, retryCount, cleanOrderNumber, cleanStatus, response.StatusCode, (int)response.StatusCode, sw.ElapsedMilliseconds, responseBody);

                        if (response.IsSuccessStatusCode)
                        {
                            Log.Information("[{ApiLabel}] SUCCESS for OrderNumber: {OrderNumber}, Status: {Status}, Transact: {Transact}",
                                apiLabel, cleanOrderNumber, cleanStatus, cleanTransact);
                            return true;
                        }
                        else
                        {
                            Log.Warning("[{ApiLabel}] Failed on Attempt {Attempt}/{MaxRetries}. OrderNumber: {OrderNumber}, StatusCode: {StatusCode}, Response: {Response}",
                                apiLabel, attempt, retryCount, cleanOrderNumber, response.StatusCode, responseBody);

                            int statusCode = (int)response.StatusCode;
                            if (statusCode >= 400 && statusCode < 500 && statusCode != 408 && statusCode != 429)
                            {
                                return false;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    Log.Warning(ex, "[{ApiLabel}] Exception on Attempt {Attempt}/{MaxRetries} for OrderNumber: {OrderNumber} after {ElapsedMs}ms. Error: {Message}",
                        apiLabel, attempt, retryCount, cleanOrderNumber, sw.ElapsedMilliseconds, ex.Message);
                }

                if (attempt < retryCount)
                {
                    int delayMs = attempt switch
                    {
                        1 => 2000,
                        2 => 4000,
                        3 => 8000,
                        _ => 15000
                    };
                    Log.Information("[{ApiLabel}] Waiting {DelaySeconds}s before retrying for OrderNumber: {OrderNumber}...",
                        apiLabel, delayMs / 1000, cleanOrderNumber);
                    await Task.Delay(delayMs).ConfigureAwait(false);
                }
            }

            Log.Error("[{ApiLabel}] All {MaxRetries} attempts exhausted for OrderNumber: {OrderNumber}, Status: {Status}",
                apiLabel, retryCount, cleanOrderNumber, cleanStatus);
            return false;
        }
        private const int COINIT_APARTMENTTHREADED = 0x2;

        [DllImport("ole32.dll")]
        private static extern int CoInitializeEx(IntPtr pvReserved, int dwCoInit);

        [DllImport("ole32.dll")]
        private static extern void CoUninitialize();

        public static string PlaceOrderdt(CallCenterOrderRoot root, string uniqueid, string orderid)
        {
            if (root == null)
            {
                return "Error: CallCenterOrderRoot order cannot be null.";
            }

            bool comInitialized = false;

            try
            {
                int comResult = CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);
                comInitialized = comResult == 0 || comResult == 1;

                string xml = CreateXML.CreateCallCenterXMLdt(root);
                if (string.IsNullOrWhiteSpace(xml))
                {
                    return "Error: Failed to generate Call Center XML.";
                }

                PixPosTrans pixPosTrans = new PixPosTrans();
                OrderProcess.LogXMLToFile(xml, "IN_CALLCENTER");

                string referenceId = root.order_number ?? orderid;
                string responseXML;

                try
                {
                    string query1 = @"select TRANSACT from dba.posheader where refid=? and opendate =(select max(opendate) from dba.posheader)";
                    OdbcParameter[] parameters = new[] { new OdbcParameter("refid", OdbcType.VarChar) { Value = referenceId } };
                    bool exist = ODBCHelper.ExistRec(query1, parameters);

                    if (exist)
                    {
                        OrderProcess.updatetabel(uniqueid, orderid, "11");
                        responseXML = xml;
                    }
                    else
                    {
                        Task<string> processTask = Task.Run(() => pixPosTrans.ProcessRequest(xml));
                        if (Task.WaitAny(new Task[] { processTask }, TimeSpan.FromSeconds(15)) != 0)
                        {
                            Log.Warning("PixelPoint COM request for CallCenter order timed out after 15 seconds.");
                            throw new TimeoutException("PixelPoint POS COM transaction request timed out after 15 seconds.");
                        }
                        responseXML = processTask.Result;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error while processing request in CallCenterOrderProcess.PlaceOrderdt.");
                    throw;
                }

                OrderProcess.LogXMLToFile(responseXML, "OUT_CALLCENTER");
                return responseXML;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Outer exception in CallCenterOrderProcess.PlaceOrderdt.");
                return $"Error: {ex.Message}";
            }
            finally
            {
                if (comInitialized)
                {
                    try
                    {
                        CoUninitialize();
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Error uninitializing COM in CallCenterOrderProcess.");
                    }
                }
            }
        }

        public static string PlaceOrderFromJson(string jsonString, string uniqueid, string orderid)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString
                };

                CallCenterOrderRoot? root = JsonSerializer.Deserialize<CallCenterOrderRoot>(jsonString, options);
                if (root == null)
                {
                    return "Error: Deserialized Call Center order root is null.";
                }

                return PlaceOrderdt(root, uniqueid, orderid);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error deserializing or placing Call Center order from JSON string.");
                return $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Sends a pre-generated final_xml request directly to PixelPoint COM for Call Center order.
        /// </summary>
        public static string PlaceOrderFromXml(string finalXml, string uniqueid, string referenceId)
        {
            if (string.IsNullOrWhiteSpace(finalXml))
            {
                return "Error: final_xml cannot be empty.";
            }

            bool comInitialized = false;

            try
            {
                int comResult = CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);
                comInitialized = comResult == 0 || comResult == 1;

                if (!comInitialized && (uint)comResult != 0x80010106)
                {
                    throw new InvalidOperationException($"COM initialization failed. HRESULT: 0x{comResult:X8}");
                }

                OrderProcess.LogXMLToFile(finalXml, "IN_CALLCENTER");

                string safeReferenceId = (referenceId ?? string.Empty).Replace("'", "''");
                string query1 = @"select TRANSACT from dba.posheader where refid='" + safeReferenceId + @"' and opendate =(select max(opendate) from dba.posheader)";

                bool exist = ODBCHelper.ExistRec(query1);

                if (exist)
                {
                    Log.Information("Call Center order already exists in PixelPoint. ReferenceId: {ReferenceId}", referenceId);
                    return finalXml;
                }

                PixPosTrans pixPosTrans = new PixPosTrans();
                Task<string> processTask = Task.Run(() => pixPosTrans.ProcessRequest(finalXml));
                if (Task.WaitAny(new Task[] { processTask }, TimeSpan.FromSeconds(15)) != 0)
                {
                    Log.Warning("PixelPoint COM request for Call Center order timed out after 15 seconds. ReferenceId: {ReferenceId}", referenceId);
                    throw new TimeoutException("PixelPoint POS COM transaction request timed out after 15 seconds.");
                }
                string responseXml = processTask.Result;

                if (string.IsNullOrWhiteSpace(responseXml))
                {
                    throw new InvalidOperationException("PixelPoint returned an empty response.");
                }

                OrderProcess.LogXMLToFile(responseXml, "OUT_CALLCENTER");
                return responseXml;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "PlaceOrderFromXml failed in CallCenterOrderProcess. ReferenceId: {ReferenceId}", referenceId);
                return $"Error: {ex.Message}";
            }
            finally
            {
                if (comInitialized)
                {
                    try
                    {
                        CoUninitialize();
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Error uninitializing COM in CallCenterOrderProcess.");
                    }
                }
            }
        }
    }
}
