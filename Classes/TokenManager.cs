using Serilog;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;
using WeCreatives_EOI.Classes;

namespace WeCreatives_EOI.Services
{
    public class TokenManager
    {
        private readonly object _lock = new object();
        private string? _token;

        public TokenManager()
        {
        }

        public async Task SetAccessTokenAsync()
        {
            try
            {
                string? token = await OAuthService.GetAccessTokenAsync().ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(token))
                {
                    lock (_lock)
                    {
                        _token = token;
                    }
                    Log.Information("Access token updated successfully via OAuthService.");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to set access token in TokenManager.");
            }
        }

        public void SetAccessToken()
        {
            SetAccessTokenAsync().GetAwaiter().GetResult();
        }

        public async Task<string> GetAccessToken(int snum)
        {
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(_token))
                {
                    return _token;
                }
            }

            try
            {
                string query = "SELECT desktoptoken FROM accestokens WHERE snum = @snum";
                SqlParameter[] parameters = new[] { new SqlParameter("@snum", SqlDbType.Int) { Value = snum } };

                DataTable dt = await SqlHelper.SelectRecAsync(query, parameters).ConfigureAwait(false);
                if (dt != null && dt.Rows.Count > 0)
                {
                    string fetchedToken = dt.Rows[0]["desktoptoken"]?.ToString() ?? string.Empty;
                    lock (_lock)
                    {
                        _token = fetchedToken;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error retrieving access token for store snum {StoreNum}", snum);
            }

            lock (_lock)
            {
                return _token ?? string.Empty;
            }
        }
    }
}

