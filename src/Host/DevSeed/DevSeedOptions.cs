namespace Cracra.Host.DevSeed;

/// <summary>
/// Controls the development data seeder.
/// </summary>
/// <remarks>
/// Off unless something turns it on, and only ever wired up in Development (see <c>Program.cs</c>). A seeder that
/// could be switched on in production by a stray configuration key is a seeder that will eventually write two
/// invented projects into somebody's real portfolio.
/// </remarks>
public sealed class DevSeedOptions
{
    public const string SectionName = "Cracra:DevSeed";

    /// <summary>Defaults to on in Development, because a dev box with an empty board teaches nobody anything.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// How many ISO weeks of history to write, counting the current one.
    /// </summary>
    /// <remarks>
    /// Three by default: enough for the weekly boards to have a past to page back into, short enough that a clean
    /// dev box is seeded in well under a second.
    /// </remarks>
    public int HistoryWeeks { get; init; } = 3;

    /// <summary>How many ISO weeks of planned entries to write ahead of today.</summary>
    public int PlannedWeeks { get; init; } = 1;
}
