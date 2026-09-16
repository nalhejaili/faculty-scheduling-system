using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;
using MiniTrainerScheduler.Services;
using TrainerScheduler.Services;

namespace MiniTrainerScheduler.Views
{
    public partial class AboutWindow : Window
    {
        public string AppTitle { get; }
        public string AppVersion { get; }
        public DateTime BuildDate { get; }
        public string DeveloperName { get; }
        public string DeveloperEmail { get; }
        public string DeveloperSpeciality { get; } = "M.Sc. in Electrical Engineering and Systems Design";
        public string DeveloperOrganization { get; } = "Independent Portfolio Project";
        public string LicenseActivation => LicenseService.ActivationText;
        public string LicenseSerial => string.IsNullOrWhiteSpace(LicenseService.Serial) ? "—" : LicenseService.Serial;
        public string LicensePlan => LicenseService.PlanDisplay;
        public string LicenseExpiry => LicenseService.ExpiryDisplay;
        public string LicenseDaysLeft => LicenseService.DaysLeftDisplay;

        public string Copyright { get; }

        public AboutWindow()
        {
            InitializeComponent();

            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            AppTitle = "University Academic Timetable Suite";
            AppVersion = asm.GetName().Version?.ToString() ?? "1.0.0";
            try
            {
                // In single-file publish, Assembly.Location may be empty; use the actual process path instead.
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
                    BuildDate = File.GetLastWriteTime(exePath);
                else
                    BuildDate = Directory.GetLastWriteTime(AppContext.BaseDirectory);
            }
            catch
            {
                BuildDate = DateTime.Now;
            }
            DeveloperName = "Nash Alj";
            DeveloperEmail = "n.alhejaili1@gmail.com";

            Copyright = $"© {DateTime.Now:yyyy} University Academic Timetable Suite. All rights reserved.";

            DataContext = this;

            SafeSetImage(AppLogo,
                "/Assets/Logos/app_logo.png");
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void OpenLicenseFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TrainerScheduler");
                Directory.CreateDirectory(folder);
                Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            }
            catch { /* ignore */ }
        }

        private void Renew_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new ActivationPromptWindow { Owner = this };
                var res = dlg.ShowDialog();
                if (res == true)
                {
                    if (!LicenseService.ActivateWithSerial(dlg.EnteredSerial, out var message))
                    {
                        MessageBox.Show(
                            string.IsNullOrWhiteSpace(message) ? "The serial number is not valid for this device." : message,
                            "Activation Failed",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    MessageBox.Show("Activation completed successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);

                    Close();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("An error occurred during activation.\n" + ex.Message,
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void SafeSetImage(System.Windows.Controls.Image img, params string[] relativeCandidates)
        {
            var asmName = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly())
                          .GetName().Name ?? "TrainerScheduler";

            foreach (var rel in relativeCandidates)
            {
                try
                {
                    var pack = new Uri($"pack://application:,,,/{asmName};component{rel}", UriKind.Absolute);
                    img.Source = new BitmapImage(pack);
                    return;
                }
                catch (Exception ex)
            {
                DiagnosticsLog.TryWrite(ex);
            }

                try
                {
                    var abs = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, rel.TrimStart('/', '\\'));
                    if (File.Exists(abs))
                    {
                        img.Source = new BitmapImage(new Uri(abs, UriKind.Absolute));
                        return;
                    }
                }
                catch (Exception ex)
            {
                DiagnosticsLog.TryWrite(ex);
            }
            }
        }
    }
}
