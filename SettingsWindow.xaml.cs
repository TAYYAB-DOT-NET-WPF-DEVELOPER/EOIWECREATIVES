using Serilog;
using System;
using System.Configuration;
using System.Windows;

namespace WeCreatives_EOI
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            LoadConfiguration();
        }

        private void LoadConfiguration()
        {
            try
            {
                txtSNUM.Text = ConfigurationManager.AppSettings["SNUM"] ?? string.Empty;
                txtHUBRISESNUM.Text = ConfigurationManager.AppSettings["HUBRISESNUM"] ?? ConfigurationManager.AppSettings["SNUM"] ?? string.Empty;
                txtTABSNUM.Text = ConfigurationManager.AppSettings["TABSNUM"] ?? string.Empty;
                txtCCSNUM.Text = ConfigurationManager.AppSettings["CCSNUM"] ?? "10001";
                txtFOODPANDASNUM.Text = ConfigurationManager.AppSettings["FOODPANDASNUM"] ?? "101";
                
                txtMainDbServer.Text = ConfigurationManager.AppSettings["HOIP"] ?? "84.247.146.170";
                txtMainDbUser.Text = ConfigurationManager.AppSettings["MAIN_DB_USER"] ?? "sa";
                txtMainDbPassword.Text = ConfigurationManager.AppSettings["MAIN_DB_PASSWORD"] ?? "EOI@471pj";

                txtHubRiseDbServer.Text = ConfigurationManager.AppSettings["HUBRISE_DB_SERVER"] ?? "eoi.wecreatives.agency,1432";
                txtHubRiseDbUser.Text = ConfigurationManager.AppSettings["HUBRISE_DB_USER"] ?? "sa";
                txtHubRiseDbPassword.Text = ConfigurationManager.AppSettings["HUBRISE_DB_PASSWORD"] ?? "cc@c2k";

                txtVendorDriverUrl.Text = ConfigurationManager.AppSettings["VendorDriverUrl"] ?? "https://rider-app.papajohns.com.pk/api/vendor/driver";
                txtVendorStatusUrl.Text = ConfigurationManager.AppSettings["VendorStatusUrl"] ?? "https://www.papajohns.com.pk/api/pos/webhook/order-status";
                txtVendorAuthUrl.Text = ConfigurationManager.AppSettings["VendorAuthUrl"] ?? "https://www.papajohns.com.pk/api/vendor/auth";
                txtVendorApiKey.Text = ConfigurationManager.AppSettings["VendorApiKey"] ?? "93uisySuw3E0ugdAcaLnYo6amg9ud9Vs";
                txtRiderApiKey.Text = ConfigurationManager.AppSettings["RiderApiKey"] ?? "eyiC6S2xaRtwfudqsXpSvjqX";
                txtVendorEmail.Text = ConfigurationManager.AppSettings["VendorEmail"] ?? "umair.zahid@papajohns-group.co.uk";
                txtVendorPassword.Text = ConfigurationManager.AppSettings["VendorPassword"] ?? "Zv3v@akovqcqmkc9";
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error loading configuration in SettingsWindow.");
                MessageBox.Show($"Failed to load configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

                UpdateSetting(config, "SNUM", txtSNUM.Text.Trim());
                UpdateSetting(config, "HUBRISESNUM", txtHUBRISESNUM.Text.Trim());
                UpdateSetting(config, "TABSNUM", txtTABSNUM.Text.Trim());
                UpdateSetting(config, "CCSNUM", txtCCSNUM.Text.Trim());
                
                UpdateSetting(config, "HOIP", txtMainDbServer.Text.Trim());
                UpdateSetting(config, "MAIN_DB_USER", txtMainDbUser.Text.Trim());
                UpdateSetting(config, "MAIN_DB_PASSWORD", txtMainDbPassword.Text.Trim());

                UpdateSetting(config, "HUBRISE_DB_SERVER", txtHubRiseDbServer.Text.Trim());
                UpdateSetting(config, "HUBRISE_DB_USER", txtHubRiseDbUser.Text.Trim());
                UpdateSetting(config, "HUBRISE_DB_PASSWORD", txtHubRiseDbPassword.Text.Trim());

                UpdateSetting(config, "VendorDriverUrl", txtVendorDriverUrl.Text.Trim());
                UpdateSetting(config, "VendorStatusUrl", txtVendorStatusUrl.Text.Trim());
                UpdateSetting(config, "VendorAuthUrl", txtVendorAuthUrl.Text.Trim());
                UpdateSetting(config, "VendorApiKey", txtVendorApiKey.Text.Trim());
                UpdateSetting(config, "RiderApiKey", txtRiderApiKey.Text.Trim());
                UpdateSetting(config, "VendorEmail", txtVendorEmail.Text.Trim());
                UpdateSetting(config, "VendorPassword", txtVendorPassword.Text.Trim());

                config.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection("appSettings");

                Log.Information("Configuration settings successfully saved to App.config from UI.");
                MessageBox.Show("Configuration settings updated successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error saving configuration in SettingsWindow.");
                MessageBox.Show($"Failed to save configuration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void UpdateSetting(Configuration config, string key, string value)
        {
            if (config.AppSettings.Settings[key] != null)
            {
                config.AppSettings.Settings[key].Value = value;
            }
            else
            {
                config.AppSettings.Settings.Add(key, value);
            }
            
            // Also update current in-memory collection
            ConfigurationManager.AppSettings[key] = value;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
