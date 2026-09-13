// <copyright file="GensJoinVanertRequestHandlerPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.MessageHandler.Gens;

using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Handler for the gens join request packets which are sent with the sub-code 0x02 (Vanert).
/// Some Season 6 clients send the gens type within the sub-code byte instead of a separate byte.
/// </summary>
[PlugIn]
[Display(Name = "Gens Join Vanert Handler", Description = "Joins the player to the Vanert gens.")]
[Guid("39DD7FF0-6C9A-486C-98AA-C90A357A31B4")]
[BelongsToGroup(GensGroupHandlerPlugIn.GroupKey)]
internal class GensJoinVanertRequestHandlerPlugIn : GensJoinRequestHandlerPlugInBase
{
    /// <inheritdoc/>
    public override byte Key => 0x02;

    /// <inheritdoc/>
    protected override GensType DefaultGensType => GensType.Vanert;
}