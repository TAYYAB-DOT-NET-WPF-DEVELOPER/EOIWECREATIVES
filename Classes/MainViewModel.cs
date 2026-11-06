using Newtonsoft.Json;
using Oracle.ManagedDataAccess.Client;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Odbc;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection.PortableExecutable;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Xml;
using System.Xml.Linq;
using WeCreatives_EOI.Services;

namespace WeCreatives_EOI.Classes
{
    /// <summary>
    /// Production-hardened version of the original MainViewModel.
    ///
    /// Existing workflows retained:
    /// 1. HubRise order polling and order placement.
    /// 2. Tab-order polling and order placement.
    /// 3. Cancelled-order synchronization.
    /// 4. Available-table synchronization.
    /// 5. Unavailable-employee synchronization.
    /// 6. SMS support and POS message-status update.
    /// 7. Existing public update methods and UI order log.
    /// </summary>
    public class MainViewModel : ObservableObject, IDisposable
    {
        private const int MaxUiOrderLogItems = 500;
        private const int MaximumOrderErrorAttempts = 4;
        private const int DefaultHttpTimeoutSeconds = 30;
        private const int MaximumResponseLogLength = 4000;

        private const string DefaultTablesUpdateUrl =
            "https://api.pjcrescent.com/api/public/tables/update";

        private const string DefaultEmployeeStatusUpdateUrl =
            "https://api.pjcrescent.com/api/public/app-users/update-status";

        private const string HubRiseBaseAddress = "https://api.hubrise.com/";
        private const string DefaultSmsUrl =
            "https://cbs.zong.com.pk/ReachCWSv2/CorporateSMS.svc";

        private ObservableCollection<OrderModel> _allorders;

        // Retained from the original code for compatibility and diagnostics.
        // Per-order counters below prevent one failed order from blocking all orders.
        private int accecctoken = 0;
        private int ORDERERROR = 0;

        private int SNUM = 0;
        private int HUBRISESNUM = 0;
        private int TABSNUM = 0;
        private string FOODPANDASNUM = string.Empty;

        private readonly TokenManager _tokenmanager;
        private readonly GetOrderData _getorderdata;
        private readonly VendorOrderStatusService? _vendorOrderStatusService;

        private readonly DispatcherTimer? _orderCheckTimer;
        private readonly DispatcherTimer _wEBorderCheckTimer;
        private readonly DispatcherTimer? _TABorderCheckTimer;
        private readonly DispatcherTimer? _CANCELorderCheckTimer;
        private readonly DispatcherTimer? _tableUpdateTimer;
        private readonly DispatcherTimer? _empUpdateTimer;
        private readonly DispatcherTimer? _fpOrderCheckTimer;
        private readonly DispatcherTimer? _callCenterOrderCheckTimer;

        private System.Threading.Timer? _watchdogTimer;
        private long _lastHeartbeatTicks = Environment.TickCount64;
        private int _isRestarting = 0;

        private bool _isTableUpdateRunning = false;

        private readonly SemaphoreSlim _tableUpdateGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _employeeUpdateGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _orderCheckGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _webOrderCheckGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _tabOrderCheckGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _cancelOrderCheckGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _driverSyncGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _fpOrderCheckGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _callCenterOrderCheckGate = new SemaphoreSlim(1, 1);

        public ICommand SyncDriversCommand { get; }
        public ICommand RunCodeCommand { get; }
        public ICommand StopCodeCommand { get; }
        public ICommand SettingsPageCommand { get; }

        private readonly ConcurrentDictionary<string, byte> _activeOrderIds =
            new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, byte> _activeTabOrderIds =
            new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, int> _driverClockStates =
            new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, DateTime> _failedDriverClockAttempts =
            new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, string> _lastAssignedDriverOrders =
            new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, int> _accessTokenFailureCounts =
            new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, int> _orderErrorCounts =
            new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, byte> _sentHubRiseStatusCache =
            new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

        private readonly CancellationTokenSource _shutdownCancellation =
            new CancellationTokenSource();

        private bool _disposed;

        private static readonly HttpClient _tableHttpClient = CreateHttpClient();
        private static readonly HttpClient _hubRiseHttpClient = CreateHubRiseHttpClient();

        public ObservableCollection<OrderModel> AllOrders
        {
            get { return _allorders; }
            set
            {
                _allorders = value ?? new ObservableCollection<OrderModel>();
                OnPropertyChanged(nameof(AllOrders));
            }
        }

        public MainViewModel(
            IServiceProvider serviceProvider,
            TokenManager tokenmanager,
            GetOrderData getorderdata,
            VendorOrderStatusService? vendorOrderStatusService = null)
        {
            if (tokenmanager == null)
                throw new ArgumentNullException(nameof(tokenmanager));

            if (getorderdata == null)
                throw new ArgumentNullException(nameof(getorderdata));

            if (serviceProvider == null)
            {
                Log.Warning("MainViewModel was created without an IServiceProvider instance.");
            }

            _tokenmanager = tokenmanager;
            _getorderdata = getorderdata;
            _vendorOrderStatusService = vendorOrderStatusService;


            SNUM = ParseRequiredPositiveInteger(App.SNUM, nameof(App.SNUM));
            FOODPANDASNUM = ParseRequiredString(App.FPSNUM, nameof(App.FPSNUM));
            HUBRISESNUM = ParseRequiredPositiveInteger(App.HUBRISESNUM, nameof(App.HUBRISESNUM));
            TABSNUM = ParseRequiredPositiveInteger(App.TABSNUM, nameof(App.TABSNUM));

            AllOrders = new ObservableCollection<OrderModel>();

            SyncDriversCommand = new RelayCommand(async () => await SyncDriversAsync());
            RunCodeCommand = new RelayCommand(() => StartTimers());
            StopCodeCommand = new RelayCommand(() => StopTimers());
            SettingsPageCommand = new RelayCommand(() => OpenSettings());

            // _orderCheckTimer = CreateTimer(TimeSpan.FromSeconds(10), OrderCheckTimer_Tick);
             _wEBorderCheckTimer = CreateTimer(TimeSpan.FromSeconds(10), WebOrderCheckTimer_Tick);
             _TABorderCheckTimer = CreateTimer(TimeSpan.FromSeconds(10), TABOrderCheckTimer_Tick);
             _CANCELorderCheckTimer = CreateTimer(TimeSpan.FromSeconds(20), CancelOrderCheckTimer_Tick);
            _tableUpdateTimer = CreateTimer(TimeSpan.FromSeconds(30), TableUpdateTimer_Tick);
            _empUpdateTimer = CreateTimer(TimeSpan.FromSeconds(30), EmpUpdateTimer_Tick);
             _fpOrderCheckTimer = CreateTimer(TimeSpan.FromSeconds(5), FPOrderCheckTimer_Tick);
            _callCenterOrderCheckTimer = CreateTimer(TimeSpan.FromSeconds(5), CallCenterOrderCheckTimer_Tick);
           // CheckForCallCenterOrder();
            StartTimers();

            Log.Information(
                "MainViewModel initialized. SNUM: {SNUM}, TABSNUM: {TABSNUM}, CCSNUM: {CCSNUM}",
                SNUM,
                TABSNUM,
                App.CCSNUM);
        }

        private static DispatcherTimer CreateTimer(TimeSpan interval, EventHandler tickHandler)
        {
            DispatcherTimer timer = new DispatcherTimer
            {
                Interval = interval
            };

            timer.Tick += tickHandler;
            return timer;
        }

        private void StartTimers()
        {
            _orderCheckTimer?.Start();
            _wEBorderCheckTimer?.Start();
            _TABorderCheckTimer?.Start();
            _CANCELorderCheckTimer?.Start();
            _tableUpdateTimer?.Start();
            _empUpdateTimer?.Start();
            _fpOrderCheckTimer?.Start();
            _callCenterOrderCheckTimer?.Start();
            _watchdogTimer = new System.Threading.Timer(WatchdogCallback, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        private void StopTimers()
        {
            _orderCheckTimer?.Stop();
            _wEBorderCheckTimer?.Stop();
            _TABorderCheckTimer?.Stop();
            _CANCELorderCheckTimer?.Stop();
            _tableUpdateTimer?.Stop();
            _empUpdateTimer?.Stop();
            _fpOrderCheckTimer?.Stop();
            _callCenterOrderCheckTimer?.Stop();
            _watchdogTimer?.Dispose();
            _watchdogTimer = null;
        }


        private async Task ExecuteWithGateAndTimeoutAsync(
            SemaphoreSlim gate,
            int timeoutSeconds,
            Func<CancellationToken, Task> action,
            string actionName)
        {
            Interlocked.Exchange(ref _lastHeartbeatTicks, Environment.TickCount64);

            if (!await gate.WaitAsync(0).ConfigureAwait(false))
            {
                Log.Debug("{ActionName} tick skipped because previous execution gate is still active.", actionName);
                return;
            }

            using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
            using (CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, _shutdownCancellation.Token))
            {
                try
                {
                    await action(linkedCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    Log.Warning("{ActionName} timed out after {TimeoutSeconds} seconds. Gate released for next cycle.", actionName, timeoutSeconds);
                }
                catch (OperationCanceledException) when (_shutdownCancellation.IsCancellationRequested)
                {
                    Log.Debug("{ActionName} stopped because the application is shutting down.", actionName);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Unhandled error during {ActionName}.", actionName);
                }
                finally
                {
                    try
                    {
                        if (gate.CurrentCount == 0)
                        {
                            gate.Release();
                        }
                    }
                    catch (SemaphoreFullException)
                    {
                        // Ignore if already released
                    }
                }
            }
        }

        private async void TableUpdateTimer_Tick(object sender, EventArgs e)
        {
            _isTableUpdateRunning = true;
            try
            {
                await ExecuteWithGateAndTimeoutAsync(
                    _tableUpdateGate,
                    30,
                    async (ct) => await PostAvailableTablesAsync(ct),
                    "Table synchronization");
            }
            finally
            {
                _isTableUpdateRunning = false;
            }
        }

        private async void EmpUpdateTimer_Tick(object sender, EventArgs e)
        {
            await ExecuteWithGateAndTimeoutAsync(
                _employeeUpdateGate,
                30,
                async (ct) =>
                {
                    await PostUnAvailableEmployeeAsync(ct);
                    await CheckRiderClockStatusAsync(ct);
                    await CheckDriverOrderAssignmentAsync(ct);
                },
                "Employee synchronization");
        }

        private async Task PostAvailableTablesAsync(CancellationToken cancellationToken = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCancellation.Token, cancellationToken);
            var token = linkedCts.Token;

            string query = @"
        SELECT TS.TABLENUM
        FROM dba.TABLESETUP TS
        WHERE NOT EXISTS (
            SELECT 1
            FROM TABINFO TI
            WHERE TI.TABLENUM = TS.TABLENUM
        )
        ORDER BY TS.TABLENUM";

            DataTable dt = await Task.Run(
                () => ODBCHelper.SelectRec(query),
                token);

            HashSet<string> uniqueTables =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (dt != null)
            {
                foreach (DataRow dr in dt.Rows)
                {
                    if (dr["TABLENUM"] == DBNull.Value)
                        continue;

                    string tableNum = Convert.ToString(dr["TABLENUM"])?.Trim();

                    if (!string.IsNullOrWhiteSpace(tableNum))
                    {
                        uniqueTables.Add(tableNum);
                    }
                }
            }

            List<string> tables = uniqueTables
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var requestBody = new
            {
                branch_id = TABSNUM,
                tables = tables
            };

            string json = JsonConvert.SerializeObject(requestBody);
            string endpoint = GetEnvironmentValue(
                "KRC_TABLES_UPDATE_URL",
                DefaultTablesUpdateUrl);

            using (StringContent content =
                   new StringContent(json, Encoding.UTF8, "application/json"))
            using (HttpResponseMessage response = await _tableHttpClient.PostAsync(
                       endpoint,
                       content,
                       _shutdownCancellation.Token))
            {
                string responseText = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    Log.Debug(
                        "Tables updated successfully. Branch: {BranchId}, TableCount: {TableCount}, Tables: {Tables}, Response: {Response}",
                        TABSNUM,
                        tables.Count,
                        string.Join(",", tables),
                        TruncateForLog(responseText));
                }
                else
                {
                    Log.Warning(
                        "Tables update failed. Branch: {BranchId}, Status: {StatusCode}, TableCount: {TableCount}, Response: {Response}",
                        TABSNUM,
                        (int)response.StatusCode,
                        tables.Count,
                        TruncateForLog(responseText));
                }
            }
        }

        private async Task PostUnAvailableEmployeeAsync(CancellationToken cancellationToken = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCancellation.Token, cancellationToken);
            var token = linkedCts.Token;

            string query = @"
        SELECT DISTINCT empnum
        FROM punchpayroll
        WHERE punchout IS NOT NULL
          AND opendate = (SELECT MAX(opendate) FROM posheader)
          AND empnum NOT IN (2146435098, 2146436080, 999)
    ";

            DataTable dt = await Task.Run(
                () => ODBCHelper.SelectRec(query),
                token);

            if (dt == null || dt.Rows.Count == 0)
            {
                Log.Debug("No employees found for status update.");
                return;
            }

            List<object> users = new List<object>();
            HashSet<string> employeeIds =
                new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (DataRow dr in dt.Rows)
            {
                if (dr["EMPNUM"] == DBNull.Value)
                    continue;

                string empNum = Convert.ToString(dr["EMPNUM"])?.Trim();

                if (string.IsNullOrWhiteSpace(empNum) || !employeeIds.Add(empNum))
                    continue;

                users.Add(new
                {
                    employee_id = empNum,
                    status = false
                });
            }

            if (users.Count == 0)
            {
                Log.Debug("No valid employee numbers found for status update.");
                return;
            }

            var requestBody = new
            {
                users = users
            };

            string json = JsonConvert.SerializeObject(requestBody);
            string endpoint = GetEnvironmentValue(
                "KRC_EMPLOYEE_STATUS_UPDATE_URL",
                DefaultEmployeeStatusUpdateUrl);

            try
            {
                using (StringContent content =
                       new StringContent(json, Encoding.UTF8, "application/json"))
                using (HttpResponseMessage response = await _tableHttpClient.PostAsync(
                           endpoint,
                           content,
                           _shutdownCancellation.Token))
                {
                    string responseText = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        Log.Debug(
                            "Employees status updated successfully. TotalEmployees: {TotalEmployees}, Response: {Response}",
                            users.Count,
                            TruncateForLog(responseText));
                    }
                    else
                    {
                        Log.Warning(
                            "Employees status update failed. StatusCode: {StatusCode}, TotalEmployees: {TotalEmployees}, Response: {Response}",
                            (int)response.StatusCode,
                            users.Count,
                            TruncateForLog(responseText));
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Log.Error(
                    ex,
                    "Error while updating employees status. TotalEmployees: {TotalEmployees}",
                    users.Count);
            }
        }

        private async Task CheckRiderClockStatusAsync(CancellationToken cancellationToken = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCancellation.Token, cancellationToken);
            var token = linkedCts.Token;

