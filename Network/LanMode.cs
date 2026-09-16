namespace TrainerScheduler.Network;

public enum LanMode
{
    /// <summary>
    /// Traditional local mode: use the local SQLite database.
    /// </summary>
    LocalOnly = 0,

    /// <summary>
    /// Admin machine: runs a small HTTP server on LAN.
    /// </summary>
    Server = 1,

    /// <summary>
    /// Client machine: will connect to the Admin server over LAN.
    /// </summary>
    Client = 2,
}
