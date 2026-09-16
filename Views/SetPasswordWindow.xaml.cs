using System;
using System.Windows;

namespace MiniTrainerScheduler.Views
{
    public partial class SetPasswordWindow : Window
    {
        public string NewPassword { get; private set; } = string.Empty;

        public SetPasswordWindow(string username)
        {
            InitializeComponent();
            TitleText.Text = $"Change Password for User: {username}";
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            ErrorText.Text = string.Empty;

            var pass = PasswordBox.Password ?? string.Empty;
            var confirm = ConfirmBox.Password ?? string.Empty;

            if (pass.Length < 4)
            {
                ErrorText.Text = "Password is too short (minimum 4 characters).";
                PasswordBox.Focus();
                return;
            }

            if (!string.Equals(pass, confirm, StringComparison.Ordinal))
            {
                ErrorText.Text = "Passwords do not match.";
                ConfirmBox.Focus();
                return;
            }

            NewPassword = pass;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
