// <copyright file="CashShopSelectionResolver.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.CashShop;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.CashShop;

/// <summary>
/// Resolves a client cash shop selection against the server catalog.
/// </summary>
/// <remarks>
/// The rules here come from the real IBS data and are shared by the buy (D2 03) and the gift
/// (D2 04) flows. Only WCoinP (509) packages are accepted: the wallet that exists in this server
/// is the WCOIN one, and no other currency is charged here.
/// </remarks>
public sealed class CashShopSelectionResolver : ICashShopSelectionResolver
{
    /// <summary>Package <c>DeleteFlag</c> for a preserved (buyable) package. Verified in IBSPackage.txt (181 in 212/212 rows).</summary>
    private const int ActivePackageDeleteFlag = 181;

    /// <summary>Product <c>DeleteFlag</c> for a preserved (buyable) product. Verified in IBSProduct.txt (144 in 2663/2720 rows).</summary>
    private const int ActiveProductDeleteFlag = 144;

    /// <summary><c>PackageProductType</c> for a normal (non-random) package (170 in 210/212 rows).</summary>
    private const int NormalPackageType = 170;

    /// <summary><c>EventFlag</c> for a normal (non-event) item (200 in 212/212 rows).</summary>
    private const int NormalEventFlag = 200;

    /// <summary><c>CashTypeFlag</c> for a coin purchase (669 in 212/212 rows).</summary>
    private const int NormalPackageCashTypeFlag = 669;

    /// <summary><c>SalesFlag</c> for a purchasable package (182 in 210/212 rows).</summary>
    private const int SalesFlagAvailable = 182;

    /// <summary>
    /// <c>CashType</c> for W Coin(P), the prepaid currency this flow charges.
    /// Verified in IBSPackage.txt: 509 maps 1:1 to <c>W Coin(P)</c>, 508 to <c>W Coin(C)</c> and 0 to <c>Goblin Point</c>.
    /// </summary>
    private const int WCoinPCashType = 509;

    /// <summary>Item codes are encoded as <c>group * 512 + number</c>.</summary>
    private const int ItemIndexStride = 512;

    /// <summary>
    /// Valores de <see cref="CashShopPackage.GiftFlag"/> conhecidos e verificados.
    /// </summary>
    /// <remarks>
    /// <b>NENHUM valor é conhecido.</b> A coluna existe no catálogo e é lida da coluna 9 do
    /// IBSPackage.txt (logo apos <c>SalesFlag</c>), mas a sua semantica e a sua distribuicao real
    /// nao puderam ser confirmadas:
    /// <list type="bullet">
    /// <item><description>o <c>IBSPackage.txt</c> nao esta neste repositio;</description></item>
    /// <item><description>a tabela <c>data."CashShopPackage"</c> do PostgreSQL em execucao esta
    /// <b>vazia</b> (0 linhas em <c>CashShopPackage</c> e em <c>CashShopProduct</c> verificado
    /// por SELECT em 2026-09-28), ou seja, o catalogo real nunca foi carregado;</description></item>
    /// <item><description>o codigo-fonte do cliente (Main.exe / MuMain) nao esta disponivel
    /// aqui, entao nao ha como ler o teste que o cliente faz da flag.</description></item>
    /// </list>
    /// Como a semantica e desconhecida, <b>adivinhar seria pior do que recusar</b>: tratar 0 como
    /// "presenteavel" poderia permitir que o servidor cobre e entregue um presente num pacote que
    /// a loja proibe presentear. Por isso <see cref="GiftableGiftFlagValuesKnown"/> e falso e o
    /// fluxo de presente recusa <b>todos</b> os pacotes, sem debitar WCoin.
    /// <para>
    /// Para liberar o presente, confirme em fonte concreta (IBSPackage.txt real ou o teste do
    /// cliente) quais valores significam "pode presentear", preencha
    /// <see cref="GiftableGiftFlagValues"/> e mude <see cref="GiftableGiftFlagValuesKnown"/>
    /// para <c>true</c>.
    /// </para>
    /// </remarks>
    private const bool GiftableGiftFlagValuesKnown = false;

    /// <summary>
    /// Os valores de <c>GiftFlag</c> que permitem presentear. Vazio de proposito enquanto
    /// <see cref="GiftableGiftFlagValuesKnown"/> for falso.
    /// </summary>
    private static readonly IReadOnlyCollection<int> GiftableGiftFlagValues = Array.Empty<int>();

