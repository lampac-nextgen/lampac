#nullable enable
using System;
using System.IO;

namespace QRAuth.Services
{
    /// <summary>Minimal append-only file logger for this module. Independent of the host's
    /// own logging pipeline so bot errors stay inspectable even when the host only logs to
    /// a console that isn't captured anywhere (the common case for Lampac deployments).</summary>
    public static class FileLog
    {
        static readonly object _lock = new();
        static string _path = "tgbot.log";

        public static void Configure(string path) => _path = path;

        public static void Write(string message)
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}";
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(_path, line + Environment.NewLine);
                }
            }
            catch
            {
                // logging must never take the bot down
            }
        }

        public static void Write(string message, Exception ex) =>
            Write(message + Environment.NewLine + ex);
    }
}
