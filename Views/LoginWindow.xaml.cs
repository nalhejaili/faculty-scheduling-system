using System;
using System.Threading.Tasks;
using System.Windows;
using TrainerScheduler.Network;
using TrainerScheduler.Security;
using TrainerScheduler.Services;

namespace MiniTrainerScheduler.Views
{
    public partial class LoginWindow : Window
    {
        private readonly SqliteStoreService _sqlite = new();
        private LanSettings _lanSettings = new();
        private bool _isLanClient;
        private bool _isFirstAdminMode;

        public LoginWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializeUiAsync();
        }

        private async Task InitializeUiAsync()
        {
            try
            {
                ErrorText.Text = string.Empty;

                // Determine LAN mode first
                _lanSettings = LanSettingsStore.LoadOrDefault();
                _isLanClient = _lanSettings.Mode == LanMode.Client;

                if (_isLanClient)
                {
                    // Client devices MUST login via the Admin server (no local users here)
                    EnterLoginMode();
                    HintText.Text = "Sign in through the central network server.";
                    DbPathText.Text = $"Server: {_lanSettings.ServerHost}:{_lanSettings.Port}";
                    return;
                }

                // Ensure DB exists + users table exists
                var hasAnyUsers = await _sqlite.AnyUsersAsync();

                DbPathText.Text = $"Database: {_sqlite.DatabasePath}";

                if (!hasAnyUsers)
                {
                    EnterFirstAdminMode();
                }
                else
                {
                    EnterLoginMode();
                }
            }
            catch (Exception ex)
            {
                ErrorText.Text = $"Unable to initialize sign-in.\n\n{ex.Message}";
                EnterLoginMode();
            }
        }

        private async void OpenLanSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var w = new LanSettingsWindow();
                w.Owner = this;
                w.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open network settings.\n{ex.Message}", "Network",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                await InitializeUiAsync();
            }
        }

        private void EnterFirstAdminMode()
        {
            _isFirstAdminMode = true;
            TitleText.Text = "Create First Administrator";
            HintText.Text = "No accounts exist yet. Create the first system administrator account to continue.";

            LoginPanel.Visibility = Visibility.Collapsed;
            FirstAdminPanel.Visibility = Visibility.Visible;

            PrimaryActionButton.Content = "Create Administrator and Sign In";

            AdminUsernameBox.Focus();
        }

        private void EnterLoginMode()
        {
            _isFirstAdminMode = false;
            TitleText.Text = "Sign In";
            HintText.Text = "Enter your credentials to continue.";

            FirstAdminPanel.Visibility = Visibility.Collapsed;
            LoginPanel.Visibility = Visibility.Visible;

            PrimaryActionButton.Content = "Sign In";

            LoginUsernameBox.Focus();
        }

        private async void PrimaryActionButton_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Text = string.Empty;

            if (_isFirstAdminMode)
                await HandleCreateFirstAdminAsync();
            else
                await HandleLoginAsync();
        }

        private async Task HandleCreateFirstAdminAsync()
        {
            var username = (AdminUsernameBox.Text ?? string.Empty).Trim();
            var pass = AdminPasswordBox.Password ?? string.Empty;
            var confirm = AdminPasswordConfirmBox.Password ?? string.Empty;

            if (username.Length == 0)
            {
                ErrorText.Text = "Username is required.";
                AdminUsernameBox.Focus();
                return;
            }

            if (pass.Length < 4)
            {
                ErrorText.Text = "Password is too short (minimum 4 characters).";
                AdminPasswordBox.Focus();
                return;
            }

            if (confirm.Length == 0)
            {
                ErrorText.Text = "Password confirmation is required.";
                AdminPasswordConfirmBox.Focus();
                return;
            }

            if (!string.Equals(pass, confirm, StringComparison.Ordinal))
            {
                ErrorText.Text = "Passwords do not match.";
                AdminPasswordConfirmBox.Focus();
                return;
            }

            var (ok, err) = await _sqlite.CreateFirstAdminAsync(username, pass);
            if (!ok)
            {
                ErrorText.Text = err;
                return;
            }

            // Auto-login after creation
            var (session, loginErr) = await _sqlite.LoginAsync(username, pass);
            if (session is null)
            {
                ErrorText.Text = string.IsNullOrWhiteSpace(loginErr)
                    ? "The administrator account was created, but automatic sign-in failed. Please sign in manually." 
                    : loginErr;

                EnterLoginMode();
                LoginUsernameBox.Text = username;
                LoginPasswordBox.Focus();
                return;
            }

            AuthContext.Set(session);
            DialogResult = true;
            Close();
        }

        private async Task HandleLoginAsync()
        {
            var username = (LoginUsernameBox.Text ?? string.Empty).Trim();
            var pass = LoginPasswordBox.Password ?? string.Empty;

            if (username.Length == 0)
            {
                ErrorText.Text = "Username is required.";
                LoginUsernameBox.Focus();
                return;
            }

            if (pass.Length == 0)
            {
                ErrorText.Text = "Password is required.";
                LoginPasswordBox.Focus();
                return;
            }

            UserSession? session;
            string err;

            if (_isLanClient)
            {
                var (s, token, e) = await LanAuthClient.LoginAsync(_lanSettings, username, pass);
                session = s;
                err = e;

                if (session is not null && !string.IsNullOrWhiteSpace(token))
                {
                    LanClientContext.Set(_lanSettings, token!, session);
                }
            }
            else
            {
                var result = await _sqlite.LoginAsync(username, pass);
                session = result.session;
                err = result.errorMessage;
            }

            if (session is null)
            {
                ErrorText.Text = string.IsNullOrWhiteSpace(err) ? "Invalid username or password." : err;
                LoginPasswordBox.SelectAll();
                LoginPasswordBox.Focus();
                return;
            }

            AuthContext.Set(session);
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            AuthContext.Clear();
            LanClientContext.Clear();
            DialogResult = false;
            Close();
        }
    }
}
