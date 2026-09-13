// <copyright file="GensRankingRequestHandlerPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.MessageHandler.Gens;

using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic;

using MUnique.OpenMU.GameLogic.PlayerActions.Gens;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Handler for gens ranking request packets (0xF8, sub-code 0x0B).
/// </summary>
[PlugIn]
[Display(Name = "Gens Ranking Handler", Description = "Requests the current gens ranking.")]
[Guid("FD35E0EA-5264-4A15-9682-562009A7B7CB")]
[BelongsToGroup(GensGroupHandlerPlugIn.GroupKey)]
internal class GensRankingRequestHandlerPlugIn : ISubPacketHandlerPlugIn
{
    private readonly GensRankingAction _action = new();

    /// <inheritdoc/>
    public bool IsEncryptionExpected => false;

    /// <inheritdoc/>
    public byte Key => 0x0B;

    /// <inheritdoc/>
    public async ValueTask HandlePacketAsync(Player player, Memory<byte> packet)
    {
        await this._action.ShowRankingAsync(player).ConfigureAwait(false);
    }
}