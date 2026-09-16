using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using MiniTrainerScheduler.ViewModels;
using TrainerScheduler.Network;
using TrainerScheduler.Security;
using TrainerScheduler.Services;

namespace MiniTrainerScheduler.Views;

public partial class LanSettingsWindow : Window
{
    private LanSettings _settings = new();

    private static bool IsLoopbackHost(string? host)
    {
        var h = (host ?? string.Empty).Trim().ToLowerInvariant();
        return h == "127.0.0.1" || h == "localhost" || h == "::1" || h == "0.0.0.0";
    }

    public LanSettingsWindow()
    {
        InitializeComponent();
        Loaded += LanSettingsWindow_Loaded;
    }

    private void LanSettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _settings = LanSettingsStore.LoadOrDefault();

            LocalOnlyRadio.IsChecked = _settings.Mode == LanMode.LocalOnly;
            ServerRadio.IsChecked = _settings.Mode == LanMode.Server;
            ClientRadio.IsChecked = _settings.Mode == LanMode.Client;

            ServerHostText.Text = _settings.ServerHost;

            // If this machine is configured as a Client, do NOT default to loopback.
            // 127.0.0.1 means "this machine", but client must point to the Admin machine.
            if (_settings.Mode == LanMode.Client && IsLoopbackHost(ServerHostText.Text))
                ServerHostText.Text = string.Empty;

            PortText.Text = _settings.Port.ToString();
            AutoStartCheck.IsChecked = _settings.AutoStartServerForAdmin;


            // Local IPs helper
            var ips = LanNetworkHelper.GetLocalIpv4Addresses();
            LocalIpsText.Text = ips.Count == 0
                ? "No IPv4 address was found. Check the network connection."
                : string.Join("  |  ", ips);

            RefreshServerStatus();
            ApplyRoleUiRules();

            RefreshLogPreview();
        }
        catch (Exception ex)
        {
            UiError.Show("Network", "Unable to load network settings.", ex);
        }
    }

    private void ApplyRoleUiRules()
    {
        // Supervisors can configure client host/port and test, but cannot start a LAN server.
        if (!AuthContext.IsAdmin)
        {
            StartServerButton.IsEnabled = false;
            StopServerButton.IsEnabled = false;

            // Still allow selecting client mode or local mode
            AutoStartCheck.IsEnabled = false;
        }
    }

    private void RefreshServerStatus()
    {
        var running = LanServerManager.IsRunning;
        if (running)
        {
            var port = LanServerManager.Port ?? _settings.Port;
            ServerStatusText.Text = $"✅ Running now on port: {port}";
        }
        else
        {
            ServerStatusText.Text = "⏹ Service stopped";
        }
    }

    private LanMode ReadSelectedMode()
    {
        if (ServerRadio.IsChecked == true) return LanMode.Server;
        if (ClientRadio.IsChecked == true) return LanMode.Client;
        return LanMode.LocalOnly;
    }

    private bool TryReadPort(out int port)
    {
        port = 0;
        if (!int.TryParse((PortText.Text ?? "").Trim(), out port))
            return false;
        return port > 0 && port <= 65535;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!TryReadPort(out var port))
            {
                MessageBox.Show("The port is invalid. Enter a number between 1 and 65535.", "Network", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var mode = ReadSelectedMode();
            var host = (ServerHostText.Text ?? "").Trim();

            if (mode == LanMode.Client && IsLoopbackHost(host))
            {
                MessageBox.Show(
                    "You are using Client mode. Enter the administrator device IP address here.\n\n" +
                    "Example: 192.0.2.10\n\n" +
                    "Note: 127.0.0.1 refers to your own device and will not connect to the server.",
                    "Network",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            _settings.Mode = mode;
            _settings.ServerHost = host;
            _settings.Port = port;
            _settings.AutoStartServerForAdmin = AutoStartCheck.IsChecked == true;

            LanSettingsStore.Save(_settings);

            // Apply immediately (no restart required)
		            if (Application.Current.MainWindow?.DataContext is MainViewModel vm)
		                vm.IsLanAutoRefreshEnabled = _settings.AutoRefreshEnabled;
            MessageBox.Show("Network settings saved.", "Network", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            UiError.Show("Network", "Unable to save network settings.", ex);
        }
    }

    private async void StartServer_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthContext.IsAdmin)
        {
            MessageBox.Show("Starting the server is available to the system administrator only.", "Network", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            if (!TryReadPort(out var port))
            {
                MessageBox.Show("The port is invalid. Enter a number between 1 and 65535.", "Network", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await LanServerManager.StartAsync(port);
            RefreshServerStatus();

            MessageBox.Show(
                "The server has started.\n\n" +
                "If Windows Firewall prompts you, choose Allow.",
                "Network",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            UiError.Show("Network", "Unable to start the server.", ex);
        }
    }

    private async void StopServer_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthContext.IsAdmin)
            return;

        try
        {
            await LanServerManager.StopAsync();
            RefreshServerStatus();
        }
        catch (Exception ex)
        {
            UiError.Show("Network", "Unable to stop the server.", ex);
        }
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!TryReadPort(out var port))
            {
                MessageBox.Show("The port is invalid. Enter a number between 1 and 65535.", "Network", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var host = (ServerHostText.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(host))
            {
                MessageBox.Show("Enter the server IP address first.", "Network", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Client mode cannot use loopback.
            if (ReadSelectedMode() == LanMode.Client && IsLoopbackHost(host))
            {
                MessageBox.Show(
                    "You are using Client mode. Enter the administrator device IP address instead of 127.0.0.1.\n\n" +
                    "Example: 192.0.2.10",
                    "Network",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            string baseUrl;
            if (host.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                // If user typed a URL without a port, add the configured port.
                if (Uri.TryCreate(host, UriKind.Absolute, out var u) && u.IsDefaultPort && port != 80)
                {
                    baseUrl = $"{u.Scheme}://{u.Host}:{port}";
                }
                else
                {
                    baseUrl = host;
                }
            }
            else
            {
                baseUrl = $"http://{host}:{port}";
            }

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var url = baseUrl.TrimEnd('/') + "/health";
            var body = await http.GetStringAsync(url);

            if (body.Trim().Equals("ok", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("✅ Connection successful.", "Network", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"Connected successfully, but the response was unexpected: {body}", "Network", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            UiError.Show("Network", "Connection test failed. Make sure the server is running and the firewall port is open.", ex);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = LanLog.LogPath;
            if (!File.Exists(path))
            {
                MessageBox.Show("No network log is available yet. Try testing the connection or running synchronization, and the log will appear here.", "Network", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            UiError.Show("Network", "Unable to open the network log.", ex);
        }
    }

    private void RefreshLog_Click(object sender, RoutedEventArgs e)
    {
        RefreshLogPreview();
    }

    private void RefreshLogPreview()
    {
        try
        {
            // Show last lines to help diagnose LAN issues quickly.
            LanLogPreviewText.Text = LanLog.ReadTail(80);
        }
        catch (Exception ex)
        {
            LanLogPreviewText.Text = "Unable to display the network log.";
            LanLog.Error("Failed to read LAN log preview", ex);
        }
    }

}
