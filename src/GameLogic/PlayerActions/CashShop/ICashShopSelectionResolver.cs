// <copyright file="ICashShopSelectionResolver.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.CashShop;

using System.Threading;
using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.GameLogic.CashShop;

/// <summary>
/// Resolves a selection of the cash shop (package, price and item code, as sent by the client)
/// into the logical catalog selection it refers to, or into the reason why it cannot be resolved.
/// </summary>
/// <remarks>
/// This is shared by the buy (D2 03) and the gift (D2 04) flows because both requests carry the
/// same six selection fields and must be validated against exactly the same catalog rules —
/// charging a different amount than the client asked for, or delivering a different item, is the
/// failure mode this prevents.
/// </remarks>
public interface ICashShopSelectionResolver
{
    /// <summary>
    /// Resolves the client selection into a purchasable product.
    /// </summary>
    /// <param name="player">The player, used for the game configuration of the item lookup.</param>
    /// <param name="packageProductSeq">The package product sequence.</param>
    /// <param name="productDisplaySeq">The package category/product display sequence.</param>
    /// <param name="priceSeq">The selected price sequence, or zero for a single-price package.</param>
    /// <param name="itemCode">The item code sent by the client.</param>
    /// <param name="coinType">The cash type sent by the client.</param>
    /// <param name="mileageFlag">Whether the client requested mileage.</param>
    /// <param name="requireGiftable">
    /// When <c>true</c>, the package must also be flagged as giftable in the catalog, otherwise
    /// the selection is refused. The buy flow passes <c>false</c>: presentability restricts the
    /// gift flow only, never a purchase.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The resolved selection, or the failure reason.</returns>
    ValueTask<CashShopSelectionResult> ResolveAsync(
        Player player,
        int packageProductSeq,
        int productDisplaySeq,
        int priceSeq,
        ushort itemCode,
        int coinType,
        bool mileageFlag,
        bool requireGiftable,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The outcome of resolving a cash shop selection.
/// </summary>
public sealed record CashShopSelectionResult
{
    /// <summary>
    /// Gets a value indicating whether the selection resolved to a purchasable product.
    /// </summary>
    public bool Success => this.ErrorCode == PurchaseErrorCode.None;

    /// <summary>
    /// Gets the product sequence shared by all rows for this logical selection.
    /// </summary>
    public int ProductSequence { get; init; }

    /// <summary>Gets the storage group shared by all rows for this logical selection.</summary>
    public int StorageGroup { get; init; }

    /// <summary>Gets the catalog name shared by all rows for this logical selection.</summary>
    public string? ProductName { get; init; }

    /// <summary>Gets the price shared by all rows for this logical selection.</summary>
    public int Price { get; init; }

    /// <summary>
    /// Gets the price sequence that was actually selected, which can differ from
    /// <c>priceSeq</c> when the client sent zero for a single-price package.
    /// </summary>
    public int SelectedPriceSequence { get; init; }

    /// <summary>
    /// Gets the item definition matching the product's in-game item code.
    /// </summary>
    public ItemDefinition? ItemDefinition { get; init; }

    /// <summary>
    /// Gets the duration in seconds aggregated from the offer's duration property rows, or zero
    /// when the offer is permanent.
    /// </summary>
    /// <remarks>
    /// Resolved and preserved, but <b>not applied</b> to the delivered item yet: OpenMU has no
    /// item expiry infrastructure, so there is nowhere to put it. Kept so the value is not lost.
    /// </remarks>
    public int DurationSeconds { get; init; }

    /// <summary>
    /// Gets the quantity aggregated from the offer's quantity property rows; one when absent.
    /// </summary>
    public int Quantity { get; init; }

    /// <summary>
    /// Gets the reason the selection could not be resolved; <see cref="PurchaseErrorCode.None"/>
    /// when it succeeded.
    /// </summary>
    public PurchaseErrorCode ErrorCode { get; init; }

    /// <summary>
    /// Gets the message describing the failure.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Creates a successful resolution without choosing any one property-variant row.
    /// </summary>
    /// <param name="productSequence">The shared product sequence.</param>
    /// <param name="storageGroup">The shared storage group.</param>
    /// <param name="productName">The shared product name.</param>
    /// <param name="price">The shared price.</param>
    /// <param name="selectedPriceSequence">The price sequence that was selected.</param>
    /// <param name="itemDefinition">The item definition of the product.</param>
    public static CashShopSelectionResult Resolved(int productSequence, int storageGroup, string productName, int price, int selectedPriceSequence, ItemDefinition itemDefinition, int durationSeconds = 0, int quantity = 1) =>
        new()
        {
            ProductSequence = productSequence,
            StorageGroup = storageGroup,
            ProductName = productName,
            Price = price,
            SelectedPriceSequence = selectedPriceSequence,
            ItemDefinition = itemDefinition,
            DurationSeconds = durationSeconds,
            Quantity = quantity,
        };

    /// <summary>
    /// Creates a failed resolution.
    /// </summary>
    /// <param name="errorCode">The reason the selection was refused.</param>
    /// <param name="errorMessage">The message describing the failure.</param>
    public static CashShopSelectionResult Failed(PurchaseErrorCode errorCode, string errorMessage) =>
        new() { ErrorCode = errorCode, ErrorMessage = errorMessage };
}

