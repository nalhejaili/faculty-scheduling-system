using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace TrainerScheduler.Services
{
    public static partial class LicenseService
    {
        private const string ProductName = "TrainerScheduler";
        private static readonly string SecretKey =
            Environment.GetEnvironmentVariable("TRAINER_SCHEDULER_LICENSE_SECRET")
            ?? "PUBLIC-DEMO-ONLY";

        private static readonly string LicenseDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                ProductName);

        private static readonly string LicensePath =
            Path.Combine(LicenseDirectory, "license.json");

        private static LicenseInfo _license = new LicenseInfo();

        public static bool HasSavedLicense { get; private set; }

        public static string Serial => _license.Serial ?? "FREE";

        public static string PlanKind =>
            string.IsNullOrWhiteSpace(_license.Plan) ? "FREE" : _license.Plan;

        public static bool IsExpired =>
            _license.ExpiryDate.HasValue &&
            _license.ExpiryDate.Value.Date < DateTime.Today;

        public static string MachineId => GetMachineId();

        public static string ActivationText { get; private set; } =
            "Activated — Free Edition";

        public static string PlanDisplay { get; private set; } =
            "Free Edition";

        public static string ExpiryDisplay { get; private set; } =
            "No Expiry";

        public static string RemainingDaysDisplay { get; private set; } =
            "∞";

        public static string DaysLeftDisplay => RemainingDaysDisplay;

        public static string MachineIdDisplay => MachineId;

        static LicenseService()
        {
            ResetToFree();
            LoadAndValidate();
        }

        private static void ResetToFree()
        {
            _license = new LicenseInfo
            {
                Serial = "FREE",
                MachineId = MachineId,
                ExpiryDate = null,
                Plan = "FREE"
            };

            HasSavedLicense = false;
            UpdateDisplayStrings();
        }

        private static void UpdateDisplayStrings()
        {
            if (PlanKind == "FREE")
            {
                ActivationText = "Activated — Free Edition";
                PlanDisplay = "Free Edition";
                ExpiryDisplay = "No Expiry";
                RemainingDaysDisplay = "∞";
                return;
            }

            PlanDisplay = PlanKind;

            if (_license.ExpiryDate.HasValue)
            {
                DateTime exp = _license.ExpiryDate.Value.Date;
                ExpiryDisplay = exp.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

                int days = (exp - DateTime.Today).Days;
                if (days < 0)
                {
                    RemainingDaysDisplay = "Expired";
                    ActivationText = "Expired — Renewal Required";
                }
                else
                {
                    RemainingDaysDisplay = days.ToString(CultureInfo.InvariantCulture);
                    ActivationText = "Activated";
                }
            }
            else
            {
                ExpiryDisplay = "No Expiry";
                RemainingDaysDisplay = "∞";
                ActivationText = "Activated";
            }
        }

        private sealed class LicenseInfo
        {
            public string Serial { get; set; } = "FREE";
            public string MachineId { get; set; } = string.Empty;
            public DateTime? ExpiryDate { get; set; }
            public string Plan { get; set; } = "FREE";
        }

        private sealed class LicensePayload
        {
            public string MachineId { get; }
            public DateTime Expiry { get; }
            public string Plan { get; }

            public LicensePayload(string machineId, DateTime expiry, string plan)
            {
                MachineId = machineId;
                Expiry = expiry;
                Plan = plan;
            }
        }
    }
}
