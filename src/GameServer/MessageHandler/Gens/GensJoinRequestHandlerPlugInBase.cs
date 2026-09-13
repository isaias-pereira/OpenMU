// <copyright file="GensJoinRequestHandlerPlugInBase.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.MessageHandler.Gens;

using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;

using MUnique.OpenMU.GameLogic.PlayerActions.Gens;

/// <summary>
/// Base class for the gens join request packet handlers.
/// </summary>
/// <remarks>
/// Season 6 clients send the join request in two variants: either with the sub-code
/// itself carrying the gens type (0x01 = Duprian, 0x02 = Vanert), or with an explicit
/// type byte at index 4. Both variants are handled here.
/// </remarks>
internal abstract class GensJoinRequestHandlerPlugInBase : ISubPacketHandlerPlugIn
{
    private readonly GensJoinAction _action = new();

    /// <summary>
    /// Gets the sub-packet key (0x01 for Duprian, 0x02 for Vanert).
    /// </summary>
    public abstract byte Key { get; }

    /// <summary>
    /// Gets the gens type which is assumed if the packet does not carry an explicit type byte.
    /// </summary>
    protected abstract GensType DefaultGensType { get; }

    /// <inheritdoc/>
    public bool IsEncryptionExpected => false;

    /// <inheritdoc/>
    public async ValueTask HandlePacketAsync(Player player, Memory<byte> packet)
    {
        if (player.SelectedCharacter is null || packet.Length < 4)
        {
            return;
        }

        var gensType = this.GetGensType(packet);
        if (gensType == GensType.Undefined)
        {
            return;
        }

        await this._action.JoinAsync(player, gensType).ConfigureAwait(false);
    }

    private GensType GetGensType(Memory<byte> packet)
    {
        if (packet.Length > 4)
        {
            var explicitType = (GensType)packet.Span[4];
            if (explicitType == GensType.Duprian || explicitType == GensType.Vanert)
            {
                return explicitType;
            }
        }

        return this.DefaultGensType;
    }
}