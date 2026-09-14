// <copyright file="CashShopStorageListRequestHandlerPlugIn.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.MessageHandler.CashShop;

using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlayerActions.CashShop;
using MUnique.OpenMU.Network.Packets.ClientToServer;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Handler for the cash shop storage list request (C1 D2 05). The client requests a page of the
/// storage when the shop dialog was opened or when the player uses the storage paging.
/// </summary>
/// <remarks>
/// The cash shop storage itself is not implemented yet (FASE 9), so this always answers an empty
/// storage (D2 06 with zero items and pages) to keep the client UI from freezing.
/// </remarks>
[PlugIn]
[Display(Name = "Cash Shop Storage List Handler", Description = "Answers the cash shop storage list request (D2 05).")]
[Guid("3A4A57D7-1644-4A10-89D6-4E3EBF06C77B")]
[BelongsToGroup(CashShopGroupHandlerPlugIn.GroupKey)]
internal class CashShopStorageListRequestHandlerPlugIn : ISubPacketHandlerPlugIn
{
    private readonly CashShopStorageListAction _action = new();

    /// <inheritdoc/>
    public bool IsEncryptionExpected => false;

    /// <inheritdoc/>
    public byte Key => CashShopStorageListRequest.SubCode;

    /// <inheritdoc/>
    public async ValueTask HandlePacketAsync(Player player, Memory<byte> packet)
    {
        if (packet.Length < CashShopStorageListRequest.Length)
        {
            return;
        }

        CashShopStorageListRequest request = packet;
        await this._action.RequestListAsync(player, request.PageIndex, request.InventoryType).ConfigureAwait(false);
    }
}
