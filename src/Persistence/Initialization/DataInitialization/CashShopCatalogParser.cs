// <copyright file="CashShopCatalogParser.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.DataInitialization;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Entities;

/// <summary>
/// Parser para arquivos de catálogo IBS (In-Game Cash Shop) do protocolo Webzen 512.2012.076.
/// Processa IBSCategory.txt (7 campos), IBSPackage.txt (27 campos), IBSProduct.txt (17 campos).
/// </summary>
public class CashShopCatalogParser
{
    private const char RecordDelimiter = '@';
    private const char ListSeparator = '|';
    private const string DateFormat = "yyyyMMddHHmmss";

    private readonly ILogger<CashShopCatalogParser> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CashShopCatalogParser"/> class.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public CashShopCatalogParser(ILogger<CashShopCatalogParser> logger)
    {
        this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Representa os dados parseados do catálogo IBS completo.
    /// </summary>
    public record CatalogData(
        List<CashShopCategory> Categories,
        List<CashShopPackage> Packages,
        List<CashShopProduct> Products,
        List<PackageProductRelation> PackageProductRelations);

    /// <summary>
    /// Relação entre PackageProductSeq e PriceSeq extraída dos campos ProductSeqList / PriceSeqList.
    /// </summary>
    public record PackageProductRelation(int PackageProductSeq, int PriceSeq);

    /// <summary>
    /// Parseia o conjunto completo de arquivos do catálogo IBS.
    /// </summary>
    /// <param name="categoryFilePath">Caminho para IBSCategory.txt.</param>
    /// <param name="packageFilePath">Caminho para IBSPackage.txt.</param>
    /// <param name="productFilePath">Caminho para IBSProduct.txt.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Dados do catálogo parseados.</returns>
    public async Task<CatalogData> ParseCatalogAsync(
        string categoryFilePath,
        string packageFilePath,
        string productFilePath,
        CancellationToken cancellationToken = default)
    {
        this._logger.LogInformation("Starting IBS catalog parse...");

        var categories = await this.ParseCategoriesAsync(categoryFilePath, cancellationToken).ConfigureAwait(false);
        this._logger.LogInformation("Parsed {Count} categories.", categories.Count);

        var (packages, relations) = await this.ParsePackagesAsync(packageFilePath, cancellationToken).ConfigureAwait(false);
        this._logger.LogInformation("Parsed {Count} packages with {RelCount} product relations.", packages.Count, relations.Count);

        var products = await this.ParseProductsAsync(productFilePath, cancellationToken).ConfigureAwait(false);
        this._logger.LogInformation("Parsed {Count} products.", products.Count);

        return new CatalogData(categories, packages, products, relations);
    }

    /// <summary>
    /// Parseia IBSCategory.txt (7 campos).
    /// Formato real IBS: ProductDisplaySeq@CategoryName@EventFlag@OpenFlag@ParentProductDisplaySeq@DisplayOrder@Root
    /// Exemplo: 10@W Coin (C)@200@201@10@1@1
    /// </summary>
    private async Task<List<CashShopCategory>> ParseCategoriesAsync(string filePath, CancellationToken cancellationToken)
    {
        var categories = new List<CashShopCategory>();
        if (!File.Exists(filePath))
        {
            this._logger.LogWarning("Category file not found: {FilePath}", filePath);
            return categories;
        }

        var lines = await File.ReadAllLinesAsync(filePath, cancellationToken).ConfigureAwait(false);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = line.Split(RecordDelimiter, StringSplitOptions.None);
            if (fields.Length < 7)
            {
                this._logger.LogWarning("Invalid category line (expected 7 fields): {Line}", line);
                continue;
            }

            // Formato real: ProductDisplaySeq @ CategoryName @ EventFlag @ OpenFlag @ ParentProductDisplaySeq @ DisplayOrder @ Root
            var category = new CashShopCategory
            {
                Id = Guid.NewGuid(),
                ProductDisplaySeq = ParseInt(fields[0]),
                CategoryName = fields[1],
                EventFlag = ParseInt(fields[2]),
                OpenFlag = ParseInt(fields[3]),
                ParentProductDisplaySeq = ParseInt(fields[4]),
                DisplayOrder = ParseInt(fields[5]),
                IsRoot = ParseInt(fields[6]) == 1,
            };

            categories.Add(category);
        }

        return categories;
    }

    /// <summary>
    /// Parseia IBSPackage.txt (27 campos) e extrai as relações Package-Product das listas.
    /// </summary>
    private async Task<(List<CashShopPackage> Packages, List<PackageProductRelation> Relations)> ParsePackagesAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        var packages = new List<CashShopPackage>();
        var relations = new List<PackageProductRelation>();
        if (!File.Exists(filePath))
        {
            this._logger.LogWarning("Package file not found: {FilePath}", filePath);
            return (packages, relations);
        }

