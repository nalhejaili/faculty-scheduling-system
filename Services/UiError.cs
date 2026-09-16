using System;
using System.IO;
using System.Text;
using System.Windows;

namespace TrainerScheduler.Services
{
    /// <summary>
    /// Show a friendly error message and log full exception details to a file.
    /// Log location is under LocalApplicationData/TrainerScheduler/logs.
    /// </summary>
    public static class UiError
    {
        public static void Show(string title, string userMessage, Exception ex)
        {
            try
            {
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TrainerScheduler",
                    "logs");

                Directory.CreateDirectory(logDir);

                var fileName = $"error_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.txt";
                var fullPath = Path.Combine(logDir, fileName);

                File.WriteAllText(fullPath, ex.ToString(), Encoding.UTF8);

                MessageBox.Show(
                    $"{userMessage}\n\n{ex.Message}\n\nError details were saved to the following file:\n{fullPath}",
                    title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch
            {
                // If logging fails, at least show full exception details.
                MessageBox.Show(
                    $"{userMessage}\n\n{ex}",
                    title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }
}
