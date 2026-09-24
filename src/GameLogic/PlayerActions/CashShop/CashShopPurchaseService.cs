// <copyright file="CashShopPurchaseService.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.CashShop;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.CashShop;

/// <summary>
/// Service for processing cash shop purchases.
/// Handles validation, wallet transactions via <see cref="WCoinService"/>, and item delivery.
/// </summary>
public class CashShopPurchaseService : ICashShopPurchaseService
{
    private readonly ICashShopCatalogService _catalogService;
    private readonly ILogger<CashShopPurchaseService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CashShopPurchaseService"/> class.
    /// </summary>
    /// <param name="catalogService">The cash shop catalog service.</param>
    /// <param name="logger">The logger.</param>
    public CashShopPurchaseService(
        ICashShopCatalogService catalogService,
        ILogger<CashShopPurchaseService> logger)
    {
        this._catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));
        this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async ValueTask<PurchaseResult> ProcessPurchaseAsync(
        Player player,
        int packageProductSeq,
        int priceSeq,
        int coinType,
        CancellationToken cancellationToken = default)
    {
        if (player.SelectedCharacter is null)
        {
            return PurchaseResult.Failed(PurchaseErrorCode.Unknown, "No character selected");
        }

        var account = player.Account;
        if (account is null)
        {
            return PurchaseResult.Failed(PurchaseErrorCode.Unknown, "No account associated");
        }

        try
        {
            // Step 1: Validate package exists and is available
            var package = await this._catalogService.GetPackageBySeqAsync(packageProductSeq).ConfigureAwait(false);
            if (package is null)
            {
                this._logger.LogWarning("Package {PackageSeq} not found for player {Player}", packageProductSeq, player.Name);
                return PurchaseResult.Failed(PurchaseErrorCode.PackageNotFound, $"Package {packageProductSeq} not found");
            }

            if (!this.IsPackageAvailable(package))
            {
                this._logger.LogWarning(
                    "Package {PackageSeq} is unavailable (DeleteFlag={DeleteFlag}, StartDate={StartDate}, EndDate={EndDate})",
                    packageProductSeq,
                    package.DeleteFlag,
                    package.StartDate,
                    package.EndDate);
                return PurchaseResult.Failed(PurchaseErrorCode.PackageUnavailable, $"Package {packageProductSeq} is not available");
            }

            // Step 2: Validate product exists
            var product = await this._catalogService.GetProductByPriceSeqAsync(priceSeq).ConfigureAwait(false);
            if (product is null)
            {
                this._logger.LogWarning("Product with PriceSeq {PriceSeq} not found for player {Player}", priceSeq, player.Name);
                return PurchaseResult.Failed(PurchaseErrorCode.ProductNotFound, $"Product with PriceSeq {priceSeq} not found");
            }

            // Step 3: Validate coin type is recognized (1=WCoinC, 2=WCoinP, 3=Goblin — all map to WCoin balance)
            if (coinType is < 1 or > 3)
            {
                this._logger.LogWarning("Invalid coin type {CoinType} from player {Player}", coinType, player.Name);
                return PurchaseResult.Failed(PurchaseErrorCode.Unknown, $"Invalid coin type: {coinType}");
            }

            // Step 4: Try atomic debit via WCoinService (checks balance + creates transaction in one save)
            long price = product.Price;
            var description = $"CashShop Purchase: Package {packageProductSeq}, Product {priceSeq} ({product.ProductName})";
            var debited = await WCoinService.TryDebitAsync(
                player.PersistenceContext,
                account.Id,
                price,
                WCoinTransactionType.Purchase,
                description).ConfigureAwait(false);

            if (!debited)
            {
                this._logger.LogWarning(
                    "Player {Player} has insufficient WCoin balance for purchase (Price={Price}, CoinType={CoinType})",
                    player.Name,
                    price,
                    coinType);
                return PurchaseResult.Failed(PurchaseErrorCode.InsufficientBalance, "Insufficient WCoin balance");
            }

            // Step 5: Deliver item to player (placeholder — real implementation in Fase 8)
            var deliveredItem = $"{product.ProductName} (InGamePackageID: {product.InGamePackageID})";
            this._logger.LogInformation(
                "Purchase successful: Player={Player}, Package={PackageSeq}, Product={PriceSeq}, Price={Price}, CoinType={CoinType}",
                player.Name,
                packageProductSeq,
                priceSeq,
                price,
                coinType);

            return PurchaseResult.Successful(deliveredItem);
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "Error processing purchase for player {Player}", player.Name);
            return PurchaseResult.Failed(PurchaseErrorCode.Unknown, $"Unexpected error: {ex.Message}");
        }
    }

    private bool IsPackageAvailable(CashShopPackage package)
    {
        // Check if package is deleted
        if (package.DeleteFlag != 0)
        {
            return false;
        }

        var now = DateTime.UtcNow;

        // Check if package has started
        if (package.StartDate != default && package.StartDate > now)
        {
            return false;
        }

        // Check if package has expired
        if (package.EndDate != default && package.EndDate < now)
        {
            return false;
        }

        return true;
    }
}
