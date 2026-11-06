using PixelPointPOS;
using Serilog;
using System;
using System.Data.Odbc;
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
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly ConcurrentDictionary<string, byte> _sentStatusCache = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Posts status update for Call Center orders to https://api.pjcrescent.com/api/public/orders/status
        /// Method: PATCH
        /// </summary>
        public static async Task<bool> PostCallCenterOrderStatusAsync(string orderNumber, string status, string ccTransact)
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

            try
            {
                string endpoint = "https://api.pjcrescent.com/api/public/orders/status";

                var payload = new
                {
                    order_number = cleanOrderNumber,
                    status = cleanStatus,
                    cctransact = cleanTransact
                };

                string jsonPayload = JsonSerializer.Serialize(payload);

                using (var request = new HttpRequestMessage(new HttpMethod("PATCH"), endpoint))
                {
                    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                    Log.Information("Sending Call Center order status update to {Endpoint}. Payload: {Payload}", endpoint, jsonPayload);

                    HttpResponseMessage response = await _httpClient.SendAsync(request).ConfigureAwait(false);
                    string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        _sentStatusCache.TryAdd(cacheKey, 0);
                        Log.Information("Successfully updated Call Center Order Status. OrderNumber: {OrderNumber}, Status: {Status}, Transact: {Transact}, Response: {Response}", cleanOrderNumber, cleanStatus, cleanTransact, responseBody);
                        return true;
                    }
                    else
                    {
                        Log.Error("Failed to update Call Center Order Status. OrderNumber: {OrderNumber}, Status: {Status}, Transact: {Transact}, StatusCode: {StatusCode}, Response: {Response}", cleanOrderNumber, cleanStatus, cleanTransact, response.StatusCode, responseBody);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Exception occurred while posting Call Center Order Status for OrderNumber: {OrderNumber}, Status: {Status}", cleanOrderNumber, cleanStatus);
                return false;
            }
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
