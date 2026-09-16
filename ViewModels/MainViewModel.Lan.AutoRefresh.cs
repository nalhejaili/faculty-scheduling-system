using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TrainerScheduler.Network;

namespace MiniTrainerScheduler.ViewModels
{
    public sealed partial class MainViewModel
    {
        private bool _isLanAutoRefreshEnabled = true;
        public bool IsLanAutoRefreshEnabled
        {
            get => _isLanAutoRefreshEnabled;
            set
            {
                if (_isLanAutoRefreshEnabled == value) return;
                _isLanAutoRefreshEnabled = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LanAutoRefreshButtonText));

                // Persist preference so the next run (and LAN settings window) reflects the user choice.
                PersistLanAutoRefreshPreference(_isLanAutoRefreshEnabled);

                if (!_isLanAutoRefreshEnabled)
                {
                    StopLanAutoRefreshTimers();
                    SetLanStatus("Automatic refresh has been turned off.");
                }
                else
                {
                    // Restart timers if possible.
                    EnsureLanAutoRefreshStarted();
                    SetLanStatus("Automatic refresh has been turned on.");
                }
            }
        }

        private static void PersistLanAutoRefreshPreference(bool enabled)
        {
            try
            {
                var s = LanSettingsStore.LoadOrDefault();
                s.AutoRefreshEnabled = enabled;
                LanSettingsStore.Save(s);
            }
            catch
            {
                // ignore (non-critical)
            }
        }

        public string LanAutoRefreshButtonText => IsLanAutoRefreshEnabled
            ? "Disable Automatic Refresh"
            : "Enable Automatic Refresh";

        public void ToggleLanAutoRefresh() => IsLanAutoRefreshEnabled = !IsLanAutoRefreshEnabled;

        private DispatcherTimer? _lanRevTimer;
        private long _lanLastRevision = -1;

        // Server-side (Admin machine): poll the local revision so the Admin UI
        // reflects changes pushed by Supervisors without requiring manual refresh.
        private DispatcherTimer? _lanServerRevTimer;
        private long _lanLastServerRevision = -1;

        private void EnsureLanAutoRefreshStarted()
        {
            if (!IsLanAutoRefreshEnabled) return;

            var settings = LanSettingsStore.LoadOrDefault();

            // Client polling: hit /rev and pull changes when revision changes.
            if (settings.Mode == LanMode.Client)
            {
                if (!LanClientContext.IsAuthenticated) return;
                if (_lanRevTimer is not null) return;

                _lanRevTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(4)
                };
                _lanRevTimer.Tick += async (_, __) => await LanAutoRefreshTickAsync();
                _lanRevTimer.Start();
                return;
            }

            // Server polling: read local SQLite revision and reload when it changes.
            // This makes Supervisor -> Admin updates visible automatically.
            if (settings.Mode == LanMode.Server)
            {
                // IMPORTANT: Do NOT depend on LanServerManager.IsRunning here.
                // The server may start asynchronously after the main window loads
                // (LanBootstrapper starts it on a background task). If we bail out
                // early, the Admin UI will never start the revision poller, and
                // Supervisor -> Admin updates will appear "stuck" until manual refresh.
                if (_lanServerRevTimer is not null) return;

                _lanServerRevTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                _lanServerRevTimer.Tick += async (_, __) => await LanServerAutoRefreshTickAsync();
                _lanServerRevTimer.Start();
            }
        }

        private async Task LanAutoRefreshTickAsync()
        {
            try
            {
                if (!IsLanAutoRefreshEnabled) return;

                var rev = await LanDataClient.TryGetRevisionAsync();
                if (rev is null) return;

                if (_lanLastRevision < 0)
                {
                    _lanLastRevision = rev.Value.revision;
                    return;
                }

                if (rev.Value.revision == _lanLastRevision)
                    return;

                _lanLastRevision = rev.Value.revision;

                // Pull latest results + blocks (scoped automatically for supervisor).
                var any = await LanDataClient.TryPullAssignmentsAndBlocksIntoStoreAsync(Store);
                if (any)
                {
                    RefreshRows();
                    SetLanStatus($"Auto refresh (Pull) — Results={Store.Assignments.Count} Blocks={Store.FacultySlotBlocks.Count}");
                }
            }
            catch (Exception ex)
            {
                // Silent: do not annoy the user on transient network issues.
                SetLanStatusErrorThrottled("Automatic refresh failed (disconnect/timeout).", ex);
            }
        }

        private async Task LanServerAutoRefreshTickAsync()
        {
            try
            {
                if (!IsLanAutoRefreshEnabled) return;

                var rev = await _sqlite.GetServerRevisionAsync();

                if (_lanLastServerRevision < 0)
                {
                    _lanLastServerRevision = rev.Revision;
                    return;
                }

                if (rev.Revision == _lanLastServerRevision)
                    return;

                _lanLastServerRevision = rev.Revision;

                // Reload from local SQLite (this will also load blocks) and refresh the Results tab.
                await TryLoadSavedResultsAsync(showMessageIfEmpty: false);
                SetLanStatus($"Server refresh (SQLite) — Results={Store.Assignments.Count} Blocks={Store.FacultySlotBlocks.Count}");
            }
            catch (Exception ex)
            {
                // Silent: admin should not be spammed with polling errors.
                SetLanStatusErrorThrottled("Unable to refresh the server automatically.", ex);
            }
        }

        private void StopLanAutoRefreshTimers()
        {
            try
            {
                if (_lanRevTimer is not null)
                {
                    _lanRevTimer.Stop();
                    _lanRevTimer = null;
                }

                if (_lanServerRevTimer is not null)
                {
                    _lanServerRevTimer.Stop();
                    _lanServerRevTimer = null;
                }
            }
            catch
            {
                // ignore
            }
        }

        public async Task RefreshLanFromServerAsync(bool showMessageIfNoChange)
        {
            var settings = LanSettingsStore.LoadOrDefault();

            if (settings.Mode == LanMode.Client && LanClientContext.IsAuthenticated)
            {
                SetLanStatus("Checking for updates...");
                var rev = await LanDataClient.TryGetRevisionAsync();
                if (rev is null)
                {
                    if (showMessageIfNoChange)
                    {
                        MessageBox.Show("Unable to read the server revision state (REV).", "LAN", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    SetLanStatusErrorThrottled("Failed to read REV (server unavailable).");
                    return;
                }

                // First time: always pull.
                if (_lanLastRevision >= 0 && rev.Value.revision == _lanLastRevision)
                {
                    if (showMessageIfNoChange)
                        MessageBox.Show("No new updates are available.", "LAN", MessageBoxButton.OK, MessageBoxImage.Information);
                    SetLanStatus("No new updates are available.");
                    return;
                }

                _lanLastRevision = rev.Value.revision;

                var any = await LanDataClient.TryPullAssignmentsAndBlocksIntoStoreAsync(Store);
                if (any)
                    RefreshRows();

                SetLanStatus($"Update completed — Results={Store.Assignments.Count} Blocks={Store.FacultySlotBlocks.Count}");

                if (showMessageIfNoChange)
                    MessageBox.Show("The data has been refreshed from the server.", "LAN", MessageBoxButton.OK, MessageBoxImage.Information);

                return;
            }

            // Server or Local mode: reload from local SQLite.
            SetLanStatus("Reloading results from SQLite...");
            await TryLoadSavedResultsAsync(showMessageIfEmpty: showMessageIfNoChange);
            SetLanStatus($"Reload completed — Results={Store.Assignments.Count} Blocks={Store.FacultySlotBlocks.Count}");
        }
    }
}
