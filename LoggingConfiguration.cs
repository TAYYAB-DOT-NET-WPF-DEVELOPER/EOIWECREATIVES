using Serilog;
using System;
using System.IO;

namespace WeCreatives_EOI
{
    public static class LoggingConfiguration
    {
        public static void ConfigureLogging()
        {
            try
            {
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "Console");
                Directory.CreateDirectory(logDir);

                string logFilePath = Path.Combine(logDir, "OrderLog.txt");

                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .WriteTo.File(
                        logFilePath,
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 14,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
                    )
                    .CreateLogger();
            }
            catch (Exception ex)
            {
                // Fallback logging if log creation fails
                Console.WriteLine($"Failed to configure logging: {ex.Message}");
            }
        }
    }
}

