using System.Diagnostics;
using System.IO;

namespace wpfhikip.Protocols.Onvif
{
    /// <summary>
    /// Configuration and utilities for ONVIF debugging
    /// </summary>
    public static class OnvifDebugging
    {
        public static bool EnableDetailedLogging { get; set; } = true;
        public static bool EnableSoapLogging { get; set; } = true;
        public static bool EnableTimingLogging { get; set; } = true;
        public static bool EnableStackTraceLogging { get; set; } = true;

        public static void ConfigureFromEnvironment()
        {
            EnableDetailedLogging = Environment.GetEnvironmentVariable("ONVIF_DETAILED_LOGGING")?.ToLower() == "true";
            EnableSoapLogging = Environment.GetEnvironmentVariable("ONVIF_SOAP_LOGGING")?.ToLower() == "true";
            EnableTimingLogging = Environment.GetEnvironmentVariable("ONVIF_TIMING_LOGGING")?.ToLower() == "true";
            EnableStackTraceLogging = Environment.GetEnvironmentVariable("ONVIF_STACKTRACE_LOGGING")?.ToLower() == "true";
        }

        public static void LogToFile(string message, string category = "ONVIF")
        {
            var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "wpfhikip", "logs");
            Directory.CreateDirectory(logDir);

            var logFile = Path.Combine(logDir, $"onvif-debug-{DateTime.Now:yyyy-MM-dd}.log");
            var logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{category}] {message}{Environment.NewLine}";

            File.AppendAllText(logFile, logEntry);
        }

        public static void EnableActivitySourceLogging()
        {
            ActivitySource.AddActivityListener(new ActivityListener
            {
                ShouldListenTo = _ => true,
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = activity => Debug.WriteLine($"Activity Started: {activity.DisplayName}"),
                ActivityStopped = activity => Debug.WriteLine($"Activity Stopped: {activity.DisplayName} ({activity.Duration.TotalMilliseconds}ms)")
            });
        }
    }
}