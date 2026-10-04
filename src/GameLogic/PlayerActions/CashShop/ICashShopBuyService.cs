// <copyright file="ICashShopBuyService.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.CashShop;

using System.Threading;

/// <summary>
/// Processes the validated D2 03 cash shop purchase request.
/// </summary>
public interface ICashShopBuyService
{
    /// <summary>
    /// Processes a purchase request from a player.
    /// </summary>
    /// <param name="player">The player making the purchase.</param>
    /// <param name="packageProductSeq">The package product sequence.</param>
    /// <param name="productDisplaySeq">The package category/product display sequence.</param>
    /// <param name="priceSeq">The selected price sequence, or zero for a single-price package.</param>
    /// <param name="itemCode">The item code sent by the client; it is validated against the server catalog.</param>
    /// <param name="coinType">The cash type sent by the client; it is validated against the package.</param>
    /// <param name="mileageFlag">Whether the client requested mileage.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the purchase attempt.</returns>
    ValueTask<PurchaseResult> ProcessPurchaseAsync(
        Player player,
        int packageProductSeq,
        int productDisplaySeq,
        int priceSeq,
        ushort itemCode,
        int coinType,
        bool mileageFlag,
        CancellationToken cancellationToken = default);
}
