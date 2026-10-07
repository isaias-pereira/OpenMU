// <copyright file="CashShopCatalogParityTests.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests.CashShop;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.CashShop;
using MUnique.OpenMU.GameLogic.PlayerActions.CashShop;
using MUnique.OpenMU.Interfaces;
using NUnit.Framework;

/// <summary>
/// Validates that every buyable catalog group from the IBS snapshot resolves through the
/// same aggregation rules that individual offers use. Complementary <c>PropertySeq</c> rows
/// of one group are one offer, not competing variants.
/// </summary>
[TestFixture]
public class CashShopCatalogParityTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Every buyable group must resolve to exactly one logical offer.
    /// </summary>
    [Test]
    public async Task CatalogParity_AllBuyableGroupsResolve()
    {
        var catalog = LoadCatalog();
        Assert.That(catalog.Groups, Has.Count.EqualTo(catalog.ExpectedGroupCount), "The fixture must contain the expected group count.");
        Assert.That(catalog.Groups, Has.Count.EqualTo(143), "Catalog parity covers 143 buyable groups.");

        var refused = new List<string>();
        foreach (var group in catalog.Groups)
        {
            var result = AggregateGroup(group);
            if (!result.Success)
            {
                refused.Add($"ProductSeq={group.ProductSeq} PriceSeq={group.PriceSeq} Item={group.InGamePackageID}: {result.ErrorMessage}");
            }
        }

        Assert.That(refused, Is.Empty, $"Grupos: {catalog.Groups.Count}{Environment.NewLine}Resolvidos: {catalog.Groups.Count - refused.Count}{Environment.NewLine}Recusados: {refused.Count}{Environment.NewLine}{string.Join(Environment.NewLine, refused.Take(10))}");
    }

    private static BuyableGroupsCatalog LoadCatalog()
    {
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDirectory, "CashShop", "TestData", "CashShopCatalogBuyableGroups.json"),
            Path.Combine(baseDirectory, "TestData", "CashShopCatalogBuyableGroups.json"),
        };

        string? path = null;
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                path = candidate;
                break;
            }
        }

        // Fall back to the source tree layout when running without copied content.
        if (path is null)
        {
            var directory = new DirectoryInfo(baseDirectory);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "tests", "MUnique.OpenMU.Tests", "CashShop", "TestData", "CashShopCatalogBuyableGroups.json");
                if (File.Exists(candidate))
                {
                    path = candidate;
                    break;
                }

                candidate = Path.Combine(directory.FullName, "CashShop", "TestData", "CashShopCatalogBuyableGroups.json");
                if (File.Exists(candidate))
                {
                    path = candidate;
                    break;
                }

                directory = directory.Parent;
            }
        }

        Assert.That(path, Is.Not.Null, "CashShopCatalogBuyableGroups.json was not found.");
        using var stream = File.OpenRead(path!);
        var document = JsonSerializer.Deserialize<BuyableGroupsDocument>(stream, JsonOptions);
        Assert.That(document, Is.Not.Null);
        Assert.That(document!.Groups, Is.Not.Null);
        Assert.That(document.Packages, Is.Not.Null);
        return new BuyableGroupsCatalog(document.Groups!, document.Packages!, document.Metadata?.ExpectedGroupCount ?? document.Groups!.Count);
    }

    private static OfferAggregation AggregateGroup(BuyableGroup group)
    {
        var products = group.Rows.Select(row => new CashShopProduct
        {
            ProductSeq = group.ProductSeq,
            PriceSeq = group.PriceSeq,
            InGamePackageID = group.InGamePackageID ?? string.Empty,
            ProductName = group.ProductName ?? string.Empty,
            Price = group.Price,
            ProductType = group.ProductType,
            StorageGroup = group.StorageGroup,
            DeleteFlag = group.DeleteFlag,
            PropertySeq = row.PropertySeq,
            PropertyType = row.PropertyType,
            Value = row.Value ?? string.Empty,
            UnitName = row.UnitName ?? string.Empty,
            UnitType = row.UnitType,
            MustFlag = row.MustFlag == 145,
            ShareFlag = row.ShareFlag == 519,
            ViewOrder = row.ViewOrder,
            PropertyName = row.PropertyName ?? string.Empty,
        }).ToList();

        // Reproduce the resolver's aggregation contract without mocks: the same structural
        // rules that accept single offers must accept every real group.
        return CashShopSelectionResolver.AggregateForParity(products, group.InGamePackageID ?? string.Empty);
    }
}
