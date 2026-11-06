using Serilog;
using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;

namespace WeCreatives_EOI.Classes
{
    public static class SqlHelper
    {
        // Main Database connection settings (for PJWEBORDER, PJGOLOTLO, tokens, etc.)
        public static string HOIP => ConfigurationManager.AppSettings["HOIP"] ?? "84.247.146.170";
        public static string MainDbUser => ConfigurationManager.AppSettings["MAIN_DB_USER"] ?? "sa";
        public static string MainDbPassword => ConfigurationManager.AppSettings["MAIN_DB_PASSWORD"] ?? "EOI@471pj";
        public static string MainDbName => ConfigurationManager.AppSettings["MAIN_DB_NAME"] ?? "HUBRISE";

        // HubRise Database connection settings (specifically for OrderCheckTimer / EOIHQ table flow)
        public static string HubRiseDbServer => ConfigurationManager.AppSettings["HUBRISE_DB_SERVER"] ?? "eoi.wecreatives.agency,1432";
        public static string HubRiseDbUser => ConfigurationManager.AppSettings["HUBRISE_DB_USER"] ?? "sa";
        public static string HubRiseDbPassword => ConfigurationManager.AppSettings["HUBRISE_DB_PASSWORD"] ?? "cc@c2k";
        public static string HubRiseDbName => ConfigurationManager.AppSettings["HUBRISE_DB_NAME"] ?? "HUBRISE";

        private static string GetMainConnectionString()
        {
            return $"Data Source={HOIP};Initial Catalog={MainDbName};User ID={MainDbUser};Password={MainDbPassword};Connect Timeout=15;Max Pool Size=50;Pooling=True;TrustServerCertificate=True;";
        }

        private static string GetHubRiseConnectionString()
        {
            return $"Data Source={HubRiseDbServer};Initial Catalog={HubRiseDbName};User ID={HubRiseDbUser};Password={HubRiseDbPassword};Connect Timeout=15;Max Pool Size=50;Pooling=True;TrustServerCertificate=True;";
        }

        #region Main DB Methods

        public static SqlConnection GetCon()
        {
            string connectionString = GetMainConnectionString();
            int retryCount = 0;
            int maxRetries = 2;
            int delayMs = 500;

            while (true)
            {
                try
                {
                    SqlConnection con = new SqlConnection(connectionString);
                    con.Open();
                    return con;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    if (retryCount >= maxRetries)
                    {
                        Log.Error(ex, "Failed to connect to Main SQL Server database after {MaxRetries} attempts.", maxRetries);
                        throw;
                    }
                    Log.Warning("Main SQL connection attempt {RetryCount} failed. Retrying in {DelayMs}ms... Error: {Message}", retryCount, delayMs, ex.Message);
                    System.Threading.Thread.Sleep(delayMs);
                }
            }
        }

        public static async Task<SqlConnection> GetConAsync()
        {
            string connectionString = GetMainConnectionString();
            int retryCount = 0;
            int maxRetries = 2;
            int delayMs = 500;

            while (true)
            {
                try
                {
                    SqlConnection con = new SqlConnection(connectionString);
                    await con.OpenAsync().ConfigureAwait(false);
                    return con;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    if (retryCount >= maxRetries)
                    {
                        Log.Error(ex, "Failed to connect to Main SQL Server database asynchronously after {MaxRetries} attempts.", maxRetries);
                        throw;
                    }
                    Log.Warning("Async Main SQL connection attempt {RetryCount} failed. Retrying in {DelayMs}ms... Error: {Message}", retryCount, delayMs, ex.Message);
                    await Task.Delay(delayMs).ConfigureAwait(false);
                }
            }
        }