            if (_vendorOrderStatusService == null)
                return;

            try
            {
                // Query 1: Rider Clock In query
                string clockInQuery = @"
                    select sin AS clockinemp from employee where empnum in (  SELECT empnum 
FROM punchpayroll 
WHERE opendate = (SELECT MAX(opendate) FROM punchpayroll) 
  AND jobtype = 2004 
  AND punchin IS NOT NULL)";

                // Query 2: Rider Clock Out query
                string clockOutQuery = @"
                    select SIN AS clockoutemp FROM EMPLOYEE WHERE EMPNUM IN (
SELECT EMPNUM
FROM punchpayroll 
WHERE opendate = (SELECT MAX(opendate) FROM punchpayroll) 
  AND jobtype = 2004 
  AND punchin IS NOT NULL 
  AND punchout IS NOT NULL)";

                DataTable dtClockIn = await Task.Run(
                    () => ODBCHelper.SelectRec(clockInQuery),
                    token);

                DataTable dtClockOut = await Task.Run(
                    () => ODBCHelper.SelectRec(clockOutQuery),
                    token);

                HashSet<string> clockOutDriverIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (dtClockOut != null && dtClockOut.Rows.Count > 0)
                {
                    foreach (DataRow dr in dtClockOut.Rows)
                    {
                        if (dr["clockoutemp"] == DBNull.Value)
                            continue;

                        string empNum = Convert.ToString(dr["clockoutemp"])?.Trim();
                        if (!string.IsNullOrWhiteSpace(empNum))
                        {
                            clockOutDriverIds.Add(empNum);
                        }
                    }
                }

                HashSet<string> clockInDriverIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (dtClockIn != null && dtClockIn.Rows.Count > 0)
                {
                    foreach (DataRow dr in dtClockIn.Rows)
                    {
                        if (dr["clockinemp"] == DBNull.Value)
                            continue;

                        string empNum = Convert.ToString(dr["clockinemp"])?.Trim();
                        if (!string.IsNullOrWhiteSpace(empNum) && !clockOutDriverIds.Contains(empNum))
                        {
                            clockInDriverIds.Add(empNum);
                        }
                    }
                }

                // Post Clock In for active riders (clock = 1)
                foreach (string driverId in clockInDriverIds)
                {
                    token.ThrowIfCancellationRequested();

                    string failKey = driverId + ":1";
                    if (_failedDriverClockAttempts.TryGetValue(failKey, out DateTime retryAfter) && DateTime.Now < retryAfter)
                    {
                        continue;
                    }

                    if (!_driverClockStates.TryGetValue(driverId, out int currentState) || currentState != 1)
                    {
                        bool success = await _vendorOrderStatusService.ClockDriverAsync(driverId, 1).ConfigureAwait(false);
                        if (success)
                        {
                            _driverClockStates[driverId] = 1;
                            _failedDriverClockAttempts.TryRemove(failKey, out _);
                            Log.Information("Rider Clock In (clock=1) posted for Driver POS ID {DriverId}", driverId);
                        }
                        else
                        {
                            _failedDriverClockAttempts[failKey] = DateTime.Now.AddMinutes(15);
                        }
                    }
                }

                // Post Clock Out for completed riders (clock = 0)
                foreach (string driverId in clockOutDriverIds)
                {
                    token.ThrowIfCancellationRequested();

                    string failKey = driverId + ":0";
                    if (_failedDriverClockAttempts.TryGetValue(failKey, out DateTime retryAfter) && DateTime.Now < retryAfter)
                    {
                        continue;
                    }

                    if (!_driverClockStates.TryGetValue(driverId, out int currentState) || currentState != 0)
                    {
                        bool success = await _vendorOrderStatusService.ClockDriverAsync(driverId, 0).ConfigureAwait(false);
                        if (success)
                        {
                            _driverClockStates[driverId] = 0;
                            _failedDriverClockAttempts.TryRemove(failKey, out _);
                            Log.Information("Rider Clock Out (clock=0) posted for Driver POS ID {DriverId}", driverId);
                        }
                        else
                        {
                            _failedDriverClockAttempts[failKey] = DateTime.Now.AddMinutes(15);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (_shutdownCancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
            }
        }

        private async Task CheckDriverOrderAssignmentAsync(CancellationToken cancellationToken = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCancellation.Token, cancellationToken);
            var token = linkedCts.Token;

            if (_vendorOrderStatusService == null)
                return;

            try
            {
                string query = @"
                    SELECT 
                        Ph.refid,
                        E.SIN,
                        P.EMPNUM
                    FROM POSHDELIVERY P
                    LEFT JOIN posheader ph 
                        ON ph.transact = p.transact
                       AND ph.snum = p.snum 
                       AND ph.opendate = p.opendate
                    LEFT JOIN EMPLOYEE E
                        ON E.EMPNUM = P.EMPNUM
                    WHERE P.TIMEOUT IS NOT NULL
                      AND P.TIMEIN IS NULL and ph.saletypeindex in (2060,1002)
                      AND P.OPENDATE = (
                          SELECT MAX(OPENDATE)
                          FROM POSHDELIVERY
                      )
                    ORDER BY P.timeout DESC";

                DataTable dt = await Task.Run(
                    () => ODBCHelper.SelectRec(query),
                    token);

                if (dt == null || dt.Rows.Count == 0)
                    return;

                Dictionary<string, List<object>> driverOrdersMap = new Dictionary<string, List<object>>(StringComparer.OrdinalIgnoreCase);

                foreach (DataRow dr in dt.Rows)
                {
                    _shutdownCancellation.Token.ThrowIfCancellationRequested();

                    string refIdStr = GetDataRowStringWithFallback(dr, "refid", "REFID");
                    string driverCode = GetDataRowStringWithFallback(dr, "SIN", "EMPNUM");

                    if (string.IsNullOrWhiteSpace(refIdStr) || string.IsNullOrWhiteSpace(driverCode))
                        continue;

                    object orderVal;
                    if (long.TryParse(refIdStr.Trim(), out long parsedOrderId))
                    {
                        orderVal = parsedOrderId;
                    }
                    else
                    {
                        orderVal = refIdStr.Trim();
                    }

                    if (!driverOrdersMap.ContainsKey(driverCode))
                    {
                        driverOrdersMap[driverCode] = new List<object>();
                    }

                    if (!driverOrdersMap[driverCode].Contains(orderVal))
                    {
                        driverOrdersMap[driverCode].Add(orderVal);
                    }
                }

                foreach (var kvp in driverOrdersMap)
                {
                    _shutdownCancellation.Token.ThrowIfCancellationRequested();

                    string driverCode = kvp.Key;
                    List<object> ordersList = kvp.Value;
                    string currentOrdersSignature = string.Join(",", ordersList);

                    if (_lastAssignedDriverOrders.TryGetValue(driverCode, out string? previousSignature) &&
                        string.Equals(previousSignature, currentOrdersSignature, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    bool success = await _vendorOrderStatusService.AssignDriverOrdersAsync(driverCode, ordersList).ConfigureAwait(false);
                    if (success)
                    {
                        _lastAssignedDriverOrders[driverCode] = currentOrdersSignature;
                        Log.Information("Driver Order Assignment successful. Driver: {DriverCode}, Orders: {Orders}", driverCode, currentOrdersSignature);
                    }
                }
            }
            catch (OperationCanceledException) when (_shutdownCancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error occurred during driver order assignment check.");
            }
        }

        public async Task SyncDriversAsync()
        {
            if (!await _driverSyncGate.WaitAsync(0).ConfigureAwait(true))
            {
                Log.Information("Driver synchronization is already in progress.");
                return;
            }

            try
            {
                Log.Information("Starting on-demand driver synchronization...");

                string query = @"
                    SELECT empname AS first_name,
                           emplastname AS last_name,
                           email AS email_address,
                           city,
                           adress1 AS address,
                           hometele AS phone_number,
                           empnum AS driver_pos_code
                    FROM employee
                    WHERE isactive = 1
                      AND hometele IS NOT NULL";

                DataTable dt = await Task.Run(
                    () => ODBCHelper.SelectRec(query),
                    _shutdownCancellation.Token);

                if (dt == null || dt.Rows.Count == 0)
                {
                    Log.Information("No active drivers with phone numbers found for synchronization.");
                    AddToAllOrders("No active drivers found to sync.", "DRIVER_SYNC", "SYSTEM");
                    return;
                }

                int successCount = 0;
                int failCount = 0;

                foreach (DataRow dr in dt.Rows)
                {
                    string firstName = GetDataRowStringWithFallback(dr, "first_name", "empname");
                    string lastName = GetDataRowStringWithFallback(dr, "last_name", "emplastname");
                    string email = GetDataRowStringWithFallback(dr, "email_address", "email");
                    string city = GetDataRowStringWithFallback(dr, "city");
                    string address = GetDataRowStringWithFallback(dr, "address", "adress1");
                    string phoneNumber = GetDataRowStringWithFallback(dr, "phone_number", "hometele");
                    string driverPosCode = GetDataRowStringWithFallback(dr, "driver_pos_code", "empnum");
                    string cnic = GetDataRowStringWithFallback(dr, "cnic");
                    string storePosCode = SNUM.ToString();

                    var driverPayload = new
                    {
                        first_name = firstName,
                        last_name = lastName,
                        email_address = email,
                        city = city,
                        address = address,
                        phone_number = phoneNumber,
                        cnic = cnic,
                        driver_pos_code = driverPosCode,
                        store_pos_code = storePosCode
                    };

                    bool success = false;
                    if (_vendorOrderStatusService != null)
                    {
                        success = await _vendorOrderStatusService.CreateDriverAsync(driverPayload).ConfigureAwait(false);
                    }

                    if (success)
                    {
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                    }
                }

                Log.Information("Driver sync completed. Total: {Total}, Success: {SuccessCount}, Failed: {FailCount}", dt.Rows.Count, successCount, failCount);
                AddToAllOrders($"Driver Sync Complete: {successCount}/{dt.Rows.Count} drivers synced.", "DRIVER_SYNC", "SYSTEM");
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Log.Error(ex, "Error occurred during driver synchronization.");
                AddToAllOrders("Driver Sync Failed", "DRIVER_SYNC", "ERROR");
            }
            finally
            {
                _driverSyncGate.Release();
            }
        }

        private static string GetDataRowStringWithFallback(DataRow row, string primaryColumn, string fallbackColumn = "")
        {
            if (row == null || row.Table == null)
                return string.Empty;

            if (row.Table.Columns.Contains(primaryColumn) && row[primaryColumn] != DBNull.Value)
            {
                string val = Convert.ToString(row[primaryColumn])?.Trim();
                if (!string.IsNullOrEmpty(val))
                    return val;
            }

            if (!string.IsNullOrEmpty(fallbackColumn) && row.Table.Columns.Contains(fallbackColumn) && row[fallbackColumn] != DBNull.Value)
            {
                return Convert.ToString(row[fallbackColumn])?.Trim() ?? string.Empty;
            }

            return string.Empty;
        }

        public void OpenSettings()
        {
            Dispatcher? dispatcher = Application.Current == null ? null : Application.Current.Dispatcher;
            if (dispatcher != null)
            {
                dispatcher.Invoke(() =>
                {
                    SettingsWindow settingsWin = new SettingsWindow
                    {
                        Owner = Application.Current.MainWindow
                    };
                    bool? result = settingsWin.ShowDialog();
                    if (result == true)
                    {
                        if (int.TryParse(App.SNUM, out int newSnum))
                        {
                            SNUM = newSnum;
                        }
                        if (int.TryParse(App.HUBRISESNUM, out int newHubriseSnum))
                        {
                            HUBRISESNUM = newHubriseSnum;
                        }
                        if (int.TryParse(App.TABSNUM, out int newTabSnum))
                        {
                            TABSNUM = newTabSnum;
                        }
                        Log.Information("Reloaded SNUM ({SNUM}), HUBRISESNUM ({HUBRISESNUM}), and TABSNUM ({TABSNUM}) after configuration save.", SNUM, HUBRISESNUM, TABSNUM);
                    }
                });
            }
        }

       

        private async void WebOrderCheckTimer_Tick(object sender, EventArgs e)
        {
            await ExecuteWithGateAndTimeoutAsync(
                _webOrderCheckGate,
                30,
                async (ct) => await CheckForWebOrder(ct),
                "Web order polling");
        }

        private async void FPOrderCheckTimer_Tick(object sender, EventArgs e)
        {
            await ExecuteWithGateAndTimeoutAsync(
                _fpOrderCheckGate,
                30,
                async (ct) => await FPORDER(ct),
                "Foodpanda order polling");
        }

        private async void CallCenterOrderCheckTimer_Tick(object sender, EventArgs e)
        {
            await ExecuteWithGateAndTimeoutAsync(
                _callCenterOrderCheckGate,
                30,
                async (ct) => await CheckForCallCenterOrder(ct),
                "CallCenter order polling");
        }

        private async Task CheckForCompletedWebOrders(CancellationToken cancellationToken = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCancellation.Token, cancellationToken);
            var token = linkedCts.Token;

            if (_vendorOrderStatusService == null)
                return;

            try
            {
                string query = @"
                    SELECT UNIQUEID, TRANSACT
                    FROM PJWEBORDER
                    WHERE STATUS = 1 AND TRANSACT IS NOT NULL AND TRANSACT <> '' AND TRANSACT <> '11' AND STOREID = '" + SNUM + "'";

                DataTable dt = await Task.Run(
                    () => SqlHelper.SelectRec(query),
                    token);

                if (dt == null || dt.Rows.Count == 0)
                    return;

                foreach (DataRow dr in dt.Rows)
                {
                    _shutdownCancellation.Token.ThrowIfCancellationRequested();

                    string transact = GetDataRowString(dr, "TRANSACT");
                    string uniqueId = GetDataRowString(dr, "UNIQUEID");

                    if (string.IsNullOrWhiteSpace(transact) || string.IsNullOrWhiteSpace(uniqueId))
                        continue;

                    if (!long.TryParse(transact.Trim(), out long transNo))
                        continue;

                    // 1. Check for Completed Status: status = 3 AND nettotal > 0
                    string odbcCompletedQuery = @"
                        SELECT refid, status, nettotal
                        FROM dba.posheader
                        WHERE transact = ? AND status = 3 AND nettotal > 0 AND opendate = (SELECT MAX(opendate) FROM dba.posheader)";

                    OdbcParameter[] parametersCompleted = new[] { new OdbcParameter("transact", OdbcType.BigInt) { Value = transNo } };
                    DataTable dtPosCompleted = await Task.Run(
                        () => ODBCHelper.SelectRec(odbcCompletedQuery, parametersCompleted),
                        _shutdownCancellation.Token);

                    if (dtPosCompleted != null && dtPosCompleted.Rows.Count > 0)
                    {
                        string refId = dtPosCompleted.Rows[0]["refid"]?.ToString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(refId))
                        {
                            bool sent = await _vendorOrderStatusService.UpdateOrderStatusAsync(refId.Trim(), "completed").ConfigureAwait(false);
                            if (sent)
                            {
                                string updateQuery = @"UPDATE PJWEBORDER SET STATUS = 3 WHERE UNIQUEID = @UNIQUEID";
                                SqlHelper.CURD(updateQuery, new SqlParameter("@UNIQUEID", uniqueId));
                                Log.Information("Vendor Order Status 'completed' sent for Web Order RefId {RefId} (Transact {Transact}, nettotal > 0). Updated STATUS = 3 in PJWEBORDER.", refId, transact);
                            }
                        }

                        continue;
                    }

                    // 2. Check for Cancelled Status: all items in order have prodtype = 101
                    string odbcDetailQuery = @"
                        SELECT 
                            COUNT(*) AS total_count,
                            SUM(CASE WHEN prodtype = 101 THEN 1 ELSE 0 END) AS voided_count
                        FROM dba.posdetail
                        WHERE transact = ?";

                    OdbcParameter[] parametersDetail = new[] { new OdbcParameter("transact", OdbcType.BigInt) { Value = transNo } };
                    DataTable dtDetail = await Task.Run(
                        () => ODBCHelper.SelectRec(odbcDetailQuery, parametersDetail),
                        _shutdownCancellation.Token);

                    if (dtDetail != null && dtDetail.Rows.Count > 0)
                    {
                        DataRow detailRow = dtDetail.Rows[0];
                        int totalCount = detailRow["total_count"] != DBNull.Value ? Convert.ToInt32(detailRow["total_count"]) : 0;
                        int voidedCount = detailRow["voided_count"] != DBNull.Value ? Convert.ToInt32(detailRow["voided_count"]) : 0;

                        if (totalCount > 0 && totalCount == voidedCount)
                        {
                            string odbcHeaderQuery = @"
                                SELECT refid
                                FROM dba.posheader
                                WHERE transact = ? AND opendate = (SELECT MAX(opendate) FROM dba.posheader)";

                            OdbcParameter[] parametersHeader = new[] { new OdbcParameter("transact", OdbcType.BigInt) { Value = transNo } };
                            DataTable dtHeader = await Task.Run(
                                () => ODBCHelper.SelectRec(odbcHeaderQuery, parametersHeader),
                                _shutdownCancellation.Token);

                            if (dtHeader != null && dtHeader.Rows.Count > 0)
                            {
                                string refId = dtHeader.Rows[0]["refid"]?.ToString() ?? string.Empty;
                                if (!string.IsNullOrWhiteSpace(refId))
                                {
                                    bool sent = await _vendorOrderStatusService.UpdateOrderStatusAsync(refId.Trim(), "cancelled").ConfigureAwait(false);
                                    if (sent)
                                    {
                                        string updateQuery = @"UPDATE PJWEBORDER SET STATUS = 9 WHERE UNIQUEID = @UNIQUEID";
                                        SqlHelper.CURD(updateQuery, new SqlParameter("@UNIQUEID", uniqueId));
                                        Log.Information("Vendor Order Status 'cancelled' sent for Web Order RefId {RefId} (Transact {Transact}). All {TotalCount} items had prodtype = 101. Updated STATUS = 9 in PJWEBORDER.", refId, transact, totalCount);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (_shutdownCancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error while checking completed/cancelled web orders.");
            }
        }




        private async void TABOrderCheckTimer_Tick(object sender, EventArgs e)
        {
            await ExecuteWithGateAndTimeoutAsync(
                _tabOrderCheckGate,
                30,
                async (ct) => await CheckForTabOrder(ct),
                "Tab-order polling");
        }

        private async void CancelOrderCheckTimer_Tick(object sender, EventArgs e)
        {
            await ExecuteWithGateAndTimeoutAsync(
                _cancelOrderCheckGate,
                30,
                async (ct) =>
                {
                    await CheckForCancelOrder(ct);

                    if (_vendorOrderStatusService == null || !_vendorOrderStatusService.IsWebOrderApiInBackoff(out _))
                    {
                        await CheckForCompletedWebOrders(ct);
                    }

                    await CheckForCancelledFPOrders(ct);

                    await CheckForCompletedAndCancelledCallCenterOrders(ct);
                },
                "Cancelled & Completed order status polling");
        }

        private void SetAccessToken()
        {
            try
            {
                _tokenmanager.SetAccessToken();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unable to set the access token.");
                Console.WriteLine(ex.Message);
            }
        }

        private static void SendSms(string contact, string transact, string msg)
        {
            string normalizedContact = NormalizePhoneNumber(contact);

            if (string.IsNullOrWhiteSpace(normalizedContact))
                throw new ArgumentException("A valid SMS destination is required.", nameof(contact));

            string message = string.Format(
                "Thank you for your order# {0}. {1}",
                transact,
                msg ?? string.Empty);

            string mask = GetEnvironmentValue("ZONG_SMS_MASK", "PAPA JOHNS");
            int unicode = 0; // 0 = English, 1 = Urdu

            // Production credentials must be supplied through environment variables.
            // Never commit live credentials in source control.
            string username = Environment.GetEnvironmentVariable("ZONG_SMS_USERNAME");
            string password = Environment.GetEnvironmentVariable("ZONG_SMS_PASSWORD");

            if (string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrWhiteSpace(password))
            {
                throw new InvalidOperationException(
                    "ZONG_SMS_USERNAME and ZONG_SMS_PASSWORD environment variables are required.");
            }

            string url = GetEnvironmentValue("ZONG_SMS_URL", DefaultSmsUrl);
            string xmlSafeMessage = SecurityElement.Escape(message) ?? string.Empty;
            string xmlSafeUsername = SecurityElement.Escape(username) ?? string.Empty;
            string xmlSafePassword = SecurityElement.Escape(password) ?? string.Empty;
            string xmlSafeContact = SecurityElement.Escape(normalizedContact) ?? string.Empty;
            string xmlSafeMask = SecurityElement.Escape(mask) ?? string.Empty;

            string xmlPostString = string.Format(
                @"<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:tem=""http://tempuri.org/"" xmlns:cbs=""http://schemas.datacontract.org/2004/07/CBSCustomerAPI"">
             <soapenv:Header/>
             <soapenv:Body>
               <tem:QuickSMS>
                 <tem:obj_QuickSMS>
                   <cbs:loginId>{0}</cbs:loginId>
                   <cbs:loginPassword>{1}</cbs:loginPassword>
                   <cbs:Destination>{2}</cbs:Destination>
                   <cbs:Mask>{3}</cbs:Mask>
                   <cbs:Message>{4}</cbs:Message>
                   <cbs:UniCode>{5}</cbs:UniCode>
                   <cbs:ShortCodePrefered>n</cbs:ShortCodePrefered>
                 </tem:obj_QuickSMS>
               </tem:QuickSMS>
             </soapenv:Body>
             </soapenv:Envelope>",
                xmlSafeUsername,
                xmlSafePassword,
                xmlSafeContact,
                xmlSafeMask,
                xmlSafeMessage,
                unicode);

            // TLS 1.2 is supported by the target framework and the remote service.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            byte[] payload = Encoding.UTF8.GetBytes(xmlPostString);

            request.Method = "POST";
            request.ContentType = "text/xml; charset=utf-8";
            request.Accept = "text/xml";
            request.Headers.Add(
                "SOAPAction",
                "http://tempuri.org/ICorporateCBS/QuickSMS");
            request.ContentLength = payload.Length;
            request.Timeout = DefaultHttpTimeoutSeconds * 1000;
            request.ReadWriteTimeout = DefaultHttpTimeoutSeconds * 1000;

            try
            {
                using (Stream reqStream = request.GetRequestStream())
                {
                    reqStream.Write(payload, 0, payload.Length);
                }

                using (HttpWebResponse response =
                       (HttpWebResponse)request.GetResponse())
                using (Stream responseStream = response.GetResponseStream())
                using (StreamReader reader = responseStream == null
                           ? null
                           : new StreamReader(responseStream))
                {
                    string responseXml = reader == null
                        ? string.Empty
                        : reader.ReadToEnd();

                    Log.Information(
                        "SMS sent successfully. Transaction: {Transaction}, Destination: {Destination}, StatusCode: {StatusCode}, Response: {Response}",
                        transact,
                        MaskPhoneNumber(normalizedContact),
                        (int)response.StatusCode,
                        TruncateForLog(responseXml));

                    Console.WriteLine(responseXml);
                }
            }
            catch (WebException ex)
            {
                string errorText = ReadWebExceptionResponse(ex);

                Log.Error(
                    ex,
                    "SMS request failed. Transaction: {Transaction}, Destination: {Destination}, Response: {Response}",
                    transact,
                    MaskPhoneNumber(normalizedContact),
                    TruncateForLog(errorText));

                Console.WriteLine(ex);
                Console.WriteLine("Error: " + errorText);
                throw;
            }
        }

        
        private async Task CheckForWebOrder(CancellationToken cancellationToken = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCancellation.Token, cancellationToken);
            var token = linkedCts.Token;

            string query = @"
        SELECT UNIQUEID, WEBORDER
        FROM PJWEBORDER
        WHERE STATUS =0
          AND TRANSACT is null
          AND STOREID = " + SNUM;

            DataTable dt = await SqlHelper.SelectRecAsync(query).ConfigureAwait(false);

            if (dt == null || dt.Rows.Count == 0)
                return;

            foreach (DataRow dr in dt.Rows)
            {
                token.ThrowIfCancellationRequested();

                string uniqueId = GetDataRowString(dr, "UNIQUEID");
                string webOrderJson = GetDataRowString(dr, "WEBORDER");

                if (string.IsNullOrWhiteSpace(uniqueId))
                {
                    Log.Warning(
                        "Skipped PJWEBORDER row because UNIQUEID is empty.");

                    continue;
                }

                if (string.IsNullOrWhiteSpace(webOrderJson))
                {
                    Log.Warning(
                        "Skipped PJWEBORDER row because WEBORDER JSON is empty. UNIQUEID: {UniqueId}",
                        uniqueId);

                    continue;
                }

                WebRoot web;

                try
                {
                    var options = new System.Text.Json.JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true,
                        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
                    };
                    options.Converters.Add(new SystemTextJsonStringConverter());
                    options.Converters.Add(new WebAddressJsonConverter());
                    web = System.Text.Json.JsonSerializer.Deserialize<WebRoot>(webOrderJson, options);
                }
                catch (JsonException ex)
                {
                    Log.Error(
                        ex,
                        "Invalid WEBORDER JSON. UNIQUEID: {UniqueId}, JSON: {Json}",
                        uniqueId,
                        TruncateForLog(webOrderJson));

                    continue;
                }

                if (web == null)
                {
                    Log.Warning(
                        "WEBORDER JSON deserialized to null. UNIQUEID: {UniqueId}",
                        uniqueId);

                    continue;
                }

                await Task.Delay(500, token);

                await WebPlaceOrder(
                    uniqueId,
                    SNUM,
                    web);
            }
        }

        public async Task FPORDER(CancellationToken cancellationToken = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCancellation.Token, cancellationToken);
            var token = linkedCts.Token;

            string query = @"select UNIQUEID, final_xml, [order] from PJFPORDER where transact is null and storeid='"+ FOODPANDASNUM +"'";

            DataTable dt = await SqlHelper.SelectRecAsync(query).ConfigureAwait(false);

            if (dt == null || dt.Rows.Count == 0)
                return;

            foreach (DataRow dr in dt.Rows)
            {
                token.ThrowIfCancellationRequested();

                string uniqueId = GetDataRowString(dr, "UNIQUEID");
                string finalXml = GetDataRowString(dr, "final_xml");
                string fpOrderJson = GetDataRowString(dr, "ORDER");

                if (string.IsNullOrWhiteSpace(uniqueId))
                {
                    Log.Warning("Skipped PJFPORDER row because UNIQUEID is empty.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(fpOrderJson))
                {
                    Log.Warning("Skipped PJFPORDER row because ORDER JSON is empty. UNIQUEID: {UniqueId}", uniqueId);
                    continue;
                }

                FPRoot fpRoot;

                try
                {
                    var options = new System.Text.Json.JsonSerializerOptions 
                    { 
                        PropertyNameCaseInsensitive = true,
                        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
                    };
                    options.Converters.Add(new SystemTextJsonStringConverter());
                    fpRoot = System.Text.Json.JsonSerializer.Deserialize<FPRoot>(fpOrderJson, options);
                }
                catch (JsonException ex)
                {
                    Log.Error(
                        ex,
                        "Invalid PJFPORDER JSON. UNIQUEID: {UniqueId}, JSON: {Json}",
                        uniqueId,
                        TruncateForLog(fpOrderJson));

                    continue;
                }

                if (fpRoot == null)
                {
                    Log.Warning(
                        "PJFPORDER JSON deserialized to null. UNIQUEID: {UniqueId}",
                        uniqueId);

                    continue;
                }

                await Task.Delay(500, token);

                await FPPlaceOrder(uniqueId, finalXml, fpRoot);
            }
        }

        private async Task FPPlaceOrder(string uniqueid, string finalXml, FPRoot fpRoot)
        {
            if (string.IsNullOrWhiteSpace(uniqueid))
            {
                Log.Warning("FPPlaceOrder was skipped because UNIQUEID is missing.");
                return;
            }

            string processingKey = "FP:" + uniqueid.Trim();

            if (!_activeOrderIds.TryAdd(processingKey, 0))
            {
                Log.Debug("Foodpanda Order is already being processed and was skipped. UNIQUEID: {UniqueId}", uniqueid);
                return;
            }

            string referenceId = !string.IsNullOrWhiteSpace(fpRoot.Code) ? fpRoot.Code : uniqueid;
            string name = GetFPCustomerName(fpRoot);

            try
            {

                string safeReferenceId = EscapeSqlLiteral(referenceId.Trim());
                string query1 = @"SELECT TRANSACT
                                  FROM dba.posheader
                                  WHERE refid='" + safeReferenceId + @"'
                                    AND opendate=(SELECT MAX(opendate) FROM dba.posheader)";

                DataTable dtExist = await Task.Run(
                    () => ODBCHelper.SelectRec(query1),
                    _shutdownCancellation.Token);

                bool exist = dtExist != null && dtExist.Rows.Count > 0;

                if (exist)
                {
                    string transact = dtExist.Rows[0]["TRANSACT"] == DBNull.Value
                        ? "11"
                        : Convert.ToString(dtExist.Rows[0]["TRANSACT"]) ?? "11";

                    await FPupdatetabel(uniqueid, transact).ConfigureAwait(false);

                    AddToAllOrders(
                        "FP Order already exist, Transact: " + transact,
                        referenceId,
                        GetFPCustomerName(fpRoot));

                    _orderErrorCounts.TryRemove(processingKey, out int _);
                    return;
                }

                string response;
                if (!string.IsNullOrWhiteSpace(finalXml))
                {
                    response = await Task.Run(
                        () => FPOrderProcess.PlaceOrderFromXml(finalXml, uniqueid, referenceId),
                        _shutdownCancellation.Token);
                }
                else
                {
                    response = await Task.Run(
                        () => FPOrderProcess.PlaceOrderdt(fpRoot, uniqueid, referenceId),
                        _shutdownCancellation.Token);
                }

                if (!TryParsePosResponse(
                        response,
                        out int errorNum,
                        out string transNumber,
                        out string parseError))
                {
                    RegisterOrderProcessingError(
                        processingKey,
                        uniqueid,
                        referenceId,
                        parseError,
                        "0",
                        GetFPCustomerName(fpRoot));
                    return;
                }

                name = GetFPCustomerName(fpRoot);

                if (errorNum == 0)
                {
                    await FPupdatetabel(uniqueid, transNumber).ConfigureAwait(false);
                    AddToAllOrders("FP Order Placed Successfully: ", transNumber, name);

                    Log.Information(
                        "Foodpanda Order placed successfully. OrderId: {OrderId}, Transaction: {Transaction}",
                        referenceId,
                        transNumber);

                    _orderErrorCounts.TryRemove(processingKey, out int _);

                    if (IsIslamabadStore("FP", FOODPANDASNUM) && !IsFoodPandaCashOnDeliveryOr1001(fpRoot, finalXml))
                    {
                        await Task.Run(() => UpdatePosHeaderStatusForIslamabad("FP", transNumber)).ConfigureAwait(false);
                    }

                    // Post Foodpanda Order Accepted status update in background queue
                    string remoteOrderId = !string.IsNullOrWhiteSpace(fpRoot.Code) ? fpRoot.Code : fpRoot.Code;
                    string acceptedUrl = fpRoot.CallbackUrls?.OrderAcceptedUrl;

                    if (!string.IsNullOrWhiteSpace(remoteOrderId) && !string.IsNullOrWhiteSpace(acceptedUrl))
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                string accessToken = await GetFPAccessTokenAsync().ConfigureAwait(false);
                                await PostFPOrderStatusAsync(remoteOrderId, acceptedUrl, accessToken).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                Log.Error(ex, "Failed to update Foodpanda order status in background for RemoteOrderId: {RemoteOrderId}", remoteOrderId);
                            }
                        });
                    }
                    else
                    {
                        Log.Warning("Foodpanda status update skipped: RemoteOrderId or OrderAcceptedUrl missing for OrderId: {OrderId}", referenceId);
                    }
                }
                else
                {
                    RegisterOrderProcessingError(
                        processingKey,
                        uniqueid,
                        referenceId,
                        "PixelPoint Error #" + errorNum,
                        "0",
                        name);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unhandled exception in FPPlaceOrder for UNIQUEID: {UniqueId}", uniqueid);
                RegisterOrderProcessingError(
                    processingKey,
                    uniqueid,
                    referenceId,
                    ex.Message,
                    "0",
                    name,
                    ex);
            }
            finally
            {
                _activeOrderIds.TryRemove(processingKey, out byte _);
            }
        }

        public async Task CheckForCallCenterOrder(CancellationToken cancellationToken = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCancellation.Token, cancellationToken);
            var token = linkedCts.Token;

            string ccsnum = App.CCSNUM;
            if (string.IsNullOrWhiteSpace(ccsnum))
            {
                ccsnum = "21";
            }

            Log.Information("Checking PJCCORDER for pending CallCenter orders with STOREID: {StoreId}...", ccsnum);

            string query = @"select * from PJCCORDER WHERE STOREID=" + ccsnum + @" AND TRANSACT IS NULL AND STATUS=0 ORDER BY 1 DESC";

            DataTable dt = await SqlHelper.SelectRecAsync(query).ConfigureAwait(false);

            if (dt == null || dt.Rows.Count == 0)
            {
                Log.Debug("No pending CallCenter orders found in PJCCORDER for STOREID {StoreId}.", ccsnum);
                return;
            }

            Log.Information("Found {Count} pending CallCenter order(s) in PJCCORDER for STOREID {StoreId}.", dt.Rows.Count, ccsnum);

            foreach (DataRow dr in dt.Rows)
            {
                token.ThrowIfCancellationRequested();

                string uniqueId = GetDataRowString(dr, "UNIQUEID");
                if (string.IsNullOrWhiteSpace(uniqueId))
                {
                    uniqueId = GetDataRowString(dr, "uniqueid");
                }

                string webOrderJson = GetDataRowString(dr, "WEBORDER");
                if (string.IsNullOrWhiteSpace(webOrderJson))
                {
                    webOrderJson = GetDataRowString(dr, "ORDER");
                }
                if (string.IsNullOrWhiteSpace(webOrderJson))
                {
                    webOrderJson = GetDataRowString(dr, "order");
                }
                if (string.IsNullOrWhiteSpace(webOrderJson))
                {
                    webOrderJson = GetDataRowString(dr, "orderdetail");
                }

                string finalXml = GetDataRowString(dr, "final_xml");
                if (string.IsNullOrWhiteSpace(finalXml))
                {
                    finalXml = GetDataRowString(dr, "FINAL_XML");
                }

                if (string.IsNullOrWhiteSpace(uniqueId))
                {
                    Log.Warning("Skipped PJCCORDER row because UNIQUEID is empty.");
                    continue;
                }

                CallCenterOrderRoot? callCenterRoot = null;

                if (!string.IsNullOrWhiteSpace(webOrderJson))
                {
                    try
                    {
                        var options = new System.Text.Json.JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString | System.Text.Json.Serialization.JsonNumberHandling.WriteAsString
                        };

                        callCenterRoot = System.Text.Json.JsonSerializer.Deserialize<CallCenterOrderRoot>(webOrderJson, options);
                    }
                    catch (JsonException ex)
                    {
                        Log.Error(ex, "Invalid PJCCORDER JSON. UNIQUEID: {UniqueId}, JSON: {Json}", uniqueId, TruncateForLog(webOrderJson));
                    }
                }

                if (callCenterRoot == null && string.IsNullOrWhiteSpace(finalXml))
                {
                    Log.Warning("PJCCORDER row skipped: both JSON and final_xml are empty or invalid. UNIQUEID: {UniqueId}", uniqueId);
                    continue;
                }

                await Task.Delay(500, token);

                await CallCenterPlaceOrder(uniqueId, ccsnum, callCenterRoot, finalXml).ConfigureAwait(false);
            }
        }

        private async Task CallCenterPlaceOrder(string uniqueid, string ccsnum, CallCenterOrderRoot? callCenterRoot, string? existingFinalXml = null)
        {
            if (string.IsNullOrWhiteSpace(uniqueid))
            {
                Log.Warning("CallCenterPlaceOrder skipped because UNIQUEID is missing.");
                return;
            }

            string processingKey = "CC:" + uniqueid.Trim();

            if (!_activeOrderIds.TryAdd(processingKey, 0))
            {
                Log.Debug("CallCenter order is already being processed and was skipped. UniqueId: {UniqueId}", uniqueid);
                return;
            }

            string referenceId = (callCenterRoot != null && !string.IsNullOrWhiteSpace(callCenterRoot.order_number))
                ? callCenterRoot.order_number
                : uniqueid;

            string custName = GetCallCenterCustomerName(callCenterRoot, existingFinalXml);

            try
            {
                Log.Information("Starting processing for CallCenter Order. UNIQUEID: {UniqueId}, ReferenceId: {ReferenceId}", uniqueid, referenceId);

                string safeReferenceId = EscapeSqlLiteral(referenceId.Trim());
                string queryCheck = @"SELECT TRANSACT FROM dba.posheader WHERE refid='" + safeReferenceId + @"' AND opendate=(SELECT MAX(opendate) FROM dba.posheader)";

                DataTable dtExist = await Task.Run(() => ODBCHelper.SelectRec(queryCheck), _shutdownCancellation.Token);

                bool exist = dtExist != null && dtExist.Rows.Count > 0;

                if (exist)
                {
                    string transact = dtExist.Rows[0]["TRANSACT"] == DBNull.Value ? "11" : Convert.ToString(dtExist.Rows[0]["TRANSACT"]) ?? "11";

                    Log.Information("CallCenter Order already exists in PixelPoint POS. ReferenceId: {ReferenceId}, Transact: {Transact}", referenceId, transact);
                    await CallCenterupdatetabel(uniqueid, transact, existingFinalXml).ConfigureAwait(false);

                    AddToAllOrders("CallCenter Order already exist, Transact: " + transact, referenceId, custName);

                    _orderErrorCounts.TryRemove(processingKey, out int _);

                    string safeRefIdForStatus = referenceId;
                    string safeTransactForStatus = transact;
                    _ = Task.Run(async () =>
                    {
                        await CallCenterOrderProcess.PostCallCenterOrderStatusAsync(safeRefIdForStatus, "accepted", safeTransactForStatus).ConfigureAwait(false);
                    });

                    return;
                }

                string response;

                if (!string.IsNullOrWhiteSpace(existingFinalXml))
                {
                    Log.Information("Sending pre-generated final_xml for CallCenter Order {ReferenceId} to PixelPoint POS COM...", referenceId);
                    response = await Task.Run(
                        () => CallCenterOrderProcess.PlaceOrderFromXml(existingFinalXml, uniqueid, referenceId),
                        _shutdownCancellation.Token);
                }
                else if (callCenterRoot != null)
                {
                    string generatedXml = CreateXML.CreateCallCenterXMLdt(callCenterRoot);
                    Log.Information("Generated new PixelPoint XML for CallCenter Order {ReferenceId}.", referenceId);

                    if (!string.IsNullOrWhiteSpace(generatedXml))
                    {
                        await SaveCallCenterFinalXml(uniqueid, generatedXml).ConfigureAwait(false);
                    }

                    Log.Information("Sending CallCenter Order XML to PixelPoint POS COM interface. ReferenceId: {ReferenceId}...", referenceId);
                    response = await Task.Run(
                        () => CallCenterOrderProcess.PlaceOrderdt(callCenterRoot, uniqueid, referenceId),
                        _shutdownCancellation.Token);
                }
                else
                {
                    Log.Warning("Cannot place CallCenter order because both finalXml and callCenterRoot are missing. UNIQUEID: {UniqueId}", uniqueid);
                    return;
                }

                custName = GetCallCenterCustomerName(callCenterRoot, existingFinalXml);

                if (!TryParsePosResponse(response, out int errorNum, out string transNumber, out string parseError))
                {
                    Log.Error("Failed to parse PixelPoint response for CallCenter Order {ReferenceId}. Response: {Response}", referenceId, response);
                    RegisterOrderProcessingError(processingKey, uniqueid, referenceId, parseError, "0", custName);
                    return;
                }

                if (errorNum == 0)
                {
                    Log.Information("CallCenter Order placed successfully in PixelPoint POS! ReferenceId: {ReferenceId}, TransactionNo: {TransNo}", referenceId, transNumber);
                    await CallCenterupdatetabel(uniqueid, transNumber, existingFinalXml).ConfigureAwait(false);
                    AddToAllOrders("CallCenter Order Placed Successfully: ", transNumber, custName);

                    _orderErrorCounts.TryRemove(processingKey, out int _);

                    if (IsIslamabadStore("CALLCENTER", ccsnum) && !IsCallCenterCashOnDeliveryOr1001(callCenterRoot, existingFinalXml))
                    {
                        await Task.Run(() => UpdatePosHeaderStatusForIslamabad("CALLCENTER", transNumber)).ConfigureAwait(false);
                    }

                    string safeRefIdForStatus = referenceId;
                    string safeTransNumberForStatus = transNumber;
                    _ = Task.Run(async () =>
                    {
                        await CallCenterOrderProcess.PostCallCenterOrderStatusAsync(safeRefIdForStatus, "accepted", safeTransNumberForStatus).ConfigureAwait(false);
                    });
                }
                else
                {
                    Log.Error("PixelPoint POS returned error #{ErrorNum} for CallCenter Order {ReferenceId}. Response: {Response}", errorNum, referenceId, response);
                    RegisterOrderProcessingError(processingKey, uniqueid, referenceId, "PixelPoint Error #" + errorNum, "0", custName);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Unhandled exception in CallCenterPlaceOrder for UNIQUEID: {UniqueId}", uniqueid);
                RegisterOrderProcessingError(processingKey, uniqueid, referenceId, ex.Message, "0", custName, ex);
            }
            finally
            {
                _activeOrderIds.TryRemove(processingKey, out byte _);
            }
        }

        private async Task CheckForTabOrder(CancellationToken cancellationToken = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCancellation.Token, cancellationToken);
            var token = linkedCts.Token;

            string query = @"SELECT *
                             FROM PJGOLOTLO
                             WHERE STATUS IS NULL
                               AND TRANSACT IS NULL
                               AND STOREID=" + TABSNUM;
            DataTable dt = await SqlHelper.SelectRecHubRiseAsync(query).ConfigureAwait(false);
            if (dt == null || dt.Rows.Count == 0)
                return;
            foreach (DataRow dr in dt.Rows)
            {
                token.ThrowIfCancellationRequested();

                string uniqueId = GetDataRowString(dr, "UNIQUEID");
                string orderDetail = GetDataRowString(dr, "ORDERDETAIL");

                if (string.IsNullOrWhiteSpace(uniqueId) ||
                    string.IsNullOrWhiteSpace(orderDetail))
                {
                    Log.Warning(
                        "Skipped an invalid PJGOLOTLO row. UNIQUEID: {UniqueId}",
                        uniqueId);
                    continue;
                }
                await Task.Delay(500, token);
                await PlaceTabOrder(uniqueId, orderDetail, SNUM);
            }
        }

        private async Task METHODFORMSG()
        {
            string query = @"SELECT
             ph.transact,
             CASE
                 WHEN LEFT(mm.hometele, 1) = '+'
                 THEN SUBSTRING(mm.hometele, 2, LEN(mm.hometele) - 1)
                 ELSE mm.hometele
             END AS hometele
         FROM posheader ph
         JOIN member mm ON ph.memcode = mm.memcode
         WHERE saletypeindex IN (2055, 734005201)
         AND (ph.extrarefid IS NULL OR ph.extrarefid = '' OR ph.extrarefid <> 1)
         AND opendate = (SELECT MAX(opendate) FROM posheader)
         AND timestart <= DATEADD(minute, -50, GETDATE())";

            DataTable dt = await Task.Run(
                () => ODBCHelper.SelectRec(query),
                _shutdownCancellation.Token);

            if (dt == null || dt.Rows.Count == 0)
                return;

            string msg = @"SELECT WEBTOKEN FROM accestokens WHERE SNUM=999";
            DataTable mes = await Task.Run(
                () => SqlHelper.SelectRec(msg),
                _shutdownCancellation.Token);

            string message = string.Empty;

            if (mes != null)
            {
                foreach (DataRow drmsg in mes.Rows)
                {
                    message = GetDataRowString(drmsg, "WEBTOKEN");
                }
            }

            foreach (DataRow dr in dt.Rows)
            {
                _shutdownCancellation.Token.ThrowIfCancellationRequested();

                string phone = GetDataRowString(dr, "hometele");
                string transaction = GetDataRowString(dr, "transact");

                if (string.IsNullOrWhiteSpace(phone) ||
                    string.IsNullOrWhiteSpace(transaction))
                {
                    continue;
                }

                await Task.Run(
                    () => SendSms(phone, transaction, message),
                    _shutdownCancellation.Token);

                updateposheadermsg(transaction);
            }
        }

        public void updateposheadermsg(string orderid)
        {
            if (!long.TryParse(orderid, out long transactionNumber))
            {
                Log.Warning(
                    "POSHEADER SMS flag was not updated because TRANSACT is invalid: {Transaction}",
                    orderid);
                return;
            }

            string query = @"UPDATE POSHEADER
                             SET extrarefid = 1
                             WHERE TRANSACT = ?";

            ODBCHelper.CURD(
                query,
                new OdbcParameter("TRANSACT", OdbcType.BigInt)
                {
                    Value = transactionNumber
                });
        }

        #region Islamabad Store & POSHEADER Update Helpers

        private static readonly HashSet<string> IslamabadFpStoreIds = new(StringComparer.OrdinalIgnoreCase)
        {
            "zr6vz-12", "zr6vz-15", "zr6vz-19", "zr6vz-24", "zr6vz-26",
            "16", "23", "24", "28", "31"
        };

        private static readonly HashSet<string> IslamabadWebStoreIds = new(StringComparer.OrdinalIgnoreCase)
        {
            "13200", "13362", "13628", "13650", "13798",
            "16", "23", "24", "28", "31"
        };

        private static readonly HashSet<string> IslamabadCcStoreIds = new(StringComparer.OrdinalIgnoreCase)
        {
            "16", "23", "24", "28", "31","10001",
            "13200", "13362", "13628", "13650", "13798"
        };

        private static readonly HashSet<string> AllIslamabadIdentifiers = new(StringComparer.OrdinalIgnoreCase)
        {
            "16", "23", "24", "28", "31",
            "zr6vz-12", "zr6vz-15", "zr6vz-19", "zr6vz-24", "zr6vz-26",
            "13200", "13362", "13628", "13650", "13798"
        };

        private bool IsIslamabadStore(string orderType, string? storeId = null)
        {
            string configuredStore = storeId;
            if (string.IsNullOrWhiteSpace(configuredStore))
            {
                configuredStore = orderType switch
                {
                    "WEB" => SNUM.ToString(),
                    "CC" or "CALLCENTER" => App.CCSNUM,
                    "FP" or "FOODPANDA" => FOODPANDASNUM,
                    _ => string.Empty
                };
            }

            if (!string.IsNullOrWhiteSpace(configuredStore))
            {
                string trimmed = configuredStore.Trim();
                if ((orderType == "FP" || orderType == "FOODPANDA") && IslamabadFpStoreIds.Contains(trimmed))
                {
                    return true;
                }
                if (orderType == "WEB" && IslamabadWebStoreIds.Contains(trimmed))
                {
                    return true;
                }
                if ((orderType == "CC" || orderType == "CALLCENTER") && IslamabadCcStoreIds.Contains(trimmed))
                {
                    return true;
                }

                if (AllIslamabadIdentifiers.Contains(trimmed))
                {
                    return true;
                }
            }

            if (SNUM > 0 && AllIslamabadIdentifiers.Contains(SNUM.ToString())) return true;
            if (!string.IsNullOrWhiteSpace(FOODPANDASNUM) && AllIslamabadIdentifiers.Contains(FOODPANDASNUM.Trim())) return true;
            if (!string.IsNullOrWhiteSpace(App.CCSNUM) && AllIslamabadIdentifiers.Contains(App.CCSNUM.Trim())) return true;
            if (!string.IsNullOrWhiteSpace(App.SNUM) && AllIslamabadIdentifiers.Contains(App.SNUM.Trim())) return true;
            if (!string.IsNullOrWhiteSpace(App.FPSNUM) && AllIslamabadIdentifiers.Contains(App.FPSNUM.Trim())) return true;

            return false;
        }

        private static bool IsWebCashOnDeliveryOr1001(WebRoot? web)
        {
            if (web == null) return true;

            if (web.Payment != null)
            {
                string remoteCode = web.Payment.RemoteCode?.Trim() ?? string.Empty;
                string paymentType = web.Payment.Type?.Trim() ?? string.Empty;

                if (remoteCode == "1001" ||
                    paymentType.Equals("cash", StringComparison.OrdinalIgnoreCase) ||
                    paymentType.Equals("cash_on_delivery", StringComparison.OrdinalIgnoreCase) ||
                    paymentType.Equals("cod", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (!string.IsNullOrWhiteSpace(remoteCode) && remoteCode != "1001")
                {
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(paymentType) &&
                    !paymentType.Equals("cash", StringComparison.OrdinalIgnoreCase) &&
                    !paymentType.Equals("cash_on_delivery", StringComparison.OrdinalIgnoreCase) &&
                    !paymentType.Equals("cod", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsCallCenterCashOnDeliveryOr1001(CallCenterOrderRoot? root, string? finalXml = null)
        {
            if (root != null)
            {
                string paymentMethod = root.payment_method_index?.ToString()?.Trim() ?? string.Empty;
                if (paymentMethod == "1001") return true;

                if (!string.IsNullOrWhiteSpace(paymentMethod) && paymentMethod != "1001")
                {
                    return false;
                }

                string orderMode = root.order_mode?.Trim() ?? string.Empty;
                if (orderMode.Equals("cash", StringComparison.OrdinalIgnoreCase) ||
                    orderMode.Equals("cod", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(finalXml))
            {
                if (finalXml.Contains("<MethodNumber>1001</MethodNumber>"))
                {
                    return true;
                }
                if (finalXml.Contains("<MethodNumber>"))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsFoodPandaCashOnDeliveryOr1001(FPRoot? root, string? finalXml = null)
        {
            if (root?.Payment != null)
            {
                string remoteCode = root.Payment.RemoteCode?.Trim() ?? string.Empty;
                string paymentType = root.Payment.Type?.Trim() ?? string.Empty;

                if (remoteCode == "1001" ||
                    paymentType.IndexOf("cash", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    paymentType.IndexOf("cod", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(finalXml) && finalXml.Contains("<MethodNumber>1001</MethodNumber>"))
            {
                return true;
            }

            return false;
        }

        private void UpdatePosHeaderStatusForIslamabad(string orderType, string transact)
        {
            if (string.IsNullOrWhiteSpace(transact) || transact == "11" || transact == "0")
            {
                Log.Warning("Skipped updating POSHEADER for {OrderType} because TRANSACT is invalid: '{Transact}'", orderType, transact);
                return;
            }

            if (!long.TryParse(transact.Trim(), out long transactionNumber))
            {
                Log.Warning("Skipped updating POSHEADER for {OrderType} because TRANSACT '{Transact}' is not a valid number.", orderType, transact);
                return;
            }

            try
            {
                string query = @"UPDATE POSHEADER
                                 SET WHOCLOSE = 2146435098,
                                     STATUS = 3
                                 WHERE TRANSACT = ?";

                ODBCHelper.CURD(
                    query,
                    new OdbcParameter("TRANSACT", OdbcType.BigInt)
                    {
                        Value = transactionNumber
                    });

                Log.Information(
                    "Executed Islamabad POSHEADER update (WHOCLOSE=2146435098, STATUS=3) for {OrderType} order. TRANSACT: {Transact}",
                    orderType,
                    transactionNumber);
            }
            catch (Exception ex)
            {
                Log.Error(
                    ex,
                    "Error executing POSHEADER update (WHOCLOSE=2146435098, STATUS=3) for {OrderType} order. TRANSACT: {Transact}",
                    orderType,
                    transact);
            }
        }

        #endregion

        private async Task CheckForCancelOrder(CancellationToken cancellationToken = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCancellation.Token, cancellationToken);
            var token = linkedCts.Token;

            string query = @"SELECT transact
                             FROM dba.posheader
                             WHERE saletypeindex IN (1002,1003,2055,734005201)
                               AND opendate = (SELECT MAX(opendate) FROM dba.posheader)
                               AND transact IN (
                                   SELECT transact
                                   FROM dba.posdetail
                                   WHERE prodtype = 101
                               )
                             ORDER BY 1 DESC";

            DataTable dt = await Task.Run(
                () => ODBCHelper.SelectRec(query),
                token);

            if (dt == null || dt.Rows.Count == 0)
                return;

            foreach (DataRow dr in dt.Rows)
            {
                _shutdownCancellation.Token.ThrowIfCancellationRequested();

                string transact = GetDataRowString(dr, "transact");

                if (!long.TryParse(transact, out long transactionNumber))
                {
                    Log.Warning(
                        "Cancelled POS transaction was skipped because TRANSACT is invalid: {Transaction}",
                        transact);
                    continue;
                }

                string oraclequery = @"SELECT orderid
                                       FROM EOIHQ
                                       WHERE STATUS <> 9
                                         AND transact=" + transactionNumber +
                                      " AND snum=" + HUBRISESNUM;

                DataTable dtoracle = await SqlHelper.SelectRecHubRiseAsync(oraclequery).ConfigureAwait(false);

                if (dtoracle == null)
                    continue;

                foreach (DataRow DTORC in dtoracle.Rows)
                {
                    string ORDERID = GetDataRowString(DTORC, "orderid");

                    if (string.IsNullOrWhiteSpace(ORDERID))
                        continue;

                    bool remoteUpdated = await UpdateCancelOrderStatusAsync(
                        ORDERID,
                        HUBRISESNUM,
                        transact);

                    if (remoteUpdated)
                    {
                        await updatecancelordertabel(ORDERID, HUBRISESNUM).ConfigureAwait(false);
                    }
                    else
                    {
                        Log.Warning(
                            "Cancelled order remains pending locally because HubRise update failed. OrderId: {OrderId}, Transaction: {Transaction}",
                            ORDERID,
                            transact);
                    }
                }
            }
        }
         private async Task WebPlaceOrder(string uniqueid, int snum,WebRoot web)
        {
            if (string.IsNullOrWhiteSpace(uniqueid))

            {
                Log.Warning(
                    "PlaceOrder was skipped because identifiers are missing. UNIQUEID: {UniqueId},",
                    uniqueid
                    );
                return;
            }

            string processingKey = snum + ":" + uniqueid.Trim();

            if (!_activeOrderIds.TryAdd(processingKey, 0))
            {
                Log.Debug(
                    "Order is already being processed and was skipped. OrderId: {OrderId}}",
                    
                    snum);
                return;
            }

            string custName = GetWebCustomerName(web);

            try
            {
                _accessTokenFailureCounts.TryRemove(processingKey, out int ignoredAccessFailures);
                accecctoken = 0;

                string referenceId = web.Code > 0 ? web.Code.ToString() : uniqueid;

                if (string.IsNullOrWhiteSpace(referenceId))
                {
                    RegisterOrderProcessingError(
                        processingKey,
                        uniqueid,
                        uniqueid,
                        "Reference ID is empty.",
                        "0",
                        custName);
                    return;
                }

                string safeReferenceId = EscapeSqlLiteral(referenceId.Trim());
                string query1 = @"SELECT TRANSACT
                                  FROM dba.posheader
                                  WHERE refid='" + safeReferenceId + @"'
                                    AND opendate=(SELECT MAX(opendate) FROM dba.posheader)";

                DataTable dtExist = await Task.Run(
                    () => ODBCHelper.SelectRec(query1),
                    _shutdownCancellation.Token);

                bool exist = dtExist != null && dtExist.Rows.Count > 0;

                if (exist)
                {
                    string transact = dtExist.Rows[0]["TRANSACT"] == DBNull.Value
                        ? "11"
                        : Convert.ToString(dtExist.Rows[0]["TRANSACT"]) ?? "11";

                    await Webupdatetabel(uniqueid, transact).ConfigureAwait(false);

                    AddToAllOrders(
                        "Web Order already exist, Transact: " + transact,
                        web.Code.ToString(),
                        custName);

                    _orderErrorCounts.TryRemove(processingKey, out int ignoredExistingErrors);
                    ORDERERROR = 0;
                    return;
                }

                string response = await Task.Run(
                    () => WebOrderProcess.PlaceOrderdt(web, uniqueid, web.Code.ToString()),
                    _shutdownCancellation.Token);

                if (!TryParsePosResponse(
                        response,
                        out int errorNum,
                        out string transNumber,
                        out string parseError))
                {
                    RegisterOrderProcessingError(
                        processingKey,
                        uniqueid,
                        web.Code.ToString(),
                        parseError,
                        "0",
                        custName);
                    return;
                }

                string name = custName;

                if (errorNum == 0)
                {
                    await Webupdatetabel(uniqueid, transNumber).ConfigureAwait(false);
                    AddToAllOrders("Web Order Placed Successfully: ", transNumber, name);

                    Log.Information(
                        "Order placed successfully. OrderId: {OrderId}, Transaction: {Transaction}",
                        web.Code.ToString(),
                        transNumber);

                    if (IsIslamabadStore("WEB", SNUM.ToString()) && !IsWebCashOnDeliveryOr1001(web))
                    {
                        await Task.Run(() => UpdatePosHeaderStatusForIslamabad("WEB", transNumber)).ConfigureAwait(false);
                    }

                    if (_vendorOrderStatusService != null)
                    {
                        string webOrderId = web.Code.ToString();
                        bool vendorStatusSent = await _vendorOrderStatusService.UpdateOrderStatusAsync(webOrderId, "accepted").ConfigureAwait(false);
                        Log.Information("Vendor Order Status 'accepted' update for OrderId {OrderId} result: {Result}", webOrderId, vendorStatusSent);
                    }

                    _orderErrorCounts.TryRemove(processingKey, out int ignoredSuccessErrors);
                    ORDERERROR = 0;
                    accecctoken = 0;
                }
                else
                {
                    RegisterOrderProcessingError(
                        processingKey,
                        uniqueid,
                        web.Code.ToString(),
                        "POS returned ErrorNum " + errorNum + ".",
                        transNumber,
                        name);
                }
            }
            catch (OperationCanceledException) when (_shutdownCancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                RegisterOrderProcessingError(
                    processingKey,
                    uniqueid,
                    web.Code.ToString(),
                    ex.Message,
                    "0",
                    custName,
                    ex);
            }
            finally
            {
                _activeOrderIds.TryRemove(processingKey, out byte ignored);
            }
        }

        private async Task PlaceTabOrder(string uniqueid, string orderdetail, int snum)
        {
            if (string.IsNullOrWhiteSpace(uniqueid) ||
                string.IsNullOrWhiteSpace(orderdetail))
            {
                Log.Warning(
                    "PlaceTabOrder was skipped because UNIQUEID or ORDERDETAIL is empty. UNIQUEID: {UniqueId}",
                    uniqueid);
                return;
            }

            if (!_activeTabOrderIds.TryAdd(uniqueid, 0))
            {
                Log.Debug(
                    "Tab order is already being processed and was skipped. UNIQUEID: {UniqueId}",
                    uniqueid);
                return;
            }

            try
            {
                TabOrderRoot order = JsonConvert.DeserializeObject<TabOrderRoot>(orderdetail);

                if (order == null)
                {
                    RegisterTabOrderProcessingError(
                        uniqueid,
                        "Tab-order JSON deserialized to null.",
                        "0",
                        "Tab Customer");
                    return;
                }

                string saleTypeNum = order.orderType;
                string referenceId;

                if (saleTypeNum == "1002" || saleTypeNum == "1003")
                {
                    // Original mapping retained:
                    // referenceId = test.id;
                    referenceId = order.order_number;
                }
                else
                {
                    // Original mapping retained:
                    // referenceId = test.@ref + "-" + test.collection_code;
                    referenceId = order.order_number;
                }

                // Original status-update call retained:
                // UpdateOrderStatusAsync(orderid, SNUM, "11");

                if (string.IsNullOrWhiteSpace(referenceId))
                {
                    RegisterTabOrderProcessingError(
                        uniqueid,
                        "Tab-order reference ID is empty.",
                        "0",
                        GetTabCustomerName(order));
                    return;
                }

                string safeReferenceId = EscapeSqlLiteral(referenceId.Trim());
                string query1 = @"SELECT TRANSACT
                                  FROM dba.posheader
                                  WHERE refid='" + safeReferenceId + @"'
                                    AND opendate=(SELECT MAX(opendate) FROM dba.posheader)";

                DataTable existingTransactions = await Task.Run(
                    () => ODBCHelper.SelectRec(query1),
                    _shutdownCancellation.Token);

                bool exist = existingTransactions != null &&
                             existingTransactions.Rows.Count > 0;

                if (exist)
                {
                    string existingTransaction =
                        GetDataRowString(existingTransactions.Rows[0], "TRANSACT");

                    if (string.IsNullOrWhiteSpace(existingTransaction))
                        existingTransaction = "11";

                    await updateTabtabel(uniqueid, existingTransaction).ConfigureAwait(false);

                    Log.Information(
                        "Existing tab order synchronized. UNIQUEID: {UniqueId}, Transaction: {Transaction}",
                        uniqueid,
                        existingTransaction);
                    return;
                }

                string response = await Task.Run(
                    () => TabOrderProcess.TabPlaceOrderdt(order, uniqueid, "11"),
                    _shutdownCancellation.Token);

                if (!TryParsePosResponse(
                        response,
                        out int errorNum,
                        out string transNumber,
                        out string parseError))
                {
                    RegisterTabOrderProcessingError(
                        uniqueid,
                        parseError,
                        "0",
                        GetTabCustomerName(order));
                    return;
                }

                string name = GetTabCustomerName(order);

                if (errorNum == 0)
                {
                    await updateTabtabel(uniqueid, transNumber).ConfigureAwait(false);
                    AddToAllOrders("Tab Order Placed Successfully: ", transNumber, name);

                    Log.Information(
                        "Tab order placed successfully. UNIQUEID: {UniqueId}, Transaction: {Transaction}",
                        uniqueid,
                        transNumber);

                    // Original HubRise status call retained:
                    // UpdateOrderStatusAsync(orderid, SNUM, transNumber);

                    UPDATETABLEFORTAB(transNumber, order.table_id);
                    accecctoken = 0;
                    ORDERERROR = 0;
                }
                else
                {
                    RegisterTabOrderProcessingError(
                        uniqueid,
                        "POS returned ErrorNum " + errorNum + ".",
                        transNumber,
                        name);
                }
            }
            catch (JsonException ex)
            {
                RegisterTabOrderProcessingError(
                    uniqueid,
                    "Invalid tab-order JSON: " + ex.Message,
                    "0",
                    "Tab Customer",
                    ex);
            }
            catch (OperationCanceledException) when (_shutdownCancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                RegisterTabOrderProcessingError(
                    uniqueid,
                    ex.Message,
                    "0",
                    "Tab Customer",
                    ex);
            }
            finally
            {
                _activeTabOrderIds.TryRemove(uniqueid, out byte ignored);
            }
        }

        public async Task<bool> UpdateOrderStatusAsync(
            string orderId,
            int SNUM,
            string TRANSACT,
            int retryCount = 4)
        {
            if (string.IsNullOrWhiteSpace(orderId))
                throw new ArgumentException("Order ID is required.", nameof(orderId));

            string cacheKey = $"{orderId.Trim().ToUpperInvariant()}:{(TRANSACT ?? string.Empty).Trim()}";
            if (_sentHubRiseStatusCache.ContainsKey(cacheKey))
            {
                Log.Debug("Skipping duplicate HubRise status update for OrderId '{OrderId}' with Transact '{Transact}'.", orderId, TRANSACT);
                return true;
            }

            if (retryCount < 1)
                retryCount = 1;

            int attempt = 0;

            while (attempt < retryCount)
            {
                _shutdownCancellation.Token.ThrowIfCancellationRequested();

                try
                {
                    string accessToken = await _tokenmanager.GetAccessToken(SNUM);

                    if (string.IsNullOrWhiteSpace(accessToken))
                    {
                        throw new InvalidOperationException(
                            "HubRise access token is empty for SNUM " + SNUM + ".");
                    }

                    string apiEndpoint =
                        "v1/location/orders/" + Uri.EscapeDataString(orderId.Trim());

                    var requestBody = new
                    {
                        status = "received",
                        custom_fields = new
                        {
                            epos = new
                            {
                                order_id = TRANSACT
                            }
                        }
                    };

                    string json = JsonConvert.SerializeObject(requestBody);

                    using (HttpRequestMessage request =
                           new HttpRequestMessage(HttpMethod.Put, apiEndpoint))
                    using (StringContent content =
                           new StringContent(json, Encoding.UTF8, "application/json"))
                    {
                        request.Content = content;
                        request.Headers.TryAddWithoutValidation(
                            "X-Access-Token",
                            accessToken);

                        using (HttpResponseMessage response =
                               await _hubRiseHttpClient.SendAsync(
                                   request,
                                   HttpCompletionOption.ResponseContentRead,
                                   _shutdownCancellation.Token))
                        {
                            string responseText =
                                await response.Content.ReadAsStringAsync();

                            if (response.IsSuccessStatusCode)
                            {
                                _sentHubRiseStatusCache.TryAdd(cacheKey, 0);
                                Log.Information(
                                    "HubRise order status updated. OrderId: {OrderId}, Transaction: {Transaction}, Attempt: {Attempt}",
                                    orderId,
                                    TRANSACT,
                                    attempt + 1);
                                return true;
                            }

                            Log.Warning(
                                "Failed to update HubRise order status. OrderId: {OrderId}, Attempt: {Attempt}, StatusCode: {StatusCode}, Response: {Response}",
                                orderId,
                                attempt + 1,
                                (int)response.StatusCode,
                                TruncateForLog(responseText));

                            if (!IsTransientStatusCode(response.StatusCode))
                                return false;
                        }
                    }
                }
                catch (OperationCanceledException) when (_shutdownCancellation.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log.Warning(
                        ex,
                        "Exception while updating HubRise order status. OrderId: {OrderId}, Attempt: {Attempt}",
                        orderId,
                        attempt + 1);
                }

                attempt++;

                if (attempt < retryCount)
                {
                    await DelayBeforeRetryAsync(attempt);
                }
            }

            return false;
        }

        public async Task<bool> UpdateCancelOrderStatusAsync(
            string orderId,
            int SNUM,
            string TRANSACT,
            int retryCount = 4)
        {
            if (string.IsNullOrWhiteSpace(orderId))
                throw new ArgumentException("Order ID is required.", nameof(orderId));

            if (retryCount < 1)
                retryCount = 1;

            int attempt = 0;

            while (attempt < retryCount)
            {
                _shutdownCancellation.Token.ThrowIfCancellationRequested();

                try
                {
                    string accessToken = await _tokenmanager.GetAccessToken(SNUM);

                    if (string.IsNullOrWhiteSpace(accessToken))
                    {
                        throw new InvalidOperationException(
                            "HubRise access token is empty for SNUM " + SNUM + ".");
                    }

                    string apiEndpoint =
                        "v1/location/orders/" + Uri.EscapeDataString(orderId.Trim());

                    var requestBody = new
                    {
                        status = "delivery_failed",
                        custom_fields = new
                        {
                            epos = new
                            {
                                rejection = new
                                {
                                    cause = "Customer Didn't Respond"
                                }
                            }
                        }
                    };

                    string json = JsonConvert.SerializeObject(requestBody);

                    using (HttpRequestMessage request =
                           new HttpRequestMessage(HttpMethod.Put, apiEndpoint))
                    using (StringContent content =
                           new StringContent(json, Encoding.UTF8, "application/json"))
                    {
                        request.Content = content;
                        request.Headers.TryAddWithoutValidation(
                            "X-Access-Token",
                            accessToken);

                        using (HttpResponseMessage response =
                               await _hubRiseHttpClient.SendAsync(
                                   request,
                                   HttpCompletionOption.ResponseContentRead,
                                   _shutdownCancellation.Token))
                        {
                            string responseText =
                                await response.Content.ReadAsStringAsync();

                            if (response.IsSuccessStatusCode)
                            {
                                Log.Information(
                                    "HubRise cancelled-order status updated. OrderId: {OrderId}, Transaction: {Transaction}, Attempt: {Attempt}",
                                    orderId,
                                    TRANSACT,
                                    attempt + 1);
                                return true;
                            }

                            Log.Warning(
                                "Failed to update HubRise cancelled-order status. OrderId: {OrderId}, Attempt: {Attempt}, StatusCode: {StatusCode}, Response: {Response}",
                                orderId,
                                attempt + 1,
                                (int)response.StatusCode,
                                TruncateForLog(responseText));

                            if (!IsTransientStatusCode(response.StatusCode))
                                return false;
                        }
                    }
                }
                catch (OperationCanceledException) when (_shutdownCancellation.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Log.Warning(
                        ex,
                        "Exception while updating HubRise cancelled-order status. OrderId: {OrderId}, Attempt: {Attempt}",
                        orderId,
                        attempt + 1);
                }

                attempt++;

                if (attempt < retryCount)
                {
                    await DelayBeforeRetryAsync(attempt);
                }
            }

            return false;
        }

        public async Task updatetabel(string uniqueid, string orderid, string transact)
        {
            string query = @"UPDATE EOIHQ
                             SET STATUS = 1,
                                 TRANSACT = @TRANSACT
                             WHERE ORDERID = @ORDERID
                               AND UNIQUEID = @UNIQUEID";

            await SqlHelper.CURDHubRiseAsync(
                query,
                new SqlParameter("@TRANSACT", SqlDbType.VarChar)
                {
                    Value = (object)transact ?? DBNull.Value
                },
                new SqlParameter("@ORDERID", SqlDbType.VarChar)
                {
                    Value = (object)orderid ?? DBNull.Value
                },
                new SqlParameter("@UNIQUEID", SqlDbType.VarChar)
                {
                    Value = (object)uniqueid ?? DBNull.Value
                }).ConfigureAwait(false);
        }
        public async Task Webupdatetabel(string uniqueid, string transact)
        {
            string query = @"UPDATE PJWEBORDER
                             SET STATUS = 1,
                                 TRANSACT = @TRANSACT
                             WHERE UNIQUEID = @UNIQUEID";

            await SqlHelper.CURDAsync(
                query,
                new SqlParameter("@TRANSACT", SqlDbType.VarChar)
                {
                    Value = (object)transact ?? DBNull.Value
                },
                new SqlParameter("@UNIQUEID", SqlDbType.VarChar)
                {
                    Value = (object)uniqueid ?? DBNull.Value
                }).ConfigureAwait(false);
        }

        public async Task FPupdatetabel(string uniqueid, string transact)
        {
            string query = @"UPDATE PJFPORDER
                             SET STATUS = 1,
                                 TRANSACT = @TRANSACT
                             WHERE UNIQUEID = @UNIQUEID";

            await SqlHelper.CURDAsync(
                query,
                new SqlParameter("@TRANSACT", SqlDbType.VarChar)
                {
                    Value = (object)transact ?? DBNull.Value
                },
                new SqlParameter("@UNIQUEID", SqlDbType.VarChar)
                {
                    Value = (object)uniqueid ?? DBNull.Value
                }).ConfigureAwait(false);
        }

        public async Task SaveCallCenterFinalXml(string uniqueid, string finalXml)
        {
            Log.Information("Updating FINAL_XML in PJCCORDER for UNIQUEID: {UniqueId}...", uniqueid);
            string query = @"UPDATE PJCCORDER
                             SET FINAL_XML = @FINAL_XML
                             WHERE UNIQUEID = @UNIQUEID";

            await SqlHelper.CURDAsync(
                query,
                new SqlParameter("@FINAL_XML", SqlDbType.VarChar)
                {
                    Value = (object)finalXml ?? DBNull.Value
                },
                new SqlParameter("@UNIQUEID", SqlDbType.VarChar)
                {
                    Value = (object)uniqueid ?? DBNull.Value
                }).ConfigureAwait(false);

            Log.Information("Successfully saved FINAL_XML to PJCCORDER table for UNIQUEID: {UniqueId}", uniqueid);
        }

        public async Task CallCenterupdatetabel(string uniqueid, string transact, string? finalXml = null)
        {
            Log.Information("Updating STATUS=1, TRANSACT={Transact} in PJCCORDER table for UNIQUEID: {UniqueId}...", transact, uniqueid);
            string query = @"UPDATE PJCCORDER
                             SET STATUS = 1,
                                 TRANSACT = @TRANSACT,
                                 FINAL_XML = COALESCE(@FINAL_XML, FINAL_XML)
                             WHERE UNIQUEID = @UNIQUEID";

            await SqlHelper.CURDAsync(
                query,
                new SqlParameter("@TRANSACT", SqlDbType.VarChar)
                {
                    Value = (object)transact ?? DBNull.Value
                },
                new SqlParameter("@FINAL_XML", SqlDbType.VarChar)
                {
                    Value = (object)finalXml ?? DBNull.Value
                },
                new SqlParameter("@UNIQUEID", SqlDbType.VarChar)
                {
                    Value = (object)uniqueid ?? DBNull.Value
                }).ConfigureAwait(false);

            Log.Information("Successfully updated PJCCORDER table: STATUS=1, TRANSACT={Transact}, UNIQUEID={UniqueId}", transact, uniqueid);
        }

        private async Task<string> GetFPAccessTokenAsync()
        {
            using (HttpClient client = new HttpClient())
            {
                string tokenEndpoint = "https://integration-middleware.as.restaurant-partners.com/v2/login";

                var requestBody = new Dictionary<string, string>
                {
                    { "grant_type", "client_credentials" },
                    { "password", "OT2FGaDREW" },
                    { "username", "as-plugin-pj-crescent-002" }
                };

                var content = new FormUrlEncodedContent(requestBody);
                HttpResponseMessage response = await client.PostAsync(tokenEndpoint, content).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    using var doc = System.Text.Json.JsonDocument.Parse(jsonResponse);
                    string accessToken = doc.RootElement.GetProperty("access_token").GetString() ?? string.Empty;
                    return accessToken;
                }
                else
                {
                    throw new Exception($"Error: {response.StatusCode}");
                }
            }
        }

        private async Task<bool> PostFPOrderStatusAsync(string remoteOrderId, string URL, string accesstoken, string status = "order_accepted", string reason = "Order cancelled in POS")
        {
            string url = URL;

            object payload;
            if (string.Equals(status, "order_rejected", StringComparison.OrdinalIgnoreCase))
            {
                payload = new
                {
                    status = "order_rejected",
                    remoteOrderId = remoteOrderId,
                    reason = reason
                };
            }
            else
            {
                payload = new
                {
                    status = "order_accepted",
                    remoteOrderId = remoteOrderId,
                    acceptanceTime = DateTime.UtcNow.AddMinutes(25).ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                };
            }

            string jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });

            using HttpClient client = new HttpClient();
            try
            {
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accesstoken);
                client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                StringContent content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync(url, content).ConfigureAwait(false);
                string responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    Log.Information("Foodpanda order status '{Status}' updated successfully. RemoteOrderId: {RemoteOrderId}", status, remoteOrderId);
                    return true;
                }
                else
                {
                    Log.Error("Failed to update Foodpanda order status '{Status}'. RemoteOrderId: {RemoteOrderId}, Status: {StatusCode}, Response: {Response}", status, remoteOrderId, response.StatusCode, responseContent);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error posting Foodpanda order status '{Status}'. RemoteOrderId: {RemoteOrderId}", status, remoteOrderId);
                return false;
            }
        }

        private async Task CheckForCancelledFPOrders(CancellationToken cancellationToken = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCancellation.Token, cancellationToken);
            var token = linkedCts.Token;

            try
            {
                string query = @"
                    SELECT UNIQUEID, TRANSACT, [order]
                    FROM PJFPORDER
                    WHERE STATUS = 1 AND TRANSACT IS NOT NULL AND TRANSACT <> '' AND TRANSACT <> '11' AND STOREID = '" + FOODPANDASNUM + "'";

                DataTable dt = await SqlHelper.SelectRecAsync(query).ConfigureAwait(false);

                if (dt == null || dt.Rows.Count == 0)
                    return;

                foreach (DataRow dr in dt.Rows)
                {
                    _shutdownCancellation.Token.ThrowIfCancellationRequested();

                    string transact = GetDataRowString(dr, "TRANSACT");
                    string uniqueId = GetDataRowString(dr, "UNIQUEID");
                    string fpOrderJson = GetDataRowString(dr, "ORDER");

                    if (string.IsNullOrWhiteSpace(transact) || string.IsNullOrWhiteSpace(uniqueId))
                        continue;

                    if (!long.TryParse(transact.Trim(), out long transNo))
                        continue;

                    // Check for Cancelled Status: all items in order have prodtype = 101
                    string odbcDetailQuery = @"
                        SELECT 
                            COUNT(*) AS total_count,
                            SUM(CASE WHEN prodtype = 101 THEN 1 ELSE 0 END) AS voided_count
                        FROM dba.posdetail
                        WHERE transact = ?";

                    OdbcParameter[] parametersDetail = new[] { new OdbcParameter("transact", OdbcType.BigInt) { Value = transNo } };
                    DataTable dtDetail = await Task.Run(
                        () => ODBCHelper.SelectRec(odbcDetailQuery, parametersDetail),
                        _shutdownCancellation.Token);

                    if (dtDetail != null && dtDetail.Rows.Count > 0)
                    {
                        DataRow detailRow = dtDetail.Rows[0];
                        int totalCount = detailRow["total_count"] != DBNull.Value ? Convert.ToInt32(detailRow["total_count"]) : 0;
                        int voidedCount = detailRow["voided_count"] != DBNull.Value ? Convert.ToInt32(detailRow["voided_count"]) : 0;

                        if (totalCount > 0 && totalCount == voidedCount)
                        {
                            string odbcHeaderQuery = @"
                                SELECT refid
                                FROM dba.posheader
                                WHERE transact = ? AND opendate = (SELECT MAX(opendate) FROM dba.posheader)";

                            OdbcParameter[] parametersHeader = new[] { new OdbcParameter("transact", OdbcType.BigInt) { Value = transNo } };
                            DataTable dtHeader = await Task.Run(
                                () => ODBCHelper.SelectRec(odbcHeaderQuery, parametersHeader),
                                _shutdownCancellation.Token);

                            if (dtHeader != null && dtHeader.Rows.Count > 0)
                            {
                                string refId = dtHeader.Rows[0]["refid"]?.ToString() ?? string.Empty;

                                FPRoot? fpRoot = null;
                                if (!string.IsNullOrWhiteSpace(fpOrderJson))
                                {
                                    try
                                    {
                                        var options = new System.Text.Json.JsonSerializerOptions
                                        {
                                            PropertyNameCaseInsensitive = true,
                                            NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
                                        };
                                        options.Converters.Add(new SystemTextJsonStringConverter());
                                        fpRoot = System.Text.Json.JsonSerializer.Deserialize<FPRoot>(fpOrderJson, options);
                                    }
                                    catch (Exception ex)
                                    {
                                        Log.Error(ex, "Error deserializing PJFPORDER JSON for cancellation check. UNIQUEID: {UniqueId}", uniqueId);
                                    }
                                }

                                string remoteOrderId = !string.IsNullOrWhiteSpace(refId)
                                    ? refId.Trim()
                                    : (!string.IsNullOrWhiteSpace(fpRoot?.Code) ? fpRoot.Code : uniqueId);

                                string? rejectedUrl = fpRoot?.CallbackUrls?.OrderRejectedUrl;

                                if (!string.IsNullOrWhiteSpace(remoteOrderId) && !string.IsNullOrWhiteSpace(rejectedUrl))
                                {
                                    string accessToken = await GetFPAccessTokenAsync().ConfigureAwait(false);
                                    bool sent = await PostFPOrderStatusAsync(remoteOrderId, rejectedUrl, accessToken, "order_rejected", "Order cancelled in POS").ConfigureAwait(false);
                                    if (sent)
                                    {
                                        string updateQuery = @"UPDATE PJFPORDER SET STATUS = 9 WHERE UNIQUEID = @UNIQUEID";
                                        SqlHelper.CURD(updateQuery, new SqlParameter("@UNIQUEID", uniqueId));
                                        Log.Information("Foodpanda Order Status 'order_rejected' sent for RemoteOrderId {RemoteOrderId} (Transact {Transact}). All {TotalCount} items had prodtype = 101. Updated STATUS = 9 in PJFPORDER.", remoteOrderId, transact, totalCount);
                                    }
                                }
                                else
                                {
                                    Log.Warning("Foodpanda rejection skipped: RemoteOrderId or OrderRejectedUrl missing for UNIQUEID: {UniqueId}, Transact: {Transact}", uniqueId, transact);
                                }
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (_shutdownCancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error while checking cancelled Foodpanda orders.");
            }
        }

        private async Task CheckForCompletedAndCancelledCallCenterOrders(CancellationToken cancellationToken = default)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdownCancellation.Token, cancellationToken);
            var token = linkedCts.Token;

            try
            {
                string ccsnum = App.CCSNUM;
                if (string.IsNullOrWhiteSpace(ccsnum))
                {
                    ccsnum = "21";
                }

                string query = @"
                    SELECT UNIQUEID, TRANSACT
                    FROM PJCCORDER
                    WHERE STATUS = 1 AND TRANSACT IS NOT NULL AND TRANSACT <> '' AND TRANSACT <> '11' AND STOREID = '" + ccsnum + "'";

                DataTable dt = await SqlHelper.SelectRecAsync(query).ConfigureAwait(false);

                if (dt == null || dt.Rows.Count == 0)
                    return;

                foreach (DataRow dr in dt.Rows)
                {
                    _shutdownCancellation.Token.ThrowIfCancellationRequested();

                    string transact = GetDataRowString(dr, "TRANSACT");
                    string uniqueId = GetDataRowString(dr, "UNIQUEID");

                    if (string.IsNullOrWhiteSpace(transact) || string.IsNullOrWhiteSpace(uniqueId))
                        continue;

                    if (!long.TryParse(transact.Trim(), out long transNo))
                        continue;

                    // 1. Check for Completed Status: status = 3 AND nettotal > 0
                    string odbcCompletedQuery = @"
                        SELECT refid, status, nettotal
                        FROM dba.posheader
                        WHERE transact = ? AND status = 3 AND nettotal > 0 AND opendate = (SELECT MAX(opendate) FROM dba.posheader)";

                    OdbcParameter[] parametersCompleted = new[] { new OdbcParameter("transact", OdbcType.BigInt) { Value = transNo } };
                    DataTable dtPosCompleted = await Task.Run(
                        () => ODBCHelper.SelectRec(odbcCompletedQuery, parametersCompleted),
                        _shutdownCancellation.Token);

                    if (dtPosCompleted != null && dtPosCompleted.Rows.Count > 0)
                    {
                        string refId = dtPosCompleted.Rows[0]["refid"]?.ToString() ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(refId))
                        {
                            bool sent = await CallCenterOrderProcess.PostCallCenterOrderStatusAsync(refId.Trim(), "completed", transact.Trim()).ConfigureAwait(false);
                            if (sent)
                            {
                                string updateQuery = @"UPDATE PJCCORDER SET STATUS = 3 WHERE UNIQUEID = @UNIQUEID";
                                SqlHelper.CURD(updateQuery, new SqlParameter("@UNIQUEID", uniqueId));
                                Log.Information("CallCenter Order Status 'completed' sent for CallCenter Order RefId {RefId} (Transact {Transact}, nettotal > 0). Updated STATUS = 3 in PJCCORDER.", refId, transact);
                            }
                        }

                        continue;
                    }

                    // 2. Check for Cancelled Status: all items in order have prodtype = 101
                    string odbcDetailQuery = @"
                        SELECT 
                            COUNT(*) AS total_count,
                            SUM(CASE WHEN prodtype = 101 THEN 1 ELSE 0 END) AS voided_count
                        FROM dba.posdetail
                        WHERE transact = ?";

                    OdbcParameter[] parametersDetail = new[] { new OdbcParameter("transact", OdbcType.BigInt) { Value = transNo } };
                    DataTable dtDetail = await Task.Run(
                        () => ODBCHelper.SelectRec(odbcDetailQuery, parametersDetail),
                        _shutdownCancellation.Token);

                    if (dtDetail != null && dtDetail.Rows.Count > 0)
                    {
                        DataRow detailRow = dtDetail.Rows[0];
                        int totalCount = detailRow["total_count"] != DBNull.Value ? Convert.ToInt32(detailRow["total_count"]) : 0;
                        int voidedCount = detailRow["voided_count"] != DBNull.Value ? Convert.ToInt32(detailRow["voided_count"]) : 0;

                        if (totalCount > 0 && totalCount == voidedCount)
                        {
                            string odbcHeaderQuery = @"
                                SELECT refid
                                FROM dba.posheader
                                WHERE transact = ? AND opendate = (SELECT MAX(opendate) FROM dba.posheader)";

                            OdbcParameter[] parametersHeader = new[] { new OdbcParameter("transact", OdbcType.BigInt) { Value = transNo } };
                            DataTable dtHeader = await Task.Run(
                                () => ODBCHelper.SelectRec(odbcHeaderQuery, parametersHeader),
                                _shutdownCancellation.Token);

                            if (dtHeader != null && dtHeader.Rows.Count > 0)
                            {
                                string refId = dtHeader.Rows[0]["refid"]?.ToString() ?? string.Empty;
                                if (!string.IsNullOrWhiteSpace(refId))
                                {
                                    bool sent = await CallCenterOrderProcess.PostCallCenterOrderStatusAsync(refId.Trim(), "cancelled", transact.Trim()).ConfigureAwait(false);
                                    if (sent)
                                    {
                                        string updateQuery = @"UPDATE PJCCORDER SET STATUS = 9 WHERE UNIQUEID = @UNIQUEID";
                                        SqlHelper.CURD(updateQuery, new SqlParameter("@UNIQUEID", uniqueId));
                                        Log.Information("CallCenter Order Status 'cancelled' sent for CallCenter Order RefId {RefId} (Transact {Transact}). All {TotalCount} items had prodtype = 101. Updated STATUS = 9 in PJCCORDER.", refId, transact, totalCount);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (_shutdownCancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error while checking completed/cancelled CallCenter orders.");
            }
        }

        private static string GetFPCustomerName(FPRoot root)
        {
            if (root?.Customer == null)
            {
                return "Foodpanda Customer";
            }

            string name = string.Join(
                " ",
                new[] { root.Customer.FirstName, root.Customer.LastName }
                    .Where(value => !string.IsNullOrWhiteSpace(value))
            );

            return string.IsNullOrWhiteSpace(name) ? "Foodpanda Customer" : name.Trim();
        }

        public async Task updateTabtabel(string uniqueid, string transact)
        {
            string query = @"UPDATE PJGOLOTLO
                             SET STATUS = 1,
                                 TRANSACT = @TRANSACT
                             WHERE UNIQUEID = @UNIQUEID";

            await SqlHelper.CURDHubRiseAsync(
                query,
                new SqlParameter("@TRANSACT", SqlDbType.VarChar)
                {
                    Value = (object)transact ?? DBNull.Value
                },
                new SqlParameter("@UNIQUEID", SqlDbType.VarChar)
                {
                    Value = (object)uniqueid ?? DBNull.Value
                }).ConfigureAwait(false);
        }

        public void UPDATETABLEFORTAB(string transact, string tableNum)
        {
            if (!int.TryParse(tableNum, out int parsedTableNumber))
            {
                Log.Warning(
                    "TABINFO was not updated because table number is invalid. TableNumber: {TableNumber}, Transaction: {Transaction}",
                    tableNum,
                    transact);
                return;
            }

            if (!long.TryParse(transact, out long parsedTransaction))
            {
                Log.Warning(
                    "TABINFO was not updated because transaction is invalid. TableNumber: {TableNumber}, Transaction: {Transaction}",
                    tableNum,
                    transact);
                return;
            }

            string query = @"
        UPDATE DBA.TABINFO
        SET TABLENUM = ?
        WHERE TRANSACT = ?";

            // ODBC parameters are positional. Keep this exact order.
            ODBCHelper.CURD(
                query,
                new OdbcParameter("TABLENUM", OdbcType.Int)
                {
                    Value = parsedTableNumber
                },
                new OdbcParameter("TRANSACT", OdbcType.BigInt)
                {
                    Value = parsedTransaction
                });
        }

        public async Task updatecancelordertabel(string orderid, int snum)
        {
            string query = @"UPDATE EOIHQ
                             SET STATUS = 9
                             WHERE ORDERID = @ORDERID
                               AND SNUM = @SNUM";

            await SqlHelper.CURDHubRiseAsync(
                query,
                new SqlParameter("@ORDERID", SqlDbType.VarChar)
                {
                    Value = (object)orderid ?? DBNull.Value
                },
                new SqlParameter("@SNUM", SqlDbType.Int)
                {
                    Value = snum
                }).ConfigureAwait(false);
        }

        public void AddToAllOrders(string Descript, string transact, string name, string status = "Placed")
        {
            OrderModel model = new OrderModel
            {
                Descript = Descript,
                Transact = transact,
                Status = status,
                NAME = name,
                Time = DateTime.Now.ToString("dd-MMM-yyyy HH:mm:ss")
            };

            Dispatcher dispatcher = Application.Current == null
                ? null
                : Application.Current.Dispatcher;

            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(
                    new Action(() => AddOrderModelToCollection(model)),
                    DispatcherPriority.Background);
                return;
            }

            AddOrderModelToCollection(model);
        }

        private void AddOrderModelToCollection(OrderModel model)
        {
            AllOrders.Add(model);

            while (AllOrders.Count > MaxUiOrderLogItems)
            {
                AllOrders.RemoveAt(0);
            }
        }

        private async void RegisterOrderProcessingError(
            string processingKey,
            string uniqueId,
            string orderId,
            string reason,
            string transaction,
            string customerName,
            Exception? exception = null)
        {
            int currentErrorCount = _orderErrorCounts.AddOrUpdate(
                processingKey,
                1,
                (key, current) => current + 1);

            ORDERERROR = currentErrorCount;

            string orderType = "Order";
            if (processingKey.StartsWith("FP:", StringComparison.OrdinalIgnoreCase))
            {
                orderType = "FP Order";
            }
            else if (processingKey.StartsWith("CC:", StringComparison.OrdinalIgnoreCase))
            {
                orderType = "CallCenter Order";
            }
            else if (processingKey.StartsWith("TAB:", StringComparison.OrdinalIgnoreCase))
            {
                orderType = "Tab Order";
            }
            else
            {
                orderType = "Web Order";
            }

            string descript = !string.IsNullOrWhiteSpace(reason)
                ? $"{orderType} Having Error : {reason}"
                : $"{orderType} Having Error : ";

            string transNumber = (!string.IsNullOrWhiteSpace(transaction) && transaction != "0")
                ? transaction
                : (!string.IsNullOrWhiteSpace(orderId) ? orderId : uniqueId);

            string validCustomerName = string.IsNullOrWhiteSpace(customerName) || customerName.Equals("name", StringComparison.OrdinalIgnoreCase)
                ? $"{orderType} Customer"
                : customerName;

            AddToAllOrders(
                descript,
                transNumber,
                validCustomerName,
                "Error");

            if (exception == null)
            {
                Log.Warning(
                    "Order processing failed. OrderId: {OrderId}, UNIQUEID: {UniqueId}, Attempt: {Attempt}, Reason: {Reason}",
                    orderId,
                    uniqueId,
                    currentErrorCount,
                    reason);
            }
            else
            {
                Log.Error(
                    exception,
                    "Order processing failed. OrderId: {OrderId}, UNIQUEID: {UniqueId}, Attempt: {Attempt}, Reason: {Reason}",
                    orderId,
                    uniqueId,
                    currentErrorCount,
                    reason);
            }

            if (currentErrorCount >= MaximumOrderErrorAttempts)
            {
                await updatetabel(uniqueId, orderId, "0").ConfigureAwait(false);
                _orderErrorCounts.TryRemove(processingKey, out int ignored);
                ORDERERROR = 0;

                Log.Error(
                    "Order marked as failed after maximum attempts. OrderId: {OrderId}, UNIQUEID: {UniqueId}",
                    orderId,
                    uniqueId);
            }
        }

        private void RegisterTabOrderProcessingError(
            string uniqueId,
            string reason,
            string transaction,
            string customerName,
            Exception? exception = null)
        {
            string processingKey = "TAB:" + uniqueId;
            int currentErrorCount = _orderErrorCounts.AddOrUpdate(
                processingKey,
                1,
                (key, current) => current + 1);

            ORDERERROR = currentErrorCount;

            string descript = !string.IsNullOrWhiteSpace(reason)
                ? $"Tab Order Having Error : {reason}"
                : "Tab Order Having Error : ";

            string transNumber = (!string.IsNullOrWhiteSpace(transaction) && transaction != "0")
                ? transaction
                : uniqueId;

            string validCustomerName = string.IsNullOrWhiteSpace(customerName) || customerName.Equals("name", StringComparison.OrdinalIgnoreCase)
                ? "Tab Customer"
                : customerName;

            AddToAllOrders(
                descript,
                transNumber,
                validCustomerName,
                "Error");

            if (exception == null)
            {
                Log.Warning(
                    "Tab-order processing failed. UNIQUEID: {UniqueId}, Attempt: {Attempt}, Reason: {Reason}",
                    uniqueId,
                    currentErrorCount,
                    reason);
            }
            else
            {
                Log.Error(
                    exception,
                    "Tab-order processing failed. UNIQUEID: {UniqueId}, Attempt: {Attempt}, Reason: {Reason}",
                    uniqueId,
                    currentErrorCount,
                    reason);
            }

            if (currentErrorCount >= MaximumOrderErrorAttempts)
            {
                // Original code intentionally did not update PJGOLOTLO on the fourth error:
                // updatetabel(uniqueid, orderid, "0");
                _orderErrorCounts.TryRemove(processingKey, out int ignored);
                ORDERERROR = 0;

                Log.Error(
                    "Tab order reached the maximum processing attempts. UNIQUEID: {UniqueId}",
                    uniqueId);
            }
        }

        private static bool TryParsePosResponse(
            string response,
            out int errorNum,
            out string transNumber,
            out string parseError)
        {
            errorNum = -1;
            transNumber = "0";
            parseError = string.Empty;

            if (string.IsNullOrWhiteSpace(response))
            {
                parseError = "POS response is empty.";
                return false;
            }

            try
            {
                XmlReaderSettings settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                };

                XDocument xmlDoc;

                using (StringReader stringReader = new StringReader(response))
                using (XmlReader xmlReader = XmlReader.Create(stringReader, settings))
                {
                    xmlDoc = XDocument.Load(xmlReader, LoadOptions.None);
                }

                string errorText = xmlDoc
                    .Descendants("ErrorNum")
                    .Select(element => element.Value)
                    .FirstOrDefault();

                if (!int.TryParse(errorText, out errorNum))
                {
                    errorNum = -1;
                    parseError = "POS response does not contain a valid ErrorNum.";
                    return false;
                }

                XElement transNumberElement = xmlDoc
                    .Descendants("TransNumber")
                    .FirstOrDefault();

                if (transNumberElement != null &&
                    !string.IsNullOrWhiteSpace(transNumberElement.Value))
                {
                    transNumber = transNumberElement.Value.Trim();
                }

                return true;
            }
            catch (Exception ex) when (
                ex is XmlException ||
                ex is InvalidOperationException ||
                ex is FormatException)
            {
                parseError = "Unable to parse POS XML response: " + ex.Message;
                return false;
            }
        }

        private async Task DelayBeforeRetryAsync(int attempt)
        {
            int delayMilliseconds = Math.Min(
                1000 * (int)Math.Pow(2, Math.Max(0, attempt - 1)),
                8000);

            await Task.Delay(
                delayMilliseconds,
                _shutdownCancellation.Token);
        }

        private static bool IsTransientStatusCode(HttpStatusCode statusCode)
        {
            int numericStatusCode = (int)statusCode;

            return numericStatusCode == 408 ||
                   numericStatusCode == 429 ||
                   numericStatusCode >= 500;
        }

        private static HttpClient CreateHttpClient()
        {
            HttpClientHandler handler = new HttpClientHandler
            {
                AutomaticDecompression =
                    DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            HttpClient client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(DefaultHttpTimeoutSeconds)
            };

            client.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.UserAgent.TryParseAdd(
                "WeCreatives-EOI/1.0");

            return client;
        }

        private static HttpClient CreateHubRiseHttpClient()
        {
            HttpClient client = CreateHttpClient();
            client.BaseAddress = new Uri(HubRiseBaseAddress);
            return client;
        }

        private static int ParseRequiredPositiveInteger(object value, string settingName)
        {
            string textValue = Convert.ToString(value);

            if (!int.TryParse(textValue, out int parsedValue) || parsedValue <= 0)
            {
                throw new InvalidOperationException(
                    settingName + " must contain a positive integer. Current value: " +
                    (textValue ?? "<null>"));
            }

            return parsedValue;
        }
        private static string ParseRequiredString(object value, string settingName)
        {
            string textValue = Convert.ToString(value)?.Trim();

            if (string.IsNullOrWhiteSpace(textValue))
            {
                throw new InvalidOperationException(
                    settingName + " must not be null or empty. Current value: " +
                    (textValue ?? "<null>"));
            }

            return textValue;
        }
        private static string GetDataRowString(DataRow row, string columnName)
        {
            if (row == null ||
                row.Table == null ||
                !row.Table.Columns.Contains(columnName) ||
                row[columnName] == DBNull.Value)
            {
                return string.Empty;
            }

            return Convert.ToString(row[columnName])?.Trim() ?? string.Empty;
        }

        private static string EscapeSqlLiteral(string value)
        {
            return (value ?? string.Empty).Replace("'", "''");
        }

        private static string GetEnvironmentValue(string name, string fallbackValue)
        {
            string value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrWhiteSpace(value) ? fallbackValue : value.Trim();
        }

        private static string TruncateForLog(string value)
        {
            if (string.IsNullOrEmpty(value) ||
                value.Length <= MaximumResponseLogLength)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, MaximumResponseLogLength) + "...<truncated>";
        }

        private static string NormalizePhoneNumber(string contact)
        {
            if (string.IsNullOrWhiteSpace(contact))
                return string.Empty;

            StringBuilder digits = new StringBuilder();

            foreach (char character in contact)
            {
                if (char.IsDigit(character))
                {
                    digits.Append(character);
                }
            }

            return digits.ToString();
        }

        private static string MaskPhoneNumber(string phoneNumber)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber) || phoneNumber.Length <= 4)
                return "****";

            return new string('*', phoneNumber.Length - 4) +
                   phoneNumber.Substring(phoneNumber.Length - 4);
        }

        private static string ReadWebExceptionResponse(WebException exception)
        {
            if (exception == null || exception.Response == null)
                return string.Empty;

            try
            {
                using (WebResponse response = exception.Response)
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = stream == null
                           ? null
                           : new StreamReader(stream))
                {
                    return reader == null ? string.Empty : reader.ReadToEnd();
                }
            }
            catch (Exception readException)
            {
                Log.Warning(
                    readException,
                    "Unable to read the SMS error response body.");
                return string.Empty;
            }
        }

        private static string GetCustomerName(Root order)
        {
            if (order == null || order.customer == null)
                return string.Empty;

            return string.Join(
                    " ",
                    new[]
                    {
                        order.customer.first_name,
                        order.customer.last_name
                    }
                    .Where(value => !string.IsNullOrWhiteSpace(value)))
                .Trim();
        }
        private static string GetWebCustomerName(WebRoot order)
        {
            if (order == null || order.Customer == null)
                return "Web Customer";

            string name = string.Join(
                    " ",
                    new[]
                    {
                        order.Customer.FirstName,
                        order.Customer.LastName
                    }
                    .Where(value => !string.IsNullOrWhiteSpace(value)))
                .Trim();

            return string.IsNullOrWhiteSpace(name) ? "Web Customer" : name;
        }

        private static string GetCallCenterCustomerName(CallCenterOrderRoot? order, string? finalXml = null)
        {
            if (order?.customer_info != null)
            {
                string name = string.Join(
                    " ",
                    new[]
                    {
                        order.customer_info.first_name,
                        order.customer_info.last_name
                    }
                    .Where(value => !string.IsNullOrWhiteSpace(value)))
                    .Trim();

                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }

            if (!string.IsNullOrWhiteSpace(finalXml))
            {
                try
                {
                    var doc = XDocument.Parse(finalXml);
                    string first = doc.Descendants("FirstName").FirstOrDefault()?.Value?.Trim() ?? string.Empty;
                    string last = doc.Descendants("LastName").FirstOrDefault()?.Value?.Trim() ?? string.Empty;
                    string name = string.Join(" ", new[] { first, last }.Where(v => !string.IsNullOrWhiteSpace(v))).Trim();
                    if (!string.IsNullOrWhiteSpace(name))
                        return name;
                }
                catch
                {
                    // Ignore XML parsing errors here
                }
            }

            return "CallCenter Customer";
        }

        private static string GetTabCustomerName(TabOrderRoot order)
        {
            if (order == null || order.customerInfo == null)
                return "Tab Customer";

            return !string.IsNullOrWhiteSpace(order.customerInfo.name) ? order.customerInfo.name.Trim() : "Tab Customer";
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                StopTimers();
                if (_wEBorderCheckTimer != null) _wEBorderCheckTimer.Tick -= WebOrderCheckTimer_Tick;
                if (_TABorderCheckTimer != null) _TABorderCheckTimer.Tick -= TABOrderCheckTimer_Tick;
                if (_CANCELorderCheckTimer != null) _CANCELorderCheckTimer.Tick -= CancelOrderCheckTimer_Tick;
                if (_tableUpdateTimer != null) _tableUpdateTimer.Tick -= TableUpdateTimer_Tick;
                if (_empUpdateTimer != null) _empUpdateTimer.Tick -= EmpUpdateTimer_Tick;
                if (_fpOrderCheckTimer != null) _fpOrderCheckTimer.Tick -= FPOrderCheckTimer_Tick;

                _shutdownCancellation.Cancel();
                _shutdownCancellation.Dispose();

                _tableUpdateGate.Dispose();
                _employeeUpdateGate.Dispose();
                _orderCheckGate.Dispose();
                _webOrderCheckGate.Dispose();
                _fpOrderCheckGate.Dispose();
                _tabOrderCheckGate.Dispose();
                _cancelOrderCheckGate.Dispose();
                _driverSyncGate.Dispose();
                _watchdogTimer?.Dispose();
                _watchdogTimer = null;
            }

            _disposed = true;
        }

        private void WatchdogCallback(object? state)
        {
            if (Interlocked.CompareExchange(ref _isRestarting, 0, 0) != 0 || _disposed)
                return;

            long elapsedMs = Environment.TickCount64 - Interlocked.Read(ref _lastHeartbeatTicks);
            // 4 minutes timeout = 240,000 ms
            if (elapsedMs > 240000)
            {
                if (Interlocked.CompareExchange(ref _isRestarting, 1, 0) == 0)
                {
                    Log.Error("WATCHDOG DETECTED FREEZE: Application background timers haven't executed for {ElapsedSec} seconds! Initiating auto-restart recovery...", elapsedMs / 1000);
                    try
                    {
                        string? exePath = Environment.ProcessPath;
                        if (!string.IsNullOrEmpty(exePath) && System.IO.File.Exists(exePath))
                        {
                            System.Diagnostics.Process.Start(exePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "WATCHDOG: Failed to launch new application process during auto-restart.");
                    }
                    finally
                    {
                        Environment.Exit(0);
                    }
                }
            }
        }
    }
}

