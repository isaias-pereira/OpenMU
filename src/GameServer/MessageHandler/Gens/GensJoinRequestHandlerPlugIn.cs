// <copyright file="GensJoinRequestHandlerPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.MessageHandler.Gens;

using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Handler for the gens join request packets which are sent with the sub-code 0x01 (Duprian).
/// </summary>
[PlugIn]
[Display(Name = "Gens Join Handler", Description = "Joins the player to the Duprian gens.")]
[Guid("0B61786B-B12C-4F8C-ABA5-B0D4F5CABCAE")]
[BelongsToGroup(GensGroupHandlerPlugIn.GroupKey)]
internal class GensJoinRequestHandlerPlugIn : GensJoinRequestHandlerPlugInBase
{
    /// <inheritdoc/>
    public override byte Key => 0x01;

    /// <inheritdoc/>
    protected override GensType DefaultGensType => GensType.Duprian;
}