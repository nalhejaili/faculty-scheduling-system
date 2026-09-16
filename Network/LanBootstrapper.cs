using System;
using System.Threading.Tasks;
using System.Windows;
using TrainerScheduler.Security;
using TrainerScheduler.Services;

namespace TrainerScheduler.Network;

public static class LanBootstrapper
{
    /// <summary>
    /// Start LAN server automatically for Admin (if configured). Non-blocking.
    /// </summary>
    public static void TryAutoStartForCurrentUser(Window? owner = null)
    {
        try
        {
            var settings = LanSettingsStore.LoadOrDefault();
            if (settings.Mode != LanMode.Server)
                return;

            if (!AuthContext.IsAdmin)
                return;

            if (!settings.AutoStartServerForAdmin)
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    await LanServerManager.StartAsync(settings.Port);
                }
                catch (Exception ex)
                {
                    // Need to marshal back to UI thread.
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        UiError.Show("LAN", "Unable to start the LAN server.", ex);
                    });
                }
            });
        }
        catch (Exception ex)
        {
            if (owner is not null)
                UiError.Show("LAN", "Unable to initialize LAN settings.", ex);
        }
    }
}
