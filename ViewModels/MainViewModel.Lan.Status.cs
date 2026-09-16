using System;
using System.Globalization;
using TrainerScheduler.Network;
using TrainerScheduler.Security;
using TrainerScheduler.Services;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        private string _lanStatusText = "";
        public string LanStatusText
        {
            get => _lanStatusText;
            private set
            {
                if (string.Equals(_lanStatusText, value, StringComparison.Ordinal)) return;
                _lanStatusText = value;
                OnPropertyChanged();
            }
        }

        private DateTime _lanLastErrorUiUtc = DateTime.MinValue;

        private string GetLanModeLabel(LanMode mode) => mode switch
        {
            LanMode.Client => "Client",
            LanMode.Server => "Server",
            _ => "Local"
        };

        private string GetRoleLabel() => AuthContext.IsAdmin ? "Administrator" : (AuthContext.IsSupervisor ? "Supervisor" : "User");

        private string GetCurrentRevisionText(LanMode mode)
        {
            if (mode == LanMode.Client)
                return _lanLastRevision >= 0 ? _lanLastRevision.ToString(CultureInfo.InvariantCulture) : "-";

            if (mode == LanMode.Server)
                return _lanLastServerRevision >= 0 ? _lanLastServerRevision.ToString(CultureInfo.InvariantCulture) : "-";

            return "-";
        }

        private void SetLanStatus(string details, bool isError = false, Exception? ex = null)
        {
            try
            {
                var settings = LanClientContext.Settings ?? LanSettingsStore.LoadOrDefault();
                var modeLabel = GetLanModeLabel(settings.Mode);
                var roleLabel = GetRoleLabel();
                var revText = GetCurrentRevisionText(settings.Mode);
                var time = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

                LanStatusText = $"LAN: {modeLabel} | {roleLabel} | Rev: {revText} | {time} — {details}";

                if (isError)
                    LanLog.Warn($"{modeLabel}/{roleLabel} | {details}");
                else
                    LanLog.Info($"{modeLabel}/{roleLabel} | {details}");

                if (ex is not null)
                    LanLog.Error($"{modeLabel}/{roleLabel} | {details}", ex);
            }
            catch
            {
                // ignore
            }
        }

        private void SetLanStatusErrorThrottled(string details, Exception? ex = null)
        {
            // Don't update UI on every transient failure during polling.
            var now = DateTime.UtcNow;
            if ((now - _lanLastErrorUiUtc).TotalSeconds < 10)
                return;

            _lanLastErrorUiUtc = now;
            SetLanStatus(details, isError: true, ex: ex);
        }

        private void InitLanStatusOnStartup()
        {
            try
            {
                var settings = LanClientContext.Settings ?? LanSettingsStore.LoadOrDefault();
                var modeLabel = GetLanModeLabel(settings.Mode);
                var roleLabel = GetRoleLabel();

                if (settings.Mode == LanMode.Client)
                {
                    if (LanClientContext.IsAuthenticated)
                        SetLanStatus("Connected — Ready");
                    else
                        SetLanStatus("Not signed in (LAN)");
                }
                else if (settings.Mode == LanMode.Server)
                {
                    SetLanStatus("Server (Local) — Ready");
                }
                else
                {
                    LanStatusText = $"LAN: {modeLabel} | {roleLabel} — Ready";
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}
