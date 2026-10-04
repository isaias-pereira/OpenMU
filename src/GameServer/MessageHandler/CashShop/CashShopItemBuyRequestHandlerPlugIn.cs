// <copyright file="CashShopItemBuyRequestHandlerPlugIn.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.MessageHandler.CashShop;

using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlayerActions.CashShop;
using MUnique.OpenMU.GameLogic.Views.CashShop;
using MUnique.OpenMU.Network.Packets.ClientToServer;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Handles the cash shop item purchase request (C1 D2 03).
/// </summary>
[PlugIn]
[Display(Name = "Cash Shop Item Buy Handler", Description = "Handles a cash shop item purchase request (D2 03).")]
[Guid("4A6B3B4D-3B0D-4C1E-9C1E-7B2D7D3B5A11")]
[BelongsToGroup(CashShopGroupHandlerPlugIn.GroupKey)]
internal class CashShopItemBuyRequestHandlerPlugIn : ISubPacketHandlerPlugIn
{
    private readonly CashShopBuyAction _action;

    /// <summary>
    /// Initializes a new instance of the <see cref="CashShopItemBuyRequestHandlerPlugIn"/> class.
    /// </summary>
    /// <param name="action">The cash shop buy action.</param>
    public CashShopItemBuyRequestHandlerPlugIn(CashShopBuyAction action)
    {
        this._action = action ?? throw new ArgumentNullException(nameof(action));
    }

    /// <inheritdoc />
    public bool IsEncryptionExpected => false;

    /// <inheritdoc />
    public byte Key => CashShopItemBuyRequest.SubCode;

    /// <inheritdoc />
    public async ValueTask HandlePacketAsync(Player player, Memory<byte> packet)
    {
        if (packet.Length < CashShopItemBuyRequest.Length)
        {
            return;
        }

        CashShopItemBuyRequest request = packet;
        if (request.PackageMainIndex > int.MaxValue ||
            request.Category > int.MaxValue ||
            request.ProductMainIndex > int.MaxValue ||
            request.CoinIndex > int.MaxValue)
        {
            await player.InvokeViewPlugInAsync<ICashShopViewPlugIn>(
                p => p.ShowBuyResultAsync(false, (int)PurchaseErrorCode.SelectionUnavailable, null)).ConfigureAwait(false);
            return;
        }

        await this._action.HandleBuyAsync(
            player,
            (int)request.PackageMainIndex,
            (int)request.Category,
            (int)request.ProductMainIndex,
            request.ItemIndex,
            (int)request.CoinIndex,
            request.MileageFlag != 0).ConfigureAwait(false);
    }
}
