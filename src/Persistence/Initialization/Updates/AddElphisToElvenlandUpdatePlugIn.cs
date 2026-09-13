// <copyright file="AddElphisToElvenlandUpdatePlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.Updates;

using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.Persistence.Initialization.VersionSeasonSix.Maps;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Adds a duplicate of the Elphis (refinery) NPC to Elvenland, so players can
/// access the refinery without going to the Kanturu Event map. Position: 42/246.
/// </summary>
[PlugIn]
[Display(Name = PlugInName, Description = PlugInDescription)]
[Guid("E4D2B1A0-7C3F-4A5B-9E1D-2F6A8C4B0E51")]
public class AddElphisToElvenlandUpdatePlugIn : UpdatePlugInBase
{
    /// <summary>
    /// The plug in name.
    /// </summary>
    internal const string PlugInName = "Add Elphis to Elvenland";

    /// <summary>
    /// The plug in description.
    /// </summary>
    internal const string PlugInDescription = "This adds a duplicate spawn of the Elphis NPC (Monster 368, refinery) to Elvenland at 42/246. The original spawn on the Kanturu Event map is kept.";

    /// <summary>
    /// The number of the Elphis NPC.
    /// </summary>
    private const short ElphisNumber = 368;

    /// <inheritdoc />
    public override UpdateVersion Version => UpdateVersion.AddElphisToElvenland;

    /// <inheritdoc />
    public override string DataInitializationKey => VersionSeasonSix.DataInitialization.Id;

    /// <inheritdoc />
    public override string Name => PlugInName;

    /// <inheritdoc />
    public override string Description => PlugInDescription;

    /// <inheritdoc />
    public override bool IsMandatory => true;

    /// <inheritdoc />
    public override DateTime CreatedAt => new(2026, 09, 12, 0, 0, 0, DateTimeKind.Utc);

    /// <inheritdoc />
#pragma warning disable CS1998
    protected override async ValueTask ApplyAsync(IContext context, GameConfiguration gameConfiguration)
#pragma warning restore CS1998
    {
        var elvenland = gameConfiguration.Maps.FirstOrDefault(m => m.Number == Elvenland.Number);
        if (elvenland is null)
        {
            return;
        }

        var elphis = gameConfiguration.Monsters.FirstOrDefault(m => m.Number == ElphisNumber);
        if (elphis is null)
        {
            return;
        }

        // Don't add twice.
        if (elvenland.MonsterSpawns.Any(s => s.MonsterDefinition?.Number == ElphisNumber))
        {
            return;
        }

        var spawn = context.CreateNew<MonsterSpawnArea>();
        spawn.SetGuid(Elvenland.Number, 16);
        spawn.GameMap = elvenland;
        spawn.MonsterDefinition = elphis;
        spawn.Quantity = 1;
        spawn.Direction = Direction.SouthWest;
        spawn.SpawnTrigger = SpawnTrigger.Automatic;
        spawn.X1 = 42;
        spawn.X2 = 42;
        spawn.Y1 = 246;
        spawn.Y2 = 246;
        elvenland.MonsterSpawns.Add(spawn);
    }
}
