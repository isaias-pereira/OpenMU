// <copyright file="CashShopEventItemListRequestHandlerPlugIn.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.MessageHandler.CashShop;

using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlayerActions.CashShop;
using MUnique.OpenMU.Network.Packets.ClientToServer;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Handler for the cash shop event item list request (C1 D2 13). The client requests the number
/// of event items of a category, and afterwards the item list itself (D2 14).
/// Integrado com CashShopCatalogService (Fase 6) para retornar pacotes ativos.
/// </summary>
[PlugIn]
[Display(Name = "Cash Shop Event List Handler", Description = "Answers the cash shop event item list request (D2 13).")]
[Guid("B00C92E4-54F4-4D8E-BFDA-5F5E295E26C0")]
[BelongsToGroup(CashShopGroupHandlerPlugIn.GroupKey)]
internal class CashShopEventItemListRequestHandlerPlugIn : ISubPacketHandlerPlugIn
{
    private readonly CashShopEventItemListAction _action;

    /// <summary>
    /// Initializes a new instance of the <see cref="CashShopEventItemListRequestHandlerPlugIn"/> class.
    /// </summary>
    /// <param name="action">The cash shop event item list action.</param>
    public CashShopEventItemListRequestHandlerPlugIn(CashShopEventItemListAction action)
    {
        this._action = action ?? throw new ArgumentNullException(nameof(action));
    }

    /// <inheritdoc/>
    public bool IsEncryptionExpected => false;

    /// <inheritdoc/>
    public byte Key => CashShopEventItemListRequest.SubCode;

    /// <inheritdoc/>
    public async ValueTask HandlePacketAsync(Player player, Memory<byte> packet)
    {
        if (packet.Length < CashShopEventItemListRequest.Length)
        {
            return;
        }

        CashShopEventItemListRequest request = packet;
        await this._action.RequestListAsync(player, request.CategoryIndex).ConfigureAwait(false);
    }
}
