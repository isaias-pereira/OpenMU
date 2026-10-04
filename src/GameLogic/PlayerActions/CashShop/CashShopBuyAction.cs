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
    private readonly ICashShopBuyService _buyService;

    /// <summary>
    /// Initializes a new instance of the <see cref="CashShopBuyAction"/> class.
    /// </summary>
    /// <param name="buyService">The cash shop buy service.</param>
    public CashShopBuyAction(ICashShopBuyService buyService)
    {
        this._buyService = buyService ?? throw new ArgumentNullException(nameof(buyService));
    }

    /// <summary>
    /// Handles the buy request from the client.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="packageProductSeq">The package product sequence.</param>
    /// <param name="productDisplaySeq">The package category/product display sequence.</param>
    /// <param name="priceSeq">The selected price sequence.</param>
    /// <param name="itemCode">The item code sent by the client.</param>
    /// <param name="coinType">The cash type sent by the client.</param>
    /// <param name="mileageFlag">Whether the client requested mileage.</param>
    public async ValueTask HandleBuyAsync(
        Player player,
        int packageProductSeq,
        int productDisplaySeq,
        int priceSeq,
        ushort itemCode,
        int coinType,
        bool mileageFlag)
    {
        if (player.SelectedCharacter is null)
        {
            return;
        }

        var result = await this._buyService.ProcessPurchaseAsync(
            player,
            packageProductSeq,
            productDisplaySeq,
            priceSeq,
            itemCode,
            coinType,
            mileageFlag).ConfigureAwait(false);

        await player.InvokeViewPlugInAsync<ICashShopViewPlugIn>(
            p => p.ShowBuyResultAsync(result.Success, (int)result.ErrorCode, result.DeliveredItem)).ConfigureAwait(false);
    }
}
