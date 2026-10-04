// <copyright file="CashShopItemGiftRequestHandlerPlugIn.cs" company="MUnique">
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
/// Handles the cash shop item gift request (C1 D2 04).
/// </summary>
/// <remarks>
/// The request is 234 bytes: the six selection fields of the buy request (D2 03), then
/// <c>GiftReceiverName</c> (11 bytes at offset 23) and <c>GiftText</c> (200 bytes at offset 34).
/// A shorter packet is dropped without an answer, because there is no way to tell which field
/// would be missing.
/// </remarks>
[PlugIn]
[Display(Name = "Cash Shop Item Gift Handler", Description = "Handles a cash shop item gift request (D2 04).")]
[Guid("7C1F5A62-9D4E-4B7A-8E23-1F5C6B0A9D42")]
[BelongsToGroup(CashShopGroupHandlerPlugIn.GroupKey)]
internal class CashShopItemGiftRequestHandlerPlugIn : ISubPacketHandlerPlugIn
{
    private readonly CashShopGiftAction _action;

    /// <summary>
    /// Initializes a new instance of the <see cref="CashShopItemGiftRequestHandlerPlugIn"/> class.
    /// </summary>
    /// <param name="action">The cash shop gift action.</param>
    public CashShopItemGiftRequestHandlerPlugIn(CashShopGiftAction action)
    {
        this._action = action ?? throw new ArgumentNullException(nameof(action));
    }

    /// <inheritdoc />
    public bool IsEncryptionExpected => false;

    /// <inheritdoc />
    public byte Key => CashShopItemGiftRequest.SubCode;

    /// <inheritdoc />
    public async ValueTask HandlePacketAsync(Player player, Memory<byte> packet)
    {
        if (packet.Length < CashShopItemGiftRequest.Length)
        {
            return;
        }

        CashShopItemGiftRequest request = packet;
        if (request.PackageMainIndex > int.MaxValue ||
            request.Category > int.MaxValue ||
            request.ProductMainIndex > int.MaxValue ||
            request.CoinIndex > int.MaxValue)
        {
            await player.InvokeViewPlugInAsync<ICashShopViewPlugIn>(
                p => p.ShowBuyResultAsync(false, (int)PurchaseErrorCode.SelectionUnavailable, null)).ConfigureAwait(false);
            return;
        }

        await this._action.HandleGiftAsync(
            player,
            (int)request.PackageMainIndex,
            (int)request.Category,
            (int)request.ProductMainIndex,
            request.ItemIndex,
            (int)request.CoinIndex,
            request.MileageFlag != 0,
            request.GiftReceiverName,
            request.GiftText).ConfigureAwait(false);
    }
}