        var lines = await File.ReadAllLinesAsync(filePath, cancellationToken).ConfigureAwait(false);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = line.Split(RecordDelimiter, StringSplitOptions.None);
            if (fields.Length < 27)
            {
                this._logger.LogWarning("Invalid package line (expected 27 fields): {Line}", line);
                continue;
            }

            var packageProductSeq = ParseInt(fields[2]);

            var productSeqList = ParseIntList(fields[19]);
            var priceSeqList = ParseIntList(fields[23]);

            // Gerar relações PackageProductRelation
            for (int i = 0; i < Math.Min(productSeqList.Count, priceSeqList.Count); i++)
            {
                relations.Add(new PackageProductRelation(packageProductSeq, priceSeqList[i]));
            }

            var package = new CashShopPackage
            {
                Id = Guid.NewGuid(),
                ProductDisplaySeq = ParseInt(fields[0]),
                ViewOrder = ParseInt(fields[1]),
                PackageProductSeq = packageProductSeq,
                PackageProductName = fields[3],
                PackageProductType = ParseInt(fields[4]),
                Price = ParseInt(fields[5]),
                Description = fields[6],
                Caution = fields[7],
                SalesFlag = ParseInt(fields[8]),
                GiftFlag = ParseInt(fields[9]),
                StartDate = ParseDateTime(fields[10]),
                EndDate = ParseDateTime(fields[11]),
                CapsuleFlag = ParseInt(fields[12]),
                CapsuleCount = ParseInt(fields[13]),
                ProductCashName = fields[14],
                PriceUnitName = fields[15],
                DeleteFlag = ParseInt(fields[16]),
                EventFlag = ParseInt(fields[17]),
                ProductAmount = ParseInt(fields[18]),
                ProductSeqList = JsonSerializer.Serialize(productSeqList),
                InGamePackageID = fields[20],
                ProductCashSeq = ParseInt(fields[21]),
                PriceCount = ParseInt(fields[22]),
                PriceSeqList = JsonSerializer.Serialize(priceSeqList),
                DeductMileageFlag = ParseInt(fields[24]) == 1,
                CashType = ParseInt(fields[25]),
                CashTypeFlag = ParseInt(fields[26]),
            };

            packages.Add(package);
        }

        return (packages, relations);
    }

    /// <summary>
    /// Parseia IBSProduct.txt (17 campos).
    /// </summary>
    private async Task<List<CashShopProduct>> ParseProductsAsync(string filePath, CancellationToken cancellationToken)
    {
        var products = new List<CashShopProduct>();
        if (!File.Exists(filePath))
        {
            this._logger.LogWarning("Product file not found: {FilePath}", filePath);
            return products;
        }

        var lines = await File.ReadAllLinesAsync(filePath, cancellationToken).ConfigureAwait(false);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = line.Split(RecordDelimiter, StringSplitOptions.None);
            if (fields.Length < 17)
            {
                this._logger.LogWarning("Invalid product line (expected 17 fields): {Line}", line);
                continue;
            }

            var product = new CashShopProduct
            {
                Id = Guid.NewGuid(),
                ProductSeq = ParseInt(fields[0]),
                ProductName = fields[1],
                PropertyName = fields[2],
                Value = fields[3],
                UnitName = fields[4],
                Price = ParseInt(fields[5]),
                PriceSeq = ParseInt(fields[6]),
                PropertyType = ParseInt(fields[7]),
                MustFlag = ParseInt(fields[8]) == 145,   // IBS: 145=required, 146=optional
                ViewOrder = ParseInt(fields[9]),
                DeleteFlag = ParseInt(fields[10]),        // IBS: 144=active, 143=deleted
                StorageGroup = ParseInt(fields[11]),
                ShareFlag = ParseInt(fields[12]) == 518,  // IBS: 518=shareable, 517=not shareable
                InGamePackageID = fields[13],
                PropertySeq = ParseInt(fields[14]),
                ProductType = ParseInt(fields[15]),
                UnitType = ParseInt(fields[16]),
            };

            products.Add(product);
        }

        return products;
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : 0;
    }

    private static int? ParseNullableInt(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "0")
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
    }

    private static DateTime ParseDateTime(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return default;
        }

        return DateTime.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var result)
            ? result
            : default;
    }

    private static List<int> ParseIntList(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new List<int>();
        }

        return value.Split(ListSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => ParseInt(s))
            .ToList();
    }
}
