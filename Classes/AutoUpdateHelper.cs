using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using AutoUpdaterDotNET;
using Serilog;

namespace WeCreatives_EOI.Classes
{
    /// <summary>
    /// Handles safe auto-updating for WeCreatives EOI across 50 stores.
    /// Preserves store-specific configurations (Branch ID, DB connections, Terminals).
    /// </summary>
    public static class AutoUpdateHelper
    {
        public static string GetServerUpdateUrl()
        {
            try
            {
                string configuredUrl = ConfigurationManager.AppSettings["UpdateServerUrl"];
                if (!string.IsNullOrWhiteSpace(configuredUrl))
                {
                    return configuredUrl;
                }

                // If not explicitly set, use HOIP from App.config
                string hoip = ConfigurationManager.AppSettings["HOIP"];
                if (!string.IsNullOrWhiteSpace(hoip))
                {
                    return $"http://{hoip}/eoi-updates/version.xml";
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoUpdate] Could not read server URL from config: {ex.Message}");
            }

            return "http://84.247.146.170/eoi-updates/version.xml";
        }

        /// <summary>
        /// Initialize updater on application startup
        /// </summary>
        public static void Initialize()
        {
            try
            {
                // If running in Visual Studio Debugger, skip updater to avoid interfering with development
                if (Debugger.IsAttached)
                {
                    Log.Information("[AutoUpdate] Debugger attached. Skipping auto-updater.");
                    return;
                }

                // 1. Safeguard local store config file (*.config)
                ProtectStoreConfigFile();

                // 2. Normalize installed version to 3 parts (Major.Minor.Build) to match version.xml
                var assembly = Assembly.GetEntryAssembly() ?? typeof(AutoUpdateHelper).Assembly;
                var rawVer = assembly.GetName().Version ?? new Version(1, 0, 0, 0);
                AutoUpdater.InstalledVersion = new Version(rawVer.Major, rawVer.Minor, Math.Max(0, rawVer.Build));

                Log.Information($"[AutoUpdate] Installed Version: {AutoUpdater.InstalledVersion}");

                string updateUrl = GetServerUpdateUrl();
                Log.Information($"[AutoUpdate] Checking server update URL: {updateUrl}");

                // 3. Configure AutoUpdater settings
                AutoUpdater.RunUpdateAsAdmin = false;
                AutoUpdater.ApplicationExitEvent += OnApplicationExit;
                AutoUpdater.CheckForUpdateEvent += OnCheckForUpdate;

                // 4. Start checking asynchronously in background
                AutoUpdater.Start(updateUrl);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[AutoUpdate] Critical failure initializing updater");
            }
        }

        /// <summary>
        /// Returns the path to the store's active config file.
        /// </summary>
        public static string GetStoreConfigPath()
        {
            try
            {
                string path = AppDomain.CurrentDomain.GetData("APP_CONFIG_FILE") as string;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    return path;
                }

                string dllConfig = Path.Combine(AppContext.BaseDirectory, "WeCreatives EOI.dll.config");
                if (File.Exists(dllConfig))
                {
                    return dllConfig;
                }

                string exeConfig = Path.Combine(AppContext.BaseDirectory, "WeCreatives EOI.exe.config");
                if (File.Exists(exeConfig))
                {
                    return exeConfig;
                }

                return dllConfig;
            }
            catch
            {
                return Path.Combine(AppContext.BaseDirectory, "WeCreatives EOI.dll.config");
            }
        }

        /// <summary>
        /// Ensures store-specific config file has a permanent backup.
        /// If ever missing or corrupted, restores from backup immediately.
        /// </summary>
        public static void ProtectStoreConfigFile()
        {
            try
            {
                string configPath = GetStoreConfigPath();
                if (string.IsNullOrEmpty(configPath)) return;

                string backupPath = configPath + ".store_permanent_backup";

                if (File.Exists(configPath))
                {
                    var configInfo = new FileInfo(configPath);
                    if (configInfo.Length > 0)
                    {
                        File.Copy(configPath, backupPath, overwrite: true);
                    }
                }
                else if (File.Exists(backupPath))
                {
                    File.Copy(backupPath, configPath, overwrite: true);
                    Log.Information("[AutoUpdate] Restored store config from permanent backup!");
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"[AutoUpdate] ProtectStoreConfigFile warning: {ex.Message}");
            }
        }

        private static void OnCheckForUpdate(UpdateInfoEventArgs args)
        {
            if (args.Error != null)
            {
                Log.Warning($"[AutoUpdate] Server update check skipped/failed: {args.Error.Message}");
                return;
            }

            Version.TryParse(args.CurrentVersion, out Version? serverVer);
            bool hasNewerVersion = args.IsUpdateAvailable && serverVer != null && serverVer > args.InstalledVersion;

            // Only proceed if an update is genuinely available and server version is strictly greater
            if (hasNewerVersion)
            {
                Log.Information($"[AutoUpdate] New version detected: {args.CurrentVersion} (Current: {args.InstalledVersion}).");
                ProtectStoreConfigFile();

                bool isMandatory = false;
                string mandatoryConfig = ConfigurationManager.AppSettings["AutoUpdateMandatory"];
                if (!string.IsNullOrWhiteSpace(mandatoryConfig))
                {
                    bool.TryParse(mandatoryConfig, out isMandatory);
                }

                MessageBoxResult result = MessageBoxResult.Yes;
                if (!isMandatory)
                {
                    // Prompt user so app is never blocked if they want to work right now
                    result = MessageBox.Show(
                        $"A new update (version {args.CurrentVersion}) is available.\n\nWould you like to install it now?\n(Click 'No' to continue using the application)",
                        "WeCreatives EOI - Update Available",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);
                }

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        if (AutoUpdater.DownloadUpdate(args))
                        {
                            App.ReleaseAppMutex();
                            Environment.Exit(0);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "[AutoUpdate] Error during update download/extract");
                    }
                }
                else
                {
                    Log.Information("[AutoUpdate] User opted to skip update for this session.");
                }
            }
            else
            {
                Log.Information($"[AutoUpdate] Application is up to date (Installed: {args.InstalledVersion}, Server: {args.CurrentVersion}).");
            }
        }

        private static void OnApplicationExit()
        {
            try
            {
                App.ReleaseAppMutex();
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[AutoUpdate] OnApplicationExit error");
            }
        }
    }
}
