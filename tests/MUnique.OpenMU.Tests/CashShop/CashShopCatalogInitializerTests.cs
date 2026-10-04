// <copyright file="CashShopCatalogInitializerTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests.CashShop;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.Persistence.InMemory;
using MUnique.OpenMU.Persistence.Initialization.DataInitialization;
using NUnit.Framework;

/// <summary>
/// Tests the catalog seed policy using the in-memory persistence provider.
/// </summary>
[TestFixture]
public class CashShopCatalogInitializerTests
{
    private InMemoryPersistenceContextProvider _contextProvider = null!;
    private GameConfiguration _gameConfiguration = null!;

    [SetUp]
    public void SetUp()
    {
        this._contextProvider = new InMemoryPersistenceContextProvider();
        this._gameConfiguration = new MUnique.OpenMU.Persistence.BasicModel.GameConfiguration { Id = Guid.NewGuid() };
    }

    [Test]
    public async Task InitializeAsync_Reimport_DoesNotDuplicateRows()
    {
        var initializer = new CashShopCatalogInitializer(
            this._contextProvider,
            NullLogger<CashShopCatalogInitializer>.Instance);
        var data = CreateCatalogData();

        await initializer.InitializeAsync(data, this._gameConfiguration);
        await initializer.InitializeAsync(data, this._gameConfiguration);

        using var verificationContext = this._contextProvider.CreateNewContext(this._gameConfiguration);
        var categories = (await verificationContext.GetAsync<CashShopCategory>()).ToList();
        var products = (await verificationContext.GetAsync<CashShopProduct>()).ToList();
        var packages = (await verificationContext.GetAsync<CashShopPackage>()).ToList();
        var relations = (await verificationContext.GetAsync<CashShopPackageProduct>()).ToList();

        Assert.That(categories, Has.Count.EqualTo(1));
        Assert.That(products, Has.Count.EqualTo(2));
        Assert.That(packages, Has.Count.EqualTo(1));
        Assert.That(relations, Has.Count.EqualTo(2));
    }

    [Test]
    public void InitializeAsync_InvalidCategoryReference_DoesNotWritePartialCatalog()
    {
        var initializer = new CashShopCatalogInitializer(
            this._contextProvider,
            NullLogger<CashShopCatalogInitializer>.Instance);
        var data = CreateCatalogData();
        data.Packages[0].ProductDisplaySeq = 99;

        Assert.ThrowsAsync<InvalidDataException>(async () =>
            await initializer.InitializeAsync(data, this._gameConfiguration));

        using var verificationContext = this._contextProvider.CreateNewContext(this._gameConfiguration);
        Assert.That(verificationContext.GetAsync<CashShopCategory>().AsTask().GetAwaiter().GetResult(), Is.Empty);
        Assert.That(verificationContext.GetAsync<CashShopProduct>().AsTask().GetAwaiter().GetResult(), Is.Empty);
    }

    private static CashShopCatalogParser.CatalogData CreateCatalogData()
    {
        var category = new CashShopCategory
        {
            Id = Guid.NewGuid(),
            ProductDisplaySeq = 10,
            CategoryName = "W Coin (C)",
            EventFlag = 200,
            OpenFlag = 201,
            ParentProductDisplaySeq = 10,
            DisplayOrder = 1,
            IsRoot = true,
        };
        var products = new List<CashShopProduct>
        {
            new() { Id = Guid.NewGuid(), ProductSeq = 3, ProductName = "Item", PropertyName = "Duration", Value = "1", UnitName = "Day", PriceSeq = 7, PropertySeq = 1 },
            new() { Id = Guid.NewGuid(), ProductSeq = 3, ProductName = "Item", PropertyName = "Duration", Value = "2", UnitName = "Day", PriceSeq = 7, PropertySeq = 2 },
        };
        var package = new CashShopPackage
        {
            Id = Guid.NewGuid(),
            ProductDisplaySeq = 10,
            PackageProductSeq = 100,
            PackageProductName = "Package",
            DeleteFlag = 181,
            ProductSeqList = JsonSerializer.Serialize(new[] { 3 }),
            PriceSeqList = JsonSerializer.Serialize(new[] { 7 }),
        };

        return new CashShopCatalogParser.CatalogData(
            new List<CashShopCategory> { category },
            new List<CashShopPackage> { package },
            products,
            new List<CashShopCatalogParser.PackageProductRelation>
            {
                new(10, 100, 3, 7),
            });
    }
}
