// <copyright file="CashShopBuyAction.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.CashShop;

using MUnique.OpenMU.GameLogic.Views.CashShop;

/// <summary>
/// Action to handle the cash shop item buy request (C1 D2 03).
/// </summary>
public class CashShopBuyAction
{
    private readonly ICashShopPurchaseService _purchaseService;

    /// <summary>
    /// Initializes a new instance of the <see cref="CashShopBuyAction"/> class.
    /// </summary>
    /// <param name="purchaseService">The purchase service.</param>
    public CashShopBuyAction(ICashShopPurchaseService purchaseService)
    {
        this._purchaseService = purchaseService ?? throw new ArgumentNullException(nameof(purchaseService));
    }

    /// <summary>
    /// Handles the buy request from the client.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="packageProductSeq">The package product sequence.</param>
    /// <param name="priceSeq">The price sequence of the selected product variant.</param>
    /// <param name="coinType">The coin type (1=WCoinC, 2=WCoinP, 3=Goblin).</param>
    /// <param name="mileageFlag">Whether to use mileage.</param>
    public async ValueTask HandleBuyAsync(
        Player player,
        int packageProductSeq,
        int priceSeq,
        int coinType,
        bool mileageFlag)
    {
        if (player.SelectedCharacter is null)
        {
            return;
        }

        // CoinIndex 1=WCoinC, 2=WCoinP, 3=GoblinCoin
        // MileageFlag: 0=don't use mileage, 1=use mileage
        // For now, mileage is not implemented, so we just use the coin type as-is
        var result = await this._purchaseService.ProcessPurchaseAsync(
            player,
            packageProductSeq,
            priceSeq,
            coinType).ConfigureAwait(false);

        await player.InvokeViewPlugInAsync<ICashShopViewPlugIn>(
            p => p.ShowBuyResultAsync(result.Success, (int)result.ErrorCode, result.DeliveredItem)).ConfigureAwait(false);
    }
}
