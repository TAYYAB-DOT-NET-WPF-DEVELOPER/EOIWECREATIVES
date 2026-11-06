using Serilog;
using System;
using System.Configuration;
using System.Data;
using System.Data.Odbc;

namespace WeCreatives_EOI
{
    public static class ODBCHelper
    {
        public static OdbcConnection GetCon()
        {
            var connStrSetting = ConfigurationManager.ConnectionStrings["ConStr"];
            if (connStrSetting == null || string.IsNullOrWhiteSpace(connStrSetting.ConnectionString))
            {
                throw new InvalidOperationException("ODBC ConnectionString 'ConStr' is missing or empty in App.config.");
            }
            return new OdbcConnection(connStrSetting.ConnectionString);
        }

        public static DataTable SelectRec(string query, params OdbcParameter[] parameters)
        {
            DataTable dt = new DataTable();
            try
            {
                using (OdbcConnection con = GetCon())
                {
                    con.Open();
                    using (OdbcCommand cmd = new OdbcCommand(query, con))
                    {
                        cmd.CommandTimeout = 10;
                        if (parameters != null && parameters.Length > 0)
                        {
                            foreach (var p in parameters)
                            {
                                cmd.Parameters.Add((OdbcParameter)((ICloneable)p).Clone());
                            }
                        }
                        using (OdbcDataAdapter da = new OdbcDataAdapter(cmd))
                        {
                            da.Fill(dt);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ODBCHelper SelectRec failed. Query: {Query}", query);
            }
            return dt;
        }

        public static bool ExistRec(string query, params OdbcParameter[] parameters)
        {
            try
            {
                using (OdbcConnection con = GetCon())
                {
                    con.Open();
                    using (OdbcCommand cmd = new OdbcCommand(query, con))
                    {
                        cmd.CommandTimeout = 10;
                        if (parameters != null && parameters.Length > 0)
                        {
                            foreach (var p in parameters)
                            {
                                cmd.Parameters.Add((OdbcParameter)((ICloneable)p).Clone());
                            }
                        }
                        using (OdbcDataReader reader = cmd.ExecuteReader())
                        {
                            return reader.HasRows;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ODBCHelper ExistRec failed. Query: {Query}", query);
                return false;
            }
        }

        public static DataTable SelectRecId(string query, params OdbcParameter[] parameters)
        {
            return SelectRec(query, parameters);
        }

        public static void CURD(string spname, params OdbcParameter[] parameters)
        {
            try
            {
                using (OdbcConnection con = GetCon())
                {
                    con.Open();
                    using (OdbcTransaction txn = con.BeginTransaction())
                    using (OdbcCommand cmd = new OdbcCommand(spname, con, txn))
                    {
                        cmd.CommandTimeout = 10;
                        if (parameters != null && parameters.Length > 0)
                        {
                            foreach (var p in parameters)
                            {
                                cmd.Parameters.Add((OdbcParameter)((ICloneable)p).Clone());
                            }
                        }
                        try
                        {
                            cmd.ExecuteNonQuery();
                            txn.Commit();
                        }
                        catch (Exception ex)
                        {
                            txn.Rollback();
                            Log.Error(ex, "ODBCHelper CURD execution failed. Command: {Command}", spname);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ODBCHelper CURD failed to establish connection for command: {Command}", spname);
            }
        }
    }
}

