using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace TrainerScheduler.Services
{
    /// <summary>
    /// Lightweight diagnostic logger for suppressed/non-fatal exceptions.
    /// Writes to %LocalAppData%\TrainerScheduler\crash.log (same location used by the global crash handler).
    /// </summary>
    internal static class DiagnosticsLog
    {
        private static readonly object _sync = new();

        public static void TryWrite(Exception ex, string? title = null,
            [CallerFilePath] string? filePath = null,
            [CallerMemberName] string? memberName = null,
            [CallerLineNumber] int lineNumber = 0)
        {
            try
            {
                var baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TrainerScheduler");
                Directory.CreateDirectory(baseDir);

                var path = Path.Combine(baseDir, "crash.log");
                var now = DateTime.Now;

                var sb = new StringBuilder();
                sb.AppendLine("==== Suppressed exception ====");
                sb.AppendLine($"Time: {now:yyyy-MM-dd HH:mm:ss}");
                if (!string.IsNullOrWhiteSpace(title))
                    sb.AppendLine($"Title: {title}");
                if (!string.IsNullOrWhiteSpace(filePath))
                    sb.AppendLine($"At: {Path.GetFileName(filePath)}:{lineNumber} ({memberName})");
                sb.AppendLine(ex.ToString());
                sb.AppendLine();

                lock (_sync)
                {
                    File.AppendAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                }

                Debug.WriteLine(sb.ToString());
            }
            catch
            {
                // Never throw from diagnostic logging.
            }
        }
    }
}
