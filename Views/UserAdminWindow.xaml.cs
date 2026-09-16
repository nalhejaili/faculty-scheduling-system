using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TrainerScheduler.Data.Entities;
using TrainerScheduler.Security;
using TrainerScheduler.Services;

namespace MiniTrainerScheduler.Views
{
    public partial class UserAdminWindow : Window
    {
        private readonly SqliteStoreService _sqlite = new();
        private List<DepartmentEntity> _departments = new();

        public UserAdminWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!AuthContext.IsAdmin)
                {
                    MessageBox.Show("This screen is available to the system administrator only.", "Permissions", MessageBoxButton.OK, MessageBoxImage.Information);
                    Close();
                    return;
                }

                // Ensure database exists and the Users table is present.
                await _sqlite.InitializeAsync();

                // Be defensive: if XAML name binding fails for any reason, don't crash.
                if (DbPathText is not null)
                    DbPathText.Text = $"Database: {_sqlite.DatabasePath}";

                await ReloadAsync();
            }
            catch (Exception ex)
            {
                UiError.Show("Users", "Unable to open the User Management window.", ex);
                Close();
            }
        }

        private async Task ReloadAsync()
        {
            try
            {
                if (DepartmentBox is null || UsersGrid is null || RoleBox is null)
                    throw new InvalidOperationException("Unable to initialize User Management controls (DepartmentBox/UsersGrid/RoleBox). Verify the integrity of UserAdminWindow.xaml.");

                _departments = await _sqlite.GetDepartmentsAsync();
                DepartmentBox.ItemsSource = _departments;

                var users = await _sqlite.GetUsersAsync();
                var rows = users.Select(u => new UserRow
                {
                    Id = u.Id,
                    Username = u.Username,
                    Role = u.Role,
                    DepartmentId = u.DepartmentId,
                    DepartmentName = u.DepartmentId is null ? "" : (_departments.FirstOrDefault(d => d.Id == u.DepartmentId.Value)?.Name ?? ""),
                    CreatedUtc = u.CreatedUtc
                }).OrderBy(r => r.Id).ToList();

                UsersGrid.ItemsSource = rows;

                // Default department selection
                if (DepartmentBox.SelectedItem is null && _departments.Count > 0)
                    DepartmentBox.SelectedItem = _departments[0];

                ApplyRoleUi();
            }
            catch (Exception ex)
            {
                UiError.Show("Error", "Unable to load users.", ex);
            }
        }

        private void ApplyRoleUi()
        {
            var role = GetSelectedRole();
            if (DepartmentBox is null) return;

            DepartmentBox.IsEnabled = role == UserRole.Supervisor;
            if (role != UserRole.Supervisor)
                DepartmentBox.SelectedItem = null;
            else if (DepartmentBox.SelectedItem is null && _departments.Count > 0)
                DepartmentBox.SelectedItem = _departments[0];
        }

        private UserRole GetSelectedRole()
        {
            if (RoleBox.SelectedItem is ComboBoxItem cbi && cbi.Tag is string tag)
            {
                if (Enum.TryParse<UserRole>(tag, ignoreCase: true, out var role))
                    return role;
            }
            return UserRole.Supervisor;
        }

        private int? GetSelectedDepartmentIdForSupervisor()
        {
            return DepartmentBox.SelectedValue is int id ? id : (int?)null;
        }

        private async void Create_Click(object sender, RoutedEventArgs e)
        {
            var username = (UsernameBox.Text ?? string.Empty).Trim();
            var password = PasswordBox.Password ?? string.Empty;
            var role = GetSelectedRole();
            var deptId = GetSelectedDepartmentIdForSupervisor();

            var (ok, err) = await _sqlite.CreateUserAsync(username, password, role, deptId);
            if (!ok)
            {
                MessageBox.Show(err, "Unable to Create User", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            UsernameBox.Text = string.Empty;
            PasswordBox.Password = string.Empty;
            await ReloadAsync();

            MessageBox.Show("User created successfully.", "Completed", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void ResetPassword_Click(object sender, RoutedEventArgs e)
        {
            if (UsersGrid.SelectedItem is not UserRow row)
            {
                MessageBox.Show("Select a user first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SetPasswordWindow(row.Username) { Owner = this };
            if (dlg.ShowDialog() != true)
                return;

            var (ok, err) = await _sqlite.ResetPasswordAsync(row.Id, dlg.NewPassword);
            if (!ok)
            {
                MessageBox.Show(err, "Unable to Change Password", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MessageBox.Show("Password updated successfully.", "Completed", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (UsersGrid.SelectedItem is not UserRow row)
            {
                MessageBox.Show("Select a user first.", "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (AuthContext.Current is not null && row.Id == AuthContext.Current.UserId)
            {
                MessageBox.Show("You cannot delete your current account.", "Notice", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"Are you sure you want to delete the user: {row.Username}?",
                "Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes)
                return;

            var (ok, err) = await _sqlite.DeleteUserAsync(row.Id);
            if (!ok)
            {
                MessageBox.Show(err, "Unable to Delete User", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await ReloadAsync();
        }

        private void RoleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyRoleUi();
        }

        private async void Reload_Click(object sender, RoutedEventArgs e)
        {
            await ReloadAsync();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private sealed class UserRow
        {
            public int Id { get; set; }
            public string Username { get; set; } = string.Empty;
            public string Role { get; set; } = string.Empty;
            public int? DepartmentId { get; set; }
            public string DepartmentName { get; set; } = string.Empty;
            public DateTime CreatedUtc { get; set; }

            public string RoleText => string.Equals(Role, UserRole.Admin.ToString(), StringComparison.OrdinalIgnoreCase)
                ? "System Administrator"
                : "Department Supervisor";

            public string CreatedText => CreatedUtc == default
                ? string.Empty
                : CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        }
    }
}
