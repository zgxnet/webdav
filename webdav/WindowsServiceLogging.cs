using System.Runtime.Versioning;
using Microsoft.Extensions.Logging.EventLog;

namespace WebDav;

[SupportedOSPlatform("windows")]
internal static class WindowsServiceLogging
{
    public static void Add(ILoggingBuilder logging)
    {
        var settings = new EventLogSettings
        {
            LogName = "Application",
            SourceName = "WebDav",
            Filter = (_, logLevel) => logLevel >= LogLevel.Information
        };

        logging.AddEventLog(settings);
    }
}
