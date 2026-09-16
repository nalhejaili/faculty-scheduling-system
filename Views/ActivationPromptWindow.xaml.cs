using MiniTrainerScheduler.Services;
using System;
using System.Windows;
using TrainerScheduler.Services;

namespace MiniTrainerScheduler.Views
{
    public partial class ActivationPromptWindow : Window
    {
        public string EnteredSerial { get; private set; } = string.Empty;

        public ActivationPromptWindow()
        {
            InitializeComponent();

            MachineIdTextBlock.Text = LicenseService.MachineIdDisplay;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            SerialTextBox.Focus();
        }

        private void Activate_Click(object sender, RoutedEventArgs e)
        {
            var serial = SerialTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(serial))
            {
                MessageBox.Show(
                    "Please enter the license serial.",
                    "Notice",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                SerialTextBox.Focus();
                return;
            }

            if (LicenseService.TryActivateFromSerial(serial, out var error))
            {
                EnteredSerial = serial;

                MessageBox.Show(
                    "The license has been activated successfully.",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                DialogResult = true;
            }
            else
            {
                MessageBox.Show(
                    error,
                    "Activation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                DialogResult = false;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void CopyMachineId_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var machineId = LicenseService.MachineIdDisplay;

                if (!string.IsNullOrWhiteSpace(machineId))
                {
                    Clipboard.SetText(machineId);
                    MessageBox.Show(
                        "The device identifier has been copied to the clipboard.",
                        "Copied",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Unable to copy the device identifier. You can copy it manually.\n\n" + ex.Message,
                    "Copy Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
