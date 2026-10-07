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
    /// <c>PropertySeq</c> values that carry the item's usage duration, confirmed in
    /// <c>CInGameShopSystem::GetProductInfo</c>, case <c>IGS_PRODUCT_ATT_TYPE_USE_LIMIT_PERIOD</c>
    /// (<c>MuMain\src\source\GameShop\InGameShopSystem.cpp:634</c>).
    /// </summary>
    /// <remarks>
    /// These rows are <b>attributes of one offer</b>, not competing variants: the client walks every
    /// row of a price sequence in <c>GetPriceSeqNext</c> and pulls out the value of the attribute it
    /// asked for, so a group such as 10/37/53 describes one product that lasts one day.
    /// </remarks>
    private static readonly IReadOnlyCollection<int> DurationPropertySequences = new[] { 2, 12, 28, 58, 10 };

    /// <summary>
    /// <c>PropertySeq</c> values that carry the item's quantity, confirmed in the same function,
    /// case <c>IGS_PRODUCT_ATT_TYPE_NUM</c> (<c>InGameShopSystem.cpp:710</c>).
    /// </summary>
    private static readonly IReadOnlyCollection<int> QuantityPropertySequences = new[] { 7, 8, 9, 11, 30, 31 };

    /// <summary>
    /// <c>UnitType</c> meaning the value is expressed in seconds.
    /// </summary>
    /// <remarks>
    /// <c>InGameShopSystem.cpp:640</c> handles this unit by dividing by 86400/3600/60, which only
    /// makes sense for a value in seconds. In the shipped catalog this is an exact bijection with
    /// the duration rows: all 336 of them use unit 386, and unit 386 appears nowhere else.
    /// </remarks>
    private const int SecondsUnitType = 386;

    /// <summary><c>UnitType</c> meaning the value is expressed in minutes (<c>InGameShopSystem.cpp:662</c>).</summary>
    private const int MinutesUnitType = 174;

    /// <summary><c>UnitType</c> meaning the value is expressed in hours (<c>InGameShopSystem.cpp:679</c>).</summary>
    private const int HoursUnitType = 172;

    /// <summary>The quantity of an offer that carries no quantity property row.</summary>
    private const int DefaultQuantity = 1;

    /// <summary>
    /// <c>GiftFlag</c> value that enables gifting, confirmed in the client source.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>MuMain\src\source\GameShop\MsgBoxIGSBuySelectItem.cpp:67</c> and
    /// <c>MsgBoxIGSBuyPackageItem.cpp:68</c> both gate the present button on exactly this value:
    /// <c>if (pPackage-&gt;GiftFlag == 184) { m_BtnPresent.SetEnable(true); } else { m_BtnPresent.SetEnable(false); }</c>.
    /// </para>
    /// <para>
    /// This is an exact comparison against a catalog code, <b>not</b> a truthiness test: the
    /// shipped catalog uses 184 for 135 packages and 185 for the other 77, so any other value -
    /// including 0 and 1 - means the shop does not allow gifting that package.
    /// </para>
    /// </remarks>
    private const int GiftableGiftFlag = 184;

    /// <summary>
    /// The <see cref="GiftFlag"/> values that allow gifting.
    /// </summary>
    /// <remarks>
    /// Kept as a collection rather than a single constant so a future catalog code with different
    /// semantics can be added without changing the comparison. Membership is the test: a flag is
    /// never coerced to a boolean, because 0 and 1 are real catalog values that do <b>not</b> mean
    /// "giftable".
    /// </remarks>
    private static readonly IReadOnlyCollection<int> GiftableGiftFlagValues = new[] { GiftableGiftFlag };

    private readonly ICashShopCatalogService _catalogService;
    private readonly ILogger<CashShopSelectionResolver> _logger;

    /// <summary>
    /// Aggregates one pre-filtered catalog group for the parity test, using the same structural
    /// rules as a live purchase (no package, price-list or item-code lookup involved).
    /// </summary>
    /// <param name="products">The complementary property rows of one group.</param>
    /// <param name="expectedItemCode">The item code every row must carry.</param>
    /// <returns>The aggregated offer, or the refusal reason.</returns>
    internal static OfferAggregation AggregateForParity(IReadOnlyList<CashShopProduct> products, string expectedItemCode)
    {
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<CashShopSelectionResolver>.Instance;
        var itemCode = ushort.TryParse(expectedItemCode, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : (ushort)0;
        var productSeq = products.Count > 0 ? products[0].ProductSeq : 0;
        var priceSeq = products.Count > 0 ? products[0].PriceSeq : 0;
        return AggregateOffer(products, productSeq, priceSeq, itemCode, logger);
    }

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

        // A logical product is the whole set of property rows the catalog published for this product and
// price sequence, not one row. The client walks every row in GetPriceSeqNext and reads the value of
// the attribute it needs (InGameShopSystem.cpp:588-606), so rows that differ in PropertyName,
// Value, UnitName, UnitType, MustFlag or ViewOrder are complementary attributes of a single offer.
// The resolver therefore aggregates them; it never picks the first, last, lowest or highest
// PropertySeq, because none of those rows is the product on its own.
var offer = AggregateOffer(usableCandidates, packageProductSeq, selectedPriceSequence, itemCode, this._logger);
if (!offer.Success)
{
    return CashShopSelectionResult.Failed(offer.ErrorCode, offer.ErrorMessage!);
}

var itemCodeValue = itemCode;
        var itemDefinition = configuration.Items.FirstOrDefault(item => item.Group == itemCodeValue / ItemIndexStride && item.Number == itemCodeValue % ItemIndexStride);
        if (itemDefinition is null)
        {
            return CashShopSelectionResult.Failed(PurchaseErrorCode.CannotBeBought, "The selected item is not available in the game configuration.");
        }

        return CashShopSelectionResult.Resolved(
            offer.ProductSequence,
            offer.StorageGroup,
            offer.ProductName,
            distinctPrices[0],
            selectedPriceSequence,
            itemDefinition,
            offer.DurationSeconds,
            offer.Quantity);
    }

    /// <summary>
    /// Aggregates the property rows of one logical product into the attributes the server needs.
    /// </summary>
    /// <param name="rows">All rows of this product and price sequence that survived the filters.</param>
    /// <param name="packageProductSeq">The package sequence, for logging.</param>
    /// <param name="priceSeq">The price sequence, for logging.</param>
    /// <param name="itemCode">The requested item code, for logging.</param>
    /// <param name="logger">The logger used for refusal diagnostics.</param>
    /// <returns>The aggregated offer, or the reason it was refused.</returns>
    /// <remarks>
    /// <para>
    /// Only a genuine contradiction refuses the selection. Two duration rows that agree, two
    /// quantity rows that agree, and any number of metadata rows are normal catalog shapes, so they
    /// are aggregated rather than rejected. Divergence on the effective duration, on the effective
    /// quantity or on the product's own sequence is refused, because there would be no defensible
    /// answer.
    /// </para>
    /// <para>
    /// <c>StorageGroup</c>, <c>ShareFlag</c>, <c>PropertyType</c>, <c>DeleteFlag</c> and
    /// <c>ProductType</c> are checked for consistency but never used as the product's identity: the
    /// client relays them without reading them, and in the shipped catalog they are constant across
    /// every group.
    /// </para>
    /// </remarks>
    private static OfferAggregation AggregateOffer(IReadOnlyList<CashShopProduct> rows, int packageProductSeq, int priceSeq, ushort itemCode, ILogger<CashShopSelectionResolver>? logger = null)
    {
        logger ??= Microsoft.Extensions.Logging.Abstractions.NullLogger<CashShopSelectionResolver>.Instance;
        var productSequences = rows.Select(row => row.ProductSeq).Distinct().ToList();
        if (productSequences.Count != 1)
        {
            logger.LogWarning(
                "Cash shop package {PackageProductSeq} price sequence {PriceSeq} mixes product sequences {Sequences} for item code {ItemCode}.",
                packageProductSeq, priceSeq, string.Join(", ", productSequences), itemCode);
            return OfferAggregation.Failed(PurchaseErrorCode.SelectionUnavailable, "The selected product mixes different product sequences.");
        }

        foreach (var (field, values) in ConsistencyFields(rows))
        {
            if (values.Count > 1)
            {
                logger.LogWarning(
                    "Cash shop package {PackageProductSeq} price sequence {PriceSeq} has inconsistent {Field} values {Values} for item code {ItemCode}.",
                    packageProductSeq, priceSeq, field, string.Join(", ", values), itemCode);
                return OfferAggregation.Failed(PurchaseErrorCode.SelectionUnavailable, "The selected product has inconsistent catalog rows.");
            }
        }

        var durationSeconds = AggregateDuration(rows, packageProductSeq, priceSeq, itemCode, logger, out var durationFailed);
        if (durationFailed)
        {
            return OfferAggregation.Failed(PurchaseErrorCode.SelectionUnavailable, "The selected product has conflicting duration rows.");
        }

        var quantity = AggregateQuantity(rows, packageProductSeq, priceSeq, itemCode, logger, out var quantityFailed);
        if (quantityFailed)
        {
            return OfferAggregation.Failed(PurchaseErrorCode.SelectionUnavailable, "The selected product has conflicting quantity rows.");
        }

        return OfferAggregation.Aggregated(
            productSequences[0],
            rows[0].StorageGroup,
            rows[0].ProductName,
            durationSeconds,
            quantity);
    }

    /// <summary>
    /// Gets the offer-level fields that every row of a product must agree on.
    /// </summary>
    /// <remarks>
    /// These are consistency checks, not identity: a divergence means the catalog rows contradict
    /// each other, which is refused. Agreement does not make them part of the product key.
    /// </remarks>
    private static IEnumerable<(string Field, List<string> Values)> ConsistencyFields(IReadOnlyList<CashShopProduct> rows)
    {
        yield return ("StorageGroup", rows.Select(row => row.StorageGroup.ToString(CultureInfo.InvariantCulture)).Distinct().ToList());
        yield return ("ShareFlag", rows.Select(row => row.ShareFlag.ToString(CultureInfo.InvariantCulture)).Distinct().ToList());
        yield return ("PropertyType", rows.Select(row => row.PropertyType.ToString(CultureInfo.InvariantCulture)).Distinct().ToList());
        yield return ("DeleteFlag", rows.Select(row => row.DeleteFlag.ToString(CultureInfo.InvariantCulture)).Distinct().ToList());
        yield return ("ProductType", rows.Select(row => row.ProductType.ToString(CultureInfo.InvariantCulture)).Distinct().ToList());
    }

    /// <summary>
    /// Aggregates the duration from the duration property rows, in seconds.
    /// </summary>
    /// <returns>The duration in seconds, or zero when the offer is permanent.</returns>
    /// <remarks>
    /// A missing, empty, zero, negative or non-numeric value means "no duration", which mirrors the
    /// client: it renders a period only when the parsed value is positive
    /// (<c>MsgBoxIGSBuySelectItem.cpp:347-355</c>). Rows that resolve to the same effective number
    /// of seconds agree and are accepted; rows that disagree are a real conflict.
    /// </remarks>
    private static int AggregateDuration(IReadOnlyList<CashShopProduct> rows, int packageProductSeq, int priceSeq, ushort itemCode, ILogger<CashShopSelectionResolver> logger, out bool failed)
    {
        failed = false;
        var seconds = new HashSet<int>();
        foreach (var row in rows.Where(row => DurationPropertySequences.Contains(row.PropertySeq)))
        {
            if (!TryReadNumericValue(row, packageProductSeq, priceSeq, itemCode, logger, out var value))
            {
                continue;
            }

            seconds.Add(checked(value * GetSecondsMultiplier(row, packageProductSeq, priceSeq, itemCode, logger)));
        }

        if (seconds.Count == 0)
        {
            return 0;
        }

        if (seconds.Count > 1)
        {
            logger.LogWarning(
                "Cash shop package {PackageProductSeq} price sequence {PriceSeq} has conflicting durations {Durations} seconds for item code {ItemCode}.",
                packageProductSeq, priceSeq, string.Join(", ", seconds.OrderBy(value => value)), itemCode);
            failed = true;
            return 0;
        }

        return seconds.First();
    }

    /// <summary>
    /// Aggregates the quantity from the quantity property rows.
    /// </summary>
    /// <returns>The quantity, or one when the offer carries no quantity row.</returns>
    private static int AggregateQuantity(IReadOnlyList<CashShopProduct> rows, int packageProductSeq, int priceSeq, ushort itemCode, ILogger<CashShopSelectionResolver> logger, out bool failed)
    {
        failed = false;
        var quantities = new HashSet<int>();
        foreach (var row in rows.Where(row => QuantityPropertySequences.Contains(row.PropertySeq)))
        {
            if (!TryReadNumericValue(row, packageProductSeq, priceSeq, itemCode, logger, out var value))
            {
                continue;
            }

            quantities.Add(value);
        }

        if (quantities.Count == 0)
        {
            return DefaultQuantity;
        }

        if (quantities.Count > 1)
        {
            logger.LogWarning(
                "Cash shop package {PackageProductSeq} price sequence {PriceSeq} has conflicting quantities {Quantities} for item code {ItemCode}.",
                packageProductSeq, priceSeq, string.Join(", ", quantities.OrderBy(value => value)), itemCode);
            failed = true;
            return DefaultQuantity;
        }

        return quantities.First();
    }

    /// <summary>
    /// Gets how many catalog units one <c>Value</c> of this row represents in seconds.
    /// </summary>
    /// <remarks>
    /// Only 386 occurs in the shipped catalog. 174 and 172 exist in the client but not in this
    /// catalog, so reaching them is logged: the conversion follows the client, but it is not covered
    /// by real data here. Any other unit is logged and read as seconds, which is the fail-safe
    /// reading for a unit the catalog does not define.
    /// </remarks>
    private static int GetSecondsMultiplier(CashShopProduct row, int packageProductSeq, int priceSeq, ushort itemCode, ILogger<CashShopSelectionResolver> logger)
    {
        switch (row.UnitType)
        {
            case SecondsUnitType:
                return 1;
            case MinutesUnitType:
                logger.LogWarning(
                    "Cash shop package {PackageProductSeq} price sequence {PriceSeq} item code {ItemCode} uses the minutes unit {UnitType}, which this catalog does not exercise.",
                    packageProductSeq, priceSeq, itemCode, row.UnitType);
                return 60;
            case HoursUnitType:
                logger.LogWarning(
                    "Cash shop package {PackageProductSeq} price sequence {PriceSeq} item code {ItemCode} uses the hours unit {UnitType}, which this catalog does not exercise.",
                    packageProductSeq, priceSeq, itemCode, row.UnitType);
                return 3600;
            default:
                logger.LogWarning(
                    "Cash shop package {PackageProductSeq} price sequence {PriceSeq} item code {ItemCode} uses the unknown unit {UnitType}; the value is read as seconds.",
                    packageProductSeq, priceSeq, itemCode, row.UnitType);
                return 1;
        }
    }

    /// <summary>
    /// Reads a property row's numeric value, logging anything unusable instead of throwing.
    /// </summary>
    /// <remarks>
    /// The catalog does contain a junk row whose value is the literal text <c>test</c>, and the
    /// client reads it with <c>_wtoi</c>, which yields zero instead of failing. Parsing here is
    /// equally forgiving so that one bad row can never take down an otherwise valid purchase.
    /// </remarks>
    private static bool TryReadNumericValue(CashShopProduct row, int packageProductSeq, int priceSeq, ushort itemCode, ILogger<CashShopSelectionResolver> logger, out int value)
    {
        if (!int.TryParse(row.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value <= 0)
        {
            logger.LogWarning(
                "Cash shop package {PackageProductSeq} price sequence {PriceSeq} item code {ItemCode} property {PropertySeq} has the unusable value '{Value}' and is treated as absent.",
                packageProductSeq, priceSeq, itemCode, row.PropertySeq, row.Value);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Determines whether the package may be sent as a gift, based on its catalog GiftFlag.
    /// </summary>
    /// <remarks>
    /// Only <see cref="GiftableGiftFlag"/> (184) allows gifting. Every other catalog value is
    /// refused, including 0 and 1: a flag is compared by value, never coerced to a boolean.
    /// </remarks>
    private static bool IsGiftable(CashShopPackage package) =>
        GiftableGiftFlagValues.Contains(package.GiftFlag);

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

/// <summary>
///     The attributes of one logical product, aggregated from its complementary property rows, or the
///     reason the selection was refused.
/// </summary>
internal sealed class OfferAggregation
{
    private OfferAggregation(bool success, int productSequence, int storageGroup, string productName, int durationSeconds, int quantity, PurchaseErrorCode errorCode, string? errorMessage)
    {
        this.Success = success;
        this.ProductSequence = productSequence;
        this.StorageGroup = storageGroup;
        this.ProductName = productName;
        this.DurationSeconds = durationSeconds;
        this.Quantity = quantity;
        this.ErrorCode = errorCode;
        this.ErrorMessage = errorMessage;
    }

    /// <summary>Gets a value indicating whether the offer was aggregated.</summary>
    public bool Success { get; }

    /// <summary>Gets the product sequence shared by every row.</summary>
    public int ProductSequence { get; }

    /// <summary>Gets the storage group shared by every row, carried as metadata.</summary>
    public int StorageGroup { get; }

    /// <summary>Gets the catalog name shared by every row.</summary>
    public string ProductName { get; }

    /// <summary>Gets the aggregated duration in seconds; zero when the offer is permanent.</summary>
    public int DurationSeconds { get; }

    /// <summary>Gets the aggregated quantity; one when the offer carries no quantity row.</summary>
    public int Quantity { get; }

    /// <summary>Gets the reason the selection was refused.</summary>
    public PurchaseErrorCode ErrorCode { get; }

    /// <summary>Gets the message describing the refusal.</summary>
    public string? ErrorMessage { get; }

    /// <summary>Creates an aggregated offer.</summary>
    /// <param name="productSequence">The shared product sequence.</param>
    /// <param name="storageGroup">The shared storage group.</param>
    /// <param name="productName">The shared catalog name.</param>
    /// <param name="durationSeconds">The aggregated duration in seconds.</param>
    /// <param name="quantity">The aggregated quantity.</param>
    /// <returns>The aggregated offer.</returns>
    public static OfferAggregation Aggregated(int productSequence, int storageGroup, string productName, int durationSeconds, int quantity) =>
        new(true, productSequence, storageGroup, productName, durationSeconds, quantity, PurchaseErrorCode.None, null);

    /// <summary>Creates a refused aggregation.</summary>
    /// <param name="errorCode">The reason for the refusal.</param>
    /// <param name="errorMessage">The message describing the refusal.</param>
    /// <returns>The refused aggregation.</returns>
    public static OfferAggregation Failed(PurchaseErrorCode errorCode, string errorMessage) =>
        new(false, 0, 0, string.Empty, 0, 1, errorCode, errorMessage);
}