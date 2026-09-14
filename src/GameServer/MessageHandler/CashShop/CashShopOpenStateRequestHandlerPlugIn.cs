// <copyright file="CashShopOpenStateRequestHandlerPlugIn.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.MessageHandler.CashShop;

using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlayerActions.CashShop;
using MUnique.OpenMU.Network.Packets.ClientToServer;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Handler for the cash shop open state request (C1 D2 02). The client tells the server that it
/// wants to open (IsClosed = false) or close (IsClosed = true) the shop dialog. In case of
/// opening, the server answers with the open result (D2 02); when that is a success the client
/// requests the point info (D2 01) and the storage list (D2 05) automatically.
/// </summary>
[PlugIn]
[Display(Name = "Cash Shop Open Handler", Description = "Answers the cash shop open/close request (D2 02).")]
[Guid("16E7B960-E617-49AB-963D-6C33CA29CD9A")]
[BelongsToGroup(CashShopGroupHandlerPlugIn.GroupKey)]
internal class CashShopOpenStateRequestHandlerPlugIn : ISubPacketHandlerPlugIn
{
    private readonly CashShopOpenStateAction _action = new();

    /// <inheritdoc/>
    public bool IsEncryptionExpected => false;

    /// <inheritdoc/>
    public byte Key => CashShopOpenState.SubCode;

    /// <inheritdoc/>
    public async ValueTask HandlePacketAsync(Player player, Memory<byte> packet)
    {
        if (packet.Length < CashShopOpenState.Length)
        {
            return;
        }

        CashShopOpenState request = packet;
        await this._action.HandleOpenStateAsync(player, request.IsClosed).ConfigureAwait(false);
    }
}