        public static DataTable SelectRec(string query, params SqlParameter[] parameters)
        {
            DataTable dataTable = new DataTable();
            try
            {
                using (SqlConnection con = GetCon())
                using (SqlCommand cmd = new SqlCommand(query, con))
                {
                    cmd.CommandTimeout = 15;
                    if (parameters != null && parameters.Length > 0)
                    {
                        foreach (var p in parameters)
                        {
                            cmd.Parameters.Add((SqlParameter)((ICloneable)p).Clone());
                        }
                    }
                    using (SqlDataAdapter dataAdapter = new SqlDataAdapter(cmd))
                    {
                        dataAdapter.Fill(dataTable);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error executing Main DB SelectRec SQL query: {Query}", query);
            }
            return dataTable;
        }

        public static async Task<DataTable> SelectRecAsync(string query, params SqlParameter[] parameters)
        {
            DataTable dataTable = new DataTable();
            try
            {
                using (SqlConnection con = await GetConAsync().ConfigureAwait(false))
                using (SqlCommand cmd = new SqlCommand(query, con))
                {
                    cmd.CommandTimeout = 15;
                    if (parameters != null && parameters.Length > 0)
                    {
                        foreach (var p in parameters)
                        {
                            cmd.Parameters.Add((SqlParameter)((ICloneable)p).Clone());
                        }
                    }
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        dataTable.Load(reader);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error executing Main DB SelectRecAsync SQL query: {Query}", query);
            }
            return dataTable;
        }

        public static void CURD(string sqlCommand, params SqlParameter[] parameters)
        {
            try
            {
                using (SqlConnection con = GetCon())
                using (SqlTransaction transaction = con.BeginTransaction())
                using (SqlCommand cmd = new SqlCommand(sqlCommand, con, transaction))
                {
                    cmd.CommandType = CommandType.Text;
                    cmd.CommandTimeout = 15;
                    if (parameters != null && parameters.Length > 0)
                    {
                        foreach (var p in parameters)
                        {
                            cmd.Parameters.Add((SqlParameter)((ICloneable)p).Clone());
                        }
                    }
                    try
                    {
                        cmd.ExecuteNonQuery();
                        transaction.Commit();
                        Log.Information("Data saved successfully in Main DB.");
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        Log.Error(ex, "Error executing Main DB CURD command: {SqlCommand}", sqlCommand);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to complete Main DB CURD transaction.");
            }
        }

        public static async Task CURDAsync(string sqlCommand, params SqlParameter[] parameters)
        {
            try
            {
                using (SqlConnection con = await GetConAsync().ConfigureAwait(false))
                using (SqlCommand cmd = new SqlCommand(sqlCommand, con))
                {
                    cmd.CommandType = CommandType.Text;
                    cmd.CommandTimeout = 15;
                    if (parameters != null && parameters.Length > 0)
                    {
                        foreach (var p in parameters)
                        {
                            cmd.Parameters.Add((SqlParameter)((ICloneable)p).Clone());
                        }
                    }
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                    Log.Information("Data saved successfully in Main DB.");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error executing Main DB CURDAsync command: {SqlCommand}", sqlCommand);
            }
        }

        #endregion

        #region HubRise Specific DB Methods (for OrderCheckTimer / EOIHQ)

        public static SqlConnection GetHubRiseCon()
        {
            string connectionString = GetHubRiseConnectionString();
            int retryCount = 0;
            int maxRetries = 2;
            int delayMs = 500;

            while (true)
            {
                try
                {
                    SqlConnection con = new SqlConnection(connectionString);
                    con.Open();
                    return con;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    if (retryCount >= maxRetries)
                    {
                        Log.Error(ex, "Failed to connect to HubRise SQL Server database after {MaxRetries} attempts.", maxRetries);
                        throw;
                    }
                    Log.Warning("HubRise SQL connection attempt {RetryCount} failed. Retrying in {DelayMs}ms... Error: {Message}", retryCount, delayMs, ex.Message);
                    System.Threading.Thread.Sleep(delayMs);
                }
            }
        }

        public static async Task<SqlConnection> GetHubRiseConAsync()
        {
            string connectionString = GetHubRiseConnectionString();
            int retryCount = 0;
            int maxRetries = 2;
            int delayMs = 500;

            while (true)
            {
                try
                {
                    SqlConnection con = new SqlConnection(connectionString);
                    await con.OpenAsync().ConfigureAwait(false);
                    return con;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    if (retryCount >= maxRetries)
                    {
                        Log.Error(ex, "Failed to connect to HubRise SQL Server database asynchronously after {MaxRetries} attempts.", maxRetries);
                        throw;
                    }
                    Log.Warning("Async HubRise SQL connection attempt {RetryCount} failed. Retrying in {DelayMs}ms... Error: {Message}", retryCount, delayMs, ex.Message);
                    await Task.Delay(delayMs).ConfigureAwait(false);
                }
            }
        }

        public static DataTable SelectRecHubRise(string query, params SqlParameter[] parameters)
        {
            DataTable dataTable = new DataTable();
            try
            {
                using (SqlConnection con = GetHubRiseCon())
                using (SqlCommand cmd = new SqlCommand(query, con))
                {
                    cmd.CommandTimeout = 15;
                    if (parameters != null && parameters.Length > 0)
                    {
                        foreach (var p in parameters)
                        {
                            cmd.Parameters.Add((SqlParameter)((ICloneable)p).Clone());
                        }
                    }
                    using (SqlDataAdapter dataAdapter = new SqlDataAdapter(cmd))
                    {
                        dataAdapter.Fill(dataTable);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error executing HubRise DB SelectRecHubRise SQL query: {Query}", query);
            }
            return dataTable;
        }

        public static async Task<DataTable> SelectRecHubRiseAsync(string query, params SqlParameter[] parameters)
        {
            DataTable dataTable = new DataTable();
            try
            {
                using (SqlConnection con = await GetHubRiseConAsync().ConfigureAwait(false))
                using (SqlCommand cmd = new SqlCommand(query, con))
                {
                    cmd.CommandTimeout = 5;
                    if (parameters != null && parameters.Length > 0)
                    {
                        foreach (var p in parameters)
                        {
                            cmd.Parameters.Add((SqlParameter)((ICloneable)p).Clone());
                        }
                    }
                    using (SqlDataReader reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                    {
                        dataTable.Load(reader);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error executing HubRise DB SelectRecHubRiseAsync SQL query: {Query}", query);
            }
            return dataTable;
        }

        public static void CURDHubRise(string sqlCommand, params SqlParameter[] parameters)
        {
            try
            {
                using (SqlConnection con = GetHubRiseCon())
                using (SqlTransaction transaction = con.BeginTransaction())
                using (SqlCommand cmd = new SqlCommand(sqlCommand, con, transaction))
                {
                    cmd.CommandType = CommandType.Text;
                    cmd.CommandTimeout = 5;
                    if (parameters != null && parameters.Length > 0)
                    {
                        foreach (var p in parameters)
                        {
                            cmd.Parameters.Add((SqlParameter)((ICloneable)p).Clone());
                        }
                    }
                    try
                    {
                        cmd.ExecuteNonQuery();
                        transaction.Commit();
                        Log.Information("Data saved successfully in HubRise DB.");
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        Log.Error(ex, "Error executing HubRise DB CURDHubRise command: {SqlCommand}", sqlCommand);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to complete HubRise DB CURDHubRise transaction.");
            }
        }

        public static async Task CURDHubRiseAsync(string sqlCommand, params SqlParameter[] parameters)
        {
            try
            {
                using (SqlConnection con = await GetHubRiseConAsync().ConfigureAwait(false))
                using (SqlCommand cmd = new SqlCommand(sqlCommand, con))
                {
                    cmd.CommandType = CommandType.Text;
                    cmd.CommandTimeout = 5;
                    if (parameters != null && parameters.Length > 0)
                    {
                        foreach (var p in parameters)
                        {
                            cmd.Parameters.Add((SqlParameter)((ICloneable)p).Clone());
                        }
                    }
                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                    Log.Information("Data saved successfully in HubRise DB.");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error executing HubRise DB CURDHubRiseAsync command: {SqlCommand}", sqlCommand);
            }
        }

        #endregion

        #region Helper Utilities

        public static bool ExistRec(SqlConnection con, string query)
        {
            try
            {
                using (SqlCommand cmd = new SqlCommand(query, con))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    return reader.HasRows;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error checking ExistRec: {Query}", query);
                return false;
            }
        }

        public static DateTime GetMaxOpenDate()
        {
            DateTime now = DateTime.Now;
            return now.Hour >= 6 ? now : now.AddDays(-1.0);
        }

        public static string GetValue(SqlConnection con, string query)
        {
            try
            {
                using (SqlDataAdapter dataAdapter = new SqlDataAdapter(query, con))
                {
                    DataTable dataTable = new DataTable();
                    dataAdapter.Fill(dataTable);
                    if (dataTable.Rows.Count > 0 && dataTable.Columns.Count > 0)
                    {
                        return Convert.ToString(dataTable.Rows[0][0]) ?? string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error executing GetValue SQL query: {Query}", query);
            }
            return string.Empty;
        }

        public static string? Get_transact_Status(SqlConnection con, string query)
        {
            try
            {
                using (SqlCommand cmd = new SqlCommand(query, con))
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    if (reader.HasRows && reader.Read())
                    {
                        return reader.IsDBNull(0) ? null : reader.GetString(0);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error executing Get_transact_Status SQL query: {Query}", query);
            }
            return null;
        }

        #endregion
    }
}
