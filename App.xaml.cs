using MiniTrainerScheduler;
using MiniTrainerScheduler.Views;
using System.Windows;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using System.Windows.Threading;
using System.Windows.Controls;
using TrainerScheduler.Network;
using TrainerScheduler.Services;

namespace TrainerScheduler
{
    public partial class App : Application
    {
        // NOTE:
        // DataGridCheckBoxColumn uses theme keys for its internal CheckBox style,
        // so an implicit CheckBox style might not affect it. To ensure a consistent
        // ✓ icon across the whole app (including DataGrids), we enforce our style
        // at runtime for any DataGridCheckBoxColumn.
        private static bool _dataGridHandlerRegistered;

        private static void EnsureGlobalDataGridCheckBoxStyle()
        {
            if (_dataGridHandlerRegistered) return;
            _dataGridHandlerRegistered = true;

            EventManager.RegisterClassHandler(
                typeof(DataGrid),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnAnyDataGridLoaded));
        }

        private static void OnAnyDataGridLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not DataGrid dg) return;

            // Prefer the DataGrid-specific one (centered), fall back to the base one.
            var style = Current?.TryFindResource("AppDataGridCheckBoxStyle") as Style
                        ?? Current?.TryFindResource("AppCheckBoxStyle") as Style;

            if (style is null) return;

            foreach (var col in dg.Columns.OfType<DataGridCheckBoxColumn>())
            {
                col.ElementStyle = style;
                col.EditingElementStyle = style;
            }
        }

        private static void WriteCrashLog(string title, Exception? ex)
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrainerScheduler");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, "crash.log");

                var sb = new StringBuilder();
                sb.AppendLine("==============================");
                sb.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine(title);
                if (ex is not null)
                {
                    sb.AppendLine(ex.ToString());
                }
                sb.AppendLine();

                File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch
            {
                // ignore
            }
        }

        private void App_Startup(object sender, StartupEventArgs e)
        {
            // Global safety net: show a friendly message instead of crashing silently.
            DispatcherUnhandledException += (_, args) =>
            {
                WriteCrashLog("DispatcherUnhandledException", args.Exception);
                MessageBox.Show($"An unexpected error occurred. The application will continue if possible.\n\n{args.Exception.Message}\n\nDetails were saved to crash.log in the TrainerScheduler folder.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                WriteCrashLog("AppDomain.UnhandledException", ex);
            };

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                WriteCrashLog("TaskScheduler.UnobservedTaskException", args.Exception);
                args.SetObserved();
            };

	            // Ensure DataGrid checkbox columns use the app-wide checkbox template.
	            EnsureGlobalDataGridCheckBoxStyle();

            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var ok = LicenseService.LoadAndValidate();

            if (!ok)
            {
                var activation = new ActivationPromptWindow
                {
                    Owner = null,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };

                bool? result = activation.ShowDialog();

                ok = LicenseService.LoadAndValidate();

                if (result != true || !ok)
                {
                    MessageBox.Show(
                        "The application is not activated, or the license has expired.\nThe application will now close.",
                        "License",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    Shutdown();
                    return;
                }
            }

            var login = new LoginWindow
            {
                Owner = null,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };

            bool? loginResult = login.ShowDialog();

            if (loginResult != true)
            {
                Shutdown();
                return;
            }

            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();

            LanBootstrapper.TryAutoStartForCurrentUser(mainWindow);
        }

        private async void App_Exit(object sender, ExitEventArgs e)
        {
            // Ensure LAN server is stopped cleanly.
            try
            {
                await LanServerManager.StopAsync();
            }
            catch
            {
                // ignore
            }
        }
    }
}
