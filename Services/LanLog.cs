using System;
using System.IO;
using System.Text;

namespace TrainerScheduler.Services
{
    /// <summary>
    /// Lightweight LAN diagnostics log (non-throwing).
    /// Location: LocalApplicationData/TrainerScheduler/logs/lan.log
    /// </summary>
    public static class LanLog
    {
        public static string LogPath
        {
            get
            {
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TrainerScheduler",
                    "logs");

                Directory.CreateDirectory(logDir);
                return Path.Combine(logDir, "lan.log");
            }
        }

        public static void Info(string message) => Write("INFO", message, null);
        public static void Warn(string message) => Write("WARN", message, null);
        public static void Error(string message, Exception? ex = null) => Write("ERR", message, ex);

        

        public static string ReadTail(int maxLines = 200)
        {
            try
            {
                if (maxLines < 10) maxLines = 10;
                if (!File.Exists(LogPath)) return "No log entries are available yet.";

                // Read all lines for simplicity (log file is typically small).
                var lines = File.ReadAllLines(LogPath, Encoding.UTF8);
                if (lines.Length == 0) return "No log entries are available yet.";

                var start = Math.Max(0, lines.Length - maxLines);
                return string.Join(Environment.NewLine, lines, start, lines.Length - start);
            }
            catch
            {
                return "Unable to read the log.";
            }
        }

private static void Write(string level, string message, Exception? ex)
        {
            try
            {
                var line = new StringBuilder();
                line.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ");
                line.Append(level).Append(" | ");
                line.Append(message);
                if (ex is not null)
                {
                    line.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
                }

                File.AppendAllText(LogPath, line.ToString() + Environment.NewLine, Encoding.UTF8);
            }
            catch
            {
                // Never crash due to logging.
            }
        }
    }
}
