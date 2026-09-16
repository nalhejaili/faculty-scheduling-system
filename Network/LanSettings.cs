using System;

namespace TrainerScheduler.Network;

public sealed class LanSettings
{
    public LanMode Mode { get; set; } = LanMode.LocalOnly;

    /// <summary>
    /// The Admin machine address for clients (e.g., 192.0.2.10).
    /// You can also enter a hostname.
    /// </summary>
    public string ServerHost { get; set; } = "127.0.0.1";

    /// <summary>
    /// TCP port for the LAN server.
    /// </summary>
    public int Port { get; set; } = 5178;

    /// <summary>
    /// If enabled, the Admin account will auto-start the LAN server after login.
    /// </summary>
    public bool AutoStartServerForAdmin { get; set; } = true;

    /// <summary>
    /// If enabled, the app will poll server revision and auto-refresh results on change.
    /// </summary>
    public bool AutoRefreshEnabled { get; set; } = true;

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}
