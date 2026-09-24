// <copyright file="ICashShopPurchaseService.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.CashShop;

using System;
using System.Threading;

/// <summary>
/// Service interface for processing cash shop purchases.
/// Handles validation, wallet transactions, and item delivery.
/// </summary>
public interface ICashShopPurchaseService
{
    /// <summary>
    /// Processes a purchase request from a player.
    /// </summary>
    /// <param name="player">The player making the purchase.</param>
    /// <param name="packageProductSeq">The package product sequence (from CashShopPackage).</param>
    /// <param name="priceSeq">The price sequence (from CashShopProduct).</param>
    /// <param name="coinType">The coin type to use (1=WCoinC, 2=WCoinP, 3=Goblin).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result indicating success or failure reason.</returns>
    ValueTask<PurchaseResult> ProcessPurchaseAsync(
        Player player,
        int packageProductSeq,
        int priceSeq,
        int coinType,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a cash shop purchase attempt.
/// </summary>
public record PurchaseResult
{
    /// <summary>
    /// Gets a value indicating whether the purchase succeeded.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Gets the error code if purchase failed.
    /// </summary>
    public PurchaseErrorCode ErrorCode { get; init; }

    /// <summary>
    /// Gets the error message for logging/debugging.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets the item that was delivered (if any).
    /// </summary>
    public string? DeliveredItem { get; init; }

    /// <summary>
    /// Creates a successful purchase result.
    /// </summary>
    public static PurchaseResult Successful(string deliveredItem) => new()
    {
        Success = true,
        DeliveredItem = deliveredItem,
    };

    /// <summary>
    /// Creates a failed purchase result.
    /// </summary>
    public static PurchaseResult Failed(PurchaseErrorCode errorCode, string errorMessage) => new()
    {
        Success = false,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage,
    };
}

/// <summary>
/// Error codes for cash shop purchase failures.
/// </summary>
public enum PurchaseErrorCode
{
    /// <summary>
    /// Purchase succeeded (not an error).
    /// </summary>
    None = 0,

    /// <summary>
    /// Player does not have enough coins.
    /// </summary>
    InsufficientBalance = 1,

    /// <summary>
    /// Package not found in catalog.
    /// </summary>
    PackageNotFound = 2,

    /// <summary>
    /// Product not found in catalog.
    /// </summary>
    ProductNotFound = 3,

    /// <summary>
    /// Package is not currently available (expired, not started, or deleted).
    /// </summary>
    PackageUnavailable = 4,

    /// <summary>
    /// Player inventory is full.
    /// </summary>
    InventoryFull = 5,

    /// <summary>
    /// Database transaction failed.
    /// </summary>
    TransactionFailed = 6,

    /// <summary>
    /// Unknown error occurred.
    /// </summary>
    Unknown = 99,
}
