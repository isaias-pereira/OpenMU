// <copyright file="GensRewardRequestHandlerPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.MessageHandler.Gens;

using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic;

using MUnique.OpenMU.GameLogic.PlayerActions.Gens;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Handler for gens reward request packets (0xF8, sub-code 0x09).
/// </summary>
[PlugIn]
[Display(Name = "Gens Reward Handler", Description = "Exchanges the gens contribution points for zen.")]
[Guid("1E031FDF-D183-4A52-A232-A2F60B5D2321")]
[BelongsToGroup(GensGroupHandlerPlugIn.GroupKey)]
internal class GensRewardRequestHandlerPlugIn : ISubPacketHandlerPlugIn
{
    private readonly GensRewardAction _action = new();

    /// <inheritdoc/>
    public bool IsEncryptionExpected => false;

    /// <inheritdoc/>
    public byte Key => 0x09;

    /// <inheritdoc/>
    public async ValueTask HandlePacketAsync(Player player, Memory<byte> packet)
    {
        await this._action.RewardAsync(player).ConfigureAwait(false);
    }
}