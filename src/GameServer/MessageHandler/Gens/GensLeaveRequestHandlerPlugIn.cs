// <copyright file="GensLeaveRequestHandlerPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.MessageHandler.Gens;

using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic;

using MUnique.OpenMU.GameLogic.PlayerActions.Gens;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Handler for gens leave request packets (0xF8, sub-code 0x03).
/// </summary>
[PlugIn]
[Display(Name = "Gens Leave Handler", Description = "Lets the player leave the current gens.")]
[Guid("681D7ADD-F9C4-485D-AF70-6C0FECCBDF5C")]
[BelongsToGroup(GensGroupHandlerPlugIn.GroupKey)]
internal class GensLeaveRequestHandlerPlugIn : ISubPacketHandlerPlugIn
{
    private readonly GensLeaveAction _action = new();

    /// <inheritdoc/>
    public bool IsEncryptionExpected => false;

    /// <inheritdoc/>
    public byte Key => 0x03;

    /// <inheritdoc/>
    public async ValueTask HandlePacketAsync(Player player, Memory<byte> packet)
    {
        await this._action.LeaveAsync(player).ConfigureAwait(false);
    }
}