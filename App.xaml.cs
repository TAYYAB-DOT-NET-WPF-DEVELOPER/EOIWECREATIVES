using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.Configuration;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WeCreatives_EOI.Classes;
using WeCreatives_EOI.Services;

namespace WeCreatives_EOI
{
    public partial class App : Application
    {
        private IServiceProvider? _serviceProvider;
        private static Mutex? _mutex;
        private static bool _hasMutex;
        private const string MutexName = "TABORDERINGEOI";

        public static string SNUM => ConfigurationManager.AppSettings["SNUM"] ?? string.Empty;
        public static string HUBRISESNUM => ConfigurationManager.AppSettings["HUBRISESNUM"] ?? ConfigurationManager.AppSettings["SNUM"] ?? string.Empty;
        public static string TABSNUM => ConfigurationManager.AppSettings["TABSNUM"] ?? string.Empty;
        public static string FPSNUM => ConfigurationManager.AppSettings["FOODPANDASNUM"] ?? string.Empty;
        public static string CCSNUM => ConfigurationManager.AppSettings["CCSNUM"] ?? ConfigurationManager.AppSettings["SNUM"] ?? string.Empty;
        public static string FPPRODNUM => ConfigurationManager.AppSettings["FPPRODNUM"] ?? string.Empty;
        public static string HOIP => ConfigurationManager.AppSettings["HOIP"] ?? string.Empty;

        protected override void OnStartup(StartupEventArgs e)
        {
            SetupExceptionHandling();

            LoggingConfiguration.ConfigureLogging();
            Log.Information("Application starting up...");

            try
            {
                _mutex = new Mutex(true, MutexName, out bool isNewInstance);
                _hasMutex = isNewInstance;

                if (!isNewInstance)
                {
                    Log.Warning("Another instance of the application is already running.");
                    MessageBox.Show("The application is already running.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Shutdown();
                    return;
                }

                // Check for auto updates from global Windows Server
                AutoUpdateHelper.Initialize();

                ConfigureServices();

                if (_serviceProvider != null)
                {
                    MainWindow = _serviceProvider.GetRequiredService<MainWindow>();
                    MainWindow.Show();
                }
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Critical failure during application startup.");
                MessageBox.Show($"Failed to start application: {ex.Message}", "Critical Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
                return;
            }

            base.OnStartup(e);
        }

        private void SetupExceptionHandling()
        {
            DispatcherUnhandledException += (s, args) =>
            {
                Log.Error(args.Exception, "Unhandled UI Thread Exception");
                args.Handled = true; // Prevent app crash
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    Log.Fatal(ex, "Unhandled AppDomain Exception");
                }
            };

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                Log.Error(args.Exception, "Unobserved Task Exception");
                args.SetObserved();
            };
        }

        private void ConfigureServices()
        {
            IServiceCollection services = new ServiceCollection();
            
            HttpClient sharedHttpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            sharedHttpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "PostmanRuntime/7.56.1");
            sharedHttpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
            services.AddSingleton(sharedHttpClient);

            services.AddSingleton<TokenManager>();
            services.AddSingleton<GetOrderData>();
            services.AddSingleton<VendorOrderStatusService>();
            services.AddSingleton<MainViewModel>();
            services.AddTransient(s => new MainWindow
            {
                DataContext = s.GetRequiredService<MainViewModel>()
            });


            _serviceProvider = services.BuildServiceProvider();
        }

                public static void ReleaseAppMutex()
        {
            if (_mutex != null && _hasMutex)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch { }
                finally
                {
                    _mutex.Dispose();
                    _mutex = null;
                    _hasMutex = false;
                }
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("Application exiting...");

            if (_mutex != null && _hasMutex)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error releasing application mutex.");
                }
                finally
                {
                    _mutex.Dispose();
                    _mutex = null;
                }
            }

            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}