    private readonly ICashShopCatalogService _catalogService;
    private readonly ILogger<CashShopSelectionResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CashShopSelectionResolver"/> class.
    /// </summary>
    /// <param name="catalogService">The cash shop catalog service.</param>
    /// <param name="logger">The logger.</param>
    public CashShopSelectionResolver(
        ICashShopCatalogService catalogService,
        ILogger<CashShopSelectionResolver> logger)
    {
        this._catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));
        this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async ValueTask<CashShopSelectionResult> ResolveAsync(
        Player player,
        int packageProductSeq,
        int productDisplaySeq,
        int priceSeq,
        ushort itemCode,
        int coinType,
        bool mileageFlag,
        bool requireGiftable,
        CancellationToken cancellationToken = default)
    {
        if (mileageFlag)
        {
            return CashShopSelectionResult.Failed(PurchaseErrorCode.CannotBeBought, "Mileage purchases are not supported.");
        }

        var configuration = player.GameContext.Configuration;
        var package = await this._catalogService.GetPackageBySeqAsync(productDisplaySeq, packageProductSeq, configuration, cancellationToken).ConfigureAwait(false);
        if (package is null)
        {
            return CashShopSelectionResult.Failed(PurchaseErrorCode.SelectionUnavailable, "The selected cash shop package does not exist in this category.");
        }

        if (!IsPackageAvailable(package))
        {
            return CashShopSelectionResult.Failed(PurchaseErrorCode.PackageUnavailable, "The selected cash shop package is not currently available.");
        }

        if (coinType != package.CashType)
        {
            return CashShopSelectionResult.Failed(PurchaseErrorCode.InvalidCoinType, "The requested cash type does not match the package.");
        }

        if (package.CashType != WCoinPCashType)
        {
            return CashShopSelectionResult.Failed(PurchaseErrorCode.InvalidCoinType, "The package is not purchasable with WCoinP.");
        }

        if (package.PackageProductType != NormalPackageType)
        {
            return CashShopSelectionResult.Failed(PurchaseErrorCode.CannotBeBought, "Random packages are not supported by this flow.");
        }

        if (package.EventFlag != NormalEventFlag)
        {
            return CashShopSelectionResult.Failed(PurchaseErrorCode.EventItemCannotBeBought, "Event items cannot be bought in this flow.");
        }

        if (package.CashTypeFlag != NormalPackageCashTypeFlag || package.DeductMileageFlag)
        {
            return CashShopSelectionResult.Failed(PurchaseErrorCode.CannotBeBought, "The package is not a plain WCoinP purchase.");
        }
        if (package.CashTypeFlag != NormalPackageCashTypeFlag || package.DeductMileageFlag)
        {
            return CashShopSelectionResult.Failed(PurchaseErrorCode.CannotBeBought, "The package is not a plain WCoinP purchase.");
        }

        // Presentability is a per-package catalog flag, so it is checked here and not by the
        // caller: the buy flow accepts any package, the gift flow only the giftable ones.
        if (requireGiftable && !IsGiftable(package))
        {
            this._logger.LogWarning(
                "Cash shop package {PackageProductSeq} is not giftable (GiftFlag {GiftFlag}); the gift was refused.",
                packageProductSeq,
                package.GiftFlag);
            return CashShopSelectionResult.Failed(PurchaseErrorCode.CannotBeBought, "The selected package cannot be sent as a gift.");
        }


        var productSequences = ParseSequenceList(package.ProductSeqList);
        if (productSequences.Count == 0)
        {
            return CashShopSelectionResult.Failed(PurchaseErrorCode.SelectionUnavailable, "The selected package has an invalid product mapping.");
        }

        var priceSequences = ParseSequenceList(package.PriceSeqList);
        if (priceSequences.Count == 0)
        {
            return CashShopSelectionResult.Failed(PurchaseErrorCode.SelectionUnavailable, "The selected package has an invalid price mapping.");
        }

        var selectedPriceSequence = priceSeq;
        if (selectedPriceSequence <= 0)
        {
            if (priceSequences.Count != 1)
            {
                return CashShopSelectionResult.Failed(PurchaseErrorCode.SelectionUnavailable, "A price selection is required for this package.");
            }

            selectedPriceSequence = priceSequences[0];
        }
        else if (!priceSequences.Contains(selectedPriceSequence))
        {
            return CashShopSelectionResult.Failed(PurchaseErrorCode.SelectionUnavailable, "The selected price is not listed by the package.");
        }

        // A PriceSeq alone is ambiguous: the IBS reuses it across product sequences
        // (874 distinct values for 2720 rows, up to 5 rows each). Scope the lookup to the
        // sequences the package itself declares, exactly like the client does.
        var candidates = await this._catalogService.GetPackageProductsByPriceSeqAsync(productSequences.ToArray(), selectedPriceSequence, configuration, cancellationToken).ConfigureAwait(false);
        var usableCandidates = candidates
            .Where(product => product.DeleteFlag == ActiveProductDeleteFlag)
            .Where(product => ushort.TryParse(product.InGamePackageID, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) && code == itemCode)
            .ToList();

        if (usableCandidates.Count == 0)
        {
            return CashShopSelectionResult.Failed(PurchaseErrorCode.SelectionUnavailable, "The selected product is not valid for this package.");
        }

        // Every row for one price sequence must agree on price, otherwise the catalog is
        // inconsistent and charging an arbitrary amount would be unsafe.
        var distinctPrices = usableCandidates.Select(product => product.Price).Distinct().ToList();
        if (distinctPrices.Count != 1 || distinctPrices[0] <= 0)
        {
            this._logger.LogWarning(
                "Cash shop package {PackageProductSeq} price sequence {PriceSeq} has inconsistent prices: {Prices}.",
                packageProductSeq,
                selectedPriceSequence,
                string.Join(", ", distinctPrices));
            return CashShopSelectionResult.Failed(PurchaseErrorCode.CannotBeBought, "The selected product has an inconsistent catalog price.");
        }

        // PropertySeq describes IBS property variants, but it is not part of the D2 03 selection.
        // Rows may therefore only be collapsed into one logical product when every field that
        // affects the purchase or the delivered item agrees. Comparing only the price is not
        // enough: two rows can carry the same price and item code and still differ in currency,
        // duration, quantity, storage group or a delivery flag, in which case picking either row
        // would silently hand the player something other than what the package describes.
        // Any divergence is refused as a real conflict; the resolver never takes the first, the
        // last, the lowest or the highest PropertySeq.
        var logicalKeys = usableCandidates
            .Select(BuildLogicalProductKey)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (logicalKeys.Count != 1)
        {
            this._logger.LogWarning(
                "Cash shop package {PackageProductSeq} price sequence {PriceSeq} has {Count} conflicting logical product rows for item code {ItemCode}.",
                packageProductSeq,
                selectedPriceSequence,
                logicalKeys.Count,
                itemCode);

            return CashShopSelectionResult.Failed(PurchaseErrorCode.SelectionUnavailable, "The selected product has conflicting catalog rows.");
        }

        var logicalProduct = usableCandidates[0];
        var itemCodeValue = itemCode;
        var itemDefinition = configuration.Items.FirstOrDefault(item => item.Group == itemCodeValue / ItemIndexStride && item.Number == itemCodeValue % ItemIndexStride);
        if (itemDefinition is null)
        {
            return CashShopSelectionResult.Failed(PurchaseErrorCode.CannotBeBought, "The selected item is not available in the game configuration.");
        }

        return CashShopSelectionResult.Resolved(
            logicalProduct.ProductSeq,
            logicalProduct.StorageGroup,
            logicalProduct.ProductName,
            distinctPrices[0],
            selectedPriceSequence,
            itemDefinition);
    }

    /// <summary>
    /// Builds the key that identifies one logical product behind possibly several IBS property
    /// variants.
    /// </summary>
    /// <param name="product">The catalog row.</param>
    /// <returns>
    /// A string that is equal for two rows only when they are interchangeable for the purchase and
    /// for the delivered item.
    /// </returns>
    /// <remarks>
    /// Deliberately excluded: <c>PropertySeq</c>, <c>PriceSeq</c> and <c>ViewOrder</c>, which
    /// identify the variant and the presentation rather than what the player receives. Everything
    /// that changes the charge or the item is included, and the name is used as an additional
    /// consistency signal.
    /// </remarks>
    private static string BuildLogicalProductKey(MUnique.OpenMU.DataModel.Entities.CashShopProduct product)
    {
        return string.Join(
            '|',
            product.ProductSeq,
            product.ProductName,
            product.Price,
            product.UnitType,
            product.UnitName,
            product.PropertyType,
            product.PropertyName,
            product.Value,
            product.StorageGroup,
            product.MustFlag,
            product.ShareFlag,
            product.DeleteFlag);
    }

    /// <summary>
    /// Determines whether the package may be sent as a gift, based on its catalog GiftFlag.
    /// </summary>
    private static bool IsGiftable(CashShopPackage package) =>
        GiftableGiftFlagValuesKnown && GiftableGiftFlagValues.Contains(package.GiftFlag);

    /// <summary>
    /// Determines whether the package can be bought right now, based on the flags verified in the real IBS data.
    /// </summary>
    private static bool IsPackageAvailable(CashShopPackage package)
    {
        // Zeroed dates mean "no limit", the same semantics the catalog listing uses
        // (CashShopCatalogService.GetPackagesByCategoryAsync). A package shown in the shop
        // must not be rejected at purchase time, so both paths agree here.
        var now = DateTime.UtcNow;
        return package.DeleteFlag == ActivePackageDeleteFlag
            && package.SalesFlag == SalesFlagAvailable
            && (package.StartDate == default || package.StartDate <= now)
            && (package.EndDate == default || package.EndDate >= now);
    }

    /// <summary>
    /// Parses a sequence list of the package (stored as a JSON array).
    /// </summary>
    private static IReadOnlyList<int> ParseSequenceList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<int>();
        }

        try
        {
            return JsonSerializer.Deserialize<int[]>(json) ?? Array.Empty<int>();
        }
        catch (JsonException)
        {
            return Array.Empty<int>();
        }
    }
}
