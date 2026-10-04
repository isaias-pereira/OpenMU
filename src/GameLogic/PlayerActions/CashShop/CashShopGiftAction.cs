// <copyright file="CashShopGiftAction.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.CashShop;

using MUnique.OpenMU.GameLogic.Views.CashShop;

/// <summary>
/// Action to handle the cash shop item gift request (C1 D2 04).
/// </summary>
/// <remarks>
/// The result is reported with the same view call the buy action uses
/// (<see cref="ICashShopViewPlugIn.ShowBuyResultAsync"/>), which sends the result code on the
/// D2 03 response layout. No separate gift result response is defined in
/// <c>CashShopConnectionExtensions</c>, and inventing a sub-code without a confirmed client
/// layout would make the client misread the packet.
/// </remarks>
public class CashShopGiftAction
{
    private readonly ICashShopGiftService _giftService;

    /// <summary>
    /// Initializes a new instance of the <see cref="CashShopGiftAction"/> class.
    /// </summary>
    /// <param name="giftService">The cash shop gift service.</param>
    public CashShopGiftAction(ICashShopGiftService giftService)
    {
        this._giftService = giftService ?? throw new ArgumentNullException(nameof(giftService));
    }

    /// <summary>
    /// Handles the gift request from the client.
    /// </summary>
    /// <param name="player">The player sending the gift.</param>
    /// <param name="packageProductSeq">The package product sequence.</param>
    /// <param name="productDisplaySeq">The package category/product display sequence.</param>
    /// <param name="priceSeq">The selected price sequence.</param>
    /// <param name="itemCode">The item code sent by the client.</param>
    /// <param name="coinType">The cash type sent by the client.</param>
    /// <param name="mileageFlag">Whether the client requested mileage.</param>
    /// <param name="receiverName">The receiver name sent by the client.</param>
    /// <param name="giftText">The gift message sent by the client.</param>
    public async ValueTask HandleGiftAsync(
        Player player,
        int packageProductSeq,
        int productDisplaySeq,
        int priceSeq,
        ushort itemCode,
        int coinType,
        bool mileageFlag,
        string receiverName,
        string giftText)
    {
        if (player.SelectedCharacter is null)
        {
            return;
        }

        var result = await this._giftService.ProcessGiftAsync(
            player,
            packageProductSeq,
            productDisplaySeq,
            priceSeq,
            itemCode,
            coinType,
            mileageFlag,
            receiverName,
            giftText).ConfigureAwait(false);

        await player.InvokeViewPlugInAsync<ICashShopViewPlugIn>(
            p => p.ShowBuyResultAsync(result.Success, (int)ToClientErrorCode(result.ErrorCode), result.DeliveredItem)).ConfigureAwait(false);
    }

    /// <summary>
    /// Converte um código interno no código que o cliente entende.
    /// </summary>
    /// <param name="errorCode">The internal error code.</param>
    /// <returns>
    /// O código a ser enviado. <see cref="PurchaseErrorCode.RollbackIncomplete"/> não existe no
    /// <c>ReceiveIGS_BuyItem</c> do cliente, então é reportado como
    /// <see cref="PurchaseErrorCode.TransactionFailed"/>, que o cliente trata como erro de banco —
    /// a falha de reversão já foi registrada como erro pelo serviço, com o detalhe que o log
    /// precisa ter.
    /// </returns>
    private static PurchaseErrorCode ToClientErrorCode(PurchaseErrorCode errorCode) =>
        errorCode == PurchaseErrorCode.RollbackIncomplete ? PurchaseErrorCode.TransactionFailed : errorCode;
}
