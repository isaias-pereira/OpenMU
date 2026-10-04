// <copyright file="ICashShopGiftService.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.CashShop;

using System.Threading;

/// <summary>
/// Processes the validated D2 04 cash shop gift request.
/// </summary>
/// <remarks>
/// A gift is a purchase made by the sender but delivered to another account, so it reuses
/// <see cref="PurchaseResult"/> and <see cref="PurchaseErrorCode"/>: the client reports the
/// outcome of a gift with the same result codes it uses for a purchase.
/// </remarks>
public interface ICashShopGiftService
{
    /// <summary>
    /// Processes a gift request from a player.
    /// </summary>
    /// <param name="player">The player sending the gift; the one whose credits are charged.</param>
    /// <param name="packageProductSeq">The package product sequence.</param>
    /// <param name="productDisplaySeq">The package category/product display sequence.</param>
    /// <param name="priceSeq">The selected price sequence, or zero for a single-price package.</param>
    /// <param name="itemCode">The item code sent by the client; validated against the server catalog.</param>
    /// <param name="coinType">The cash type sent by the client; validated against the package.</param>
    /// <param name="mileageFlag">Whether the client requested mileage.</param>
    /// <param name="receiverName">
    /// The receiver name exactly as the client sent it (D2 04, 11 bytes at offset 23). The client
    /// types a <b>character</b> name, so the receiving account is the one that owns that
    /// character.
    /// </param>
    /// <param name="giftText">
    /// The gift message (D2 04, 200 bytes at offset 34).
    /// <para>
    /// <b>Currently ignored.</b> It is received and validated, and nothing more: there is no
    /// column for it and no response packet that carries it back. See the remarks of
    /// <see cref="CashShopGiftService"/> for why it is not persisted.
    /// </para>
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the gift attempt.</returns>
    ValueTask<PurchaseResult> ProcessGiftAsync(
        Player player,
        int packageProductSeq,
        int productDisplaySeq,
        int priceSeq,
        ushort itemCode,
        int coinType,
        bool mileageFlag,
        string receiverName,
        string giftText,
        CancellationToken cancellationToken = default);
}
