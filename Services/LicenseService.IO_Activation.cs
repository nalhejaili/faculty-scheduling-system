using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace TrainerScheduler.Services
{
    public static partial class LicenseService
    {
        public static bool LoadAndValidate()
        {
            try
            {
                if (!File.Exists(LicensePath))
                {
                    ResetToFree();
                    return false;
                }

                string json = File.ReadAllText(LicensePath, Encoding.UTF8);
                LicenseInfo info = JsonSerializer.Deserialize<LicenseInfo>(json) ?? new LicenseInfo();

                if (string.IsNullOrWhiteSpace(info.Serial))
                {
                    ResetToFree();
                    return false;
                }

                if (string.Equals(info.Serial, "FREE", StringComparison.OrdinalIgnoreCase))
                {
                    _license = new LicenseInfo
                    {
                        Serial = "FREE",
                        MachineId = MachineId,
                        ExpiryDate = null,
                        Plan = "FREE"
                    };

                    HasSavedLicense = true;
                    UpdateDisplayStrings();
                    return true;
                }

                string error;
                LicensePayload payload;
                if (!TryDecodeSerial(info.Serial, out payload, out error))
                {
                    ResetToFree();
                    return false;
                }

                string currentMachine = MachineId;
                if (!string.Equals(payload.MachineId, currentMachine, StringComparison.OrdinalIgnoreCase))
                {
                    ResetToFree();
                    return false;
                }

                _license = new LicenseInfo
                {
                    Serial = info.Serial,
                    MachineId = payload.MachineId,
                    ExpiryDate = payload.Expiry,
                    Plan = payload.Plan
                };

                HasSavedLicense = true;
                UpdateDisplayStrings();
                return !IsExpired;
            }
            catch
            {
                ResetToFree();
                return false;
            }
        }

        public static bool TryActivate(string serial, out string message)
        {
            serial = (serial ?? string.Empty).Trim();

            if (serial.Length == 0)
            {
                message = "No serial key was entered.";
                return false;
            }

            if (string.Equals(serial, "FREE", StringComparison.OrdinalIgnoreCase))
            {
                _license = new LicenseInfo
                {
                    Serial = "FREE",
                    MachineId = MachineId,
                    ExpiryDate = null,
                    Plan = "FREE"
                };

                SaveLicense();
                UpdateDisplayStrings();
                message = "Activation completed (free edition).";
                return true;
            }

            string error;
            LicensePayload payload;
            if (!TryDecodeSerial(serial, out payload, out error))
            {
                message = error ?? "The serial key is invalid.";
                return false;
            }

            string currentMachine = MachineId;
            if (!string.Equals(payload.MachineId, currentMachine, StringComparison.OrdinalIgnoreCase))
            {
                message = "This serial key belongs to another device.";
                return false;
            }

            _license = new LicenseInfo
            {
                Serial = serial,
                MachineId = payload.MachineId,
                ExpiryDate = payload.Expiry,
                Plan = payload.Plan
            };

            SaveLicense();
            UpdateDisplayStrings();

            if (IsExpired)
                message = "Activation completed, but the license has expired. Please request a new serial key.";
            else
                message = "Activation completed successfully.";

            return true;
        }

        public static bool ActivateWithSerial(string serial, out string message)
            => TryActivate(serial, out message);

        public static void KillIfExpired()
        {
            if (!IsExpired)
                return;

            MessageBox.Show(
                "The license for this edition of the timetable application has expired.\n" +
                "Please request a new serial key from the developer, then restart the application.",
                "License Expired",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Application.Current.Shutdown();
        }

        private static void SaveLicense()
        {
            try
            {
                if (!Directory.Exists(LicenseDirectory))
                    Directory.CreateDirectory(LicenseDirectory);

                string json = JsonSerializer.Serialize(_license,
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(LicensePath, json, Encoding.UTF8);
                HasSavedLicense = true;
            }
            catch
            {
            }
        }
    }
}
