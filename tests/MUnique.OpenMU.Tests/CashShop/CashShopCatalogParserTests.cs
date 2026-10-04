// <copyright file="CashShopCatalogParserTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests.CashShop;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using MUnique.OpenMU.Persistence.Initialization.DataInitialization;
using NUnit.Framework;

/// <summary>
/// Testes unitários para <see cref="CashShopCatalogParser"/>.
/// Valida o parser de arquivos IBS (IBSCategory.txt, IBSPackage.txt, IBSProduct.txt).
/// </summary>
[TestFixture]
public class CashShopCatalogParserTests
{
    private CashShopCatalogParser _parser = null!;
    private string _tempDirectory = null!;

    [SetUp]
    public void SetUp()
    {
        var loggerMock = new Mock<ILogger<CashShopCatalogParser>>();
        this._parser = new CashShopCatalogParser(loggerMock.Object);
        this._tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(this._tempDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(this._tempDirectory))
        {
            Directory.Delete(this._tempDirectory, true);
        }
    }

    [Test]
    public async Task ParseCategoriesAsync_ValidFile_ParsesCorrectly()
    {
        // Arrange — formato real IBS: ProductDisplaySeq@CategoryName@EventFlag@OpenFlag@ParentProductDisplaySeq@DisplayOrder@Root
        var categoryFile = Path.Combine(this._tempDirectory, "IBSCategory.txt");
        var content = "10@W Coin (C)@200@201@10@1@1\n" +
                      "13@Special@200@201@10@1@0\n";
        await File.WriteAllTextAsync(categoryFile, content);
        await File.WriteAllTextAsync(Path.Combine(this._tempDirectory, "empty_packages.txt"), string.Empty);
        await File.WriteAllTextAsync(Path.Combine(this._tempDirectory, "empty_products.txt"), string.Empty);

        // Act
        var result = await this._parser.ParseCatalogAsync(
            categoryFile,
            Path.Combine(this._tempDirectory, "empty_packages.txt"),
            Path.Combine(this._tempDirectory, "empty_products.txt"));

        // Assert
        Assert.That(result.Categories.Count, Is.EqualTo(2));

        var rootCategory = result.Categories[0];
        Assert.That(rootCategory.ProductDisplaySeq, Is.EqualTo(10));
        Assert.That(rootCategory.CategoryName, Is.EqualTo("W Coin (C)"));
        Assert.That(rootCategory.EventFlag, Is.EqualTo(200));
        Assert.That(rootCategory.OpenFlag, Is.EqualTo(201));
        Assert.That(rootCategory.ParentProductDisplaySeq, Is.EqualTo(10));
        Assert.That(rootCategory.DisplayOrder, Is.EqualTo(1));
        Assert.That(rootCategory.IsRoot, Is.True);

        var subCategory = result.Categories[1];
        Assert.That(subCategory.ProductDisplaySeq, Is.EqualTo(13));
        Assert.That(subCategory.CategoryName, Is.EqualTo("Special"));
        Assert.That(subCategory.IsRoot, Is.False);
        Assert.That(subCategory.ParentProductDisplaySeq, Is.EqualTo(10));
    }

    [Test]
    public async Task ParsePackagesAsync_ValidFile_ParsesCorrectly()
    {
        // Arrange
        var packageFile = Path.Combine(this._tempDirectory, "IBSPackage.txt");
        // Formato: ProductDisplaySeq@ViewOrder@PackageProductSeq@PackageProductName@PackageProductType@Price@Description@Caution@SalesFlag@GiftFlag@StartDate@EndDate@CapsuleFlag@CapsuleCount@ProductCashName@PriceUnitName@DeleteFlag@EventFlag@ProductAmount@ProductSeqList@InGamePackageID@ProductCashSeq@PriceCount@PriceSeqList@DeductMileageFlag@CashType@CashTypeFlag@
        var content = "1@0@100@Test Package@1@1000@Package description@Package caution@1@1@20260101000000@20261231235959@0@0@CashName@USD@0@0@1@101|102@PKG001@301@2@201|202@0@0@0\n";
        await File.WriteAllTextAsync(packageFile, content);
        await File.WriteAllTextAsync(Path.Combine(this._tempDirectory, "empty_categories.txt"), string.Empty);
        await File.WriteAllTextAsync(Path.Combine(this._tempDirectory, "empty_products.txt"), string.Empty);

        // Act
        var result = await this._parser.ParseCatalogAsync(
            Path.Combine(this._tempDirectory, "empty_categories.txt"),
            packageFile,
            Path.Combine(this._tempDirectory, "empty_products.txt"));

        // Assert
        Assert.That(result.Packages.Count, Is.EqualTo(1));

        var package = result.Packages[0];
        Assert.That(package.PackageProductSeq, Is.EqualTo(100));
        Assert.That(package.ProductDisplaySeq, Is.EqualTo(1));
        Assert.That(package.PackageProductName, Is.EqualTo("Test Package"));
        Assert.That(package.ViewOrder, Is.EqualTo(0));
        Assert.That(package.Description, Is.EqualTo("Package description"));
        Assert.That(package.Caution, Is.EqualTo("Package caution"));
        Assert.That(package.Price, Is.EqualTo(1000));
        Assert.That(package.InGamePackageID, Is.EqualTo("PKG001"));

        // Verifica listas parseadas como JSON
        var productSeqList = JsonSerializer.Deserialize<List<int>>(package.ProductSeqList);
        Assert.That(productSeqList, Is.Not.Null);
        Assert.That(productSeqList!.Count, Is.EqualTo(2));
        Assert.That(productSeqList[0], Is.EqualTo(101));
        Assert.That(productSeqList[1], Is.EqualTo(102));

        var priceSeqList = JsonSerializer.Deserialize<List<int>>(package.PriceSeqList);
        Assert.That(priceSeqList, Is.Not.Null);
        Assert.That(priceSeqList!.Count, Is.EqualTo(2));
        Assert.That(priceSeqList[0], Is.EqualTo(201));
        Assert.That(priceSeqList[1], Is.EqualTo(202));

        // Verifica datas parseadas
        Assert.That(package.StartDate, Is.EqualTo(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        Assert.That(package.EndDate, Is.EqualTo(new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc)));

        // Verifica relações extraídas
        Assert.That(result.PackageProductRelations.Count, Is.EqualTo(4));
        Assert.That(result.PackageProductRelations[0].PackageProductSeq, Is.EqualTo(100));
        Assert.That(result.PackageProductRelations[0].ProductSeq, Is.EqualTo(101));
        Assert.That(result.PackageProductRelations[0].PriceSeq, Is.EqualTo(201));
        Assert.That(result.PackageProductRelations[1].ProductSeq, Is.EqualTo(101));
        Assert.That(result.PackageProductRelations[1].PriceSeq, Is.EqualTo(202));
        Assert.That(result.PackageProductRelations[2].ProductSeq, Is.EqualTo(102));
        Assert.That(result.PackageProductRelations[2].PriceSeq, Is.EqualTo(201));
        Assert.That(result.PackageProductRelations[3].ProductSeq, Is.EqualTo(102));
        Assert.That(result.PackageProductRelations[3].PriceSeq, Is.EqualTo(202));
    }

    [Test]
    public async Task ParseProductsAsync_Cp949Encoding_PreservesText()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);
        var productFile = Path.Combine(this._tempDirectory, "IBSProduct.txt");
        var content = "101@한글@Property@Value@Unit@1000@201@1@145@0@144@2@518@ITEM001@5@3@1\n";
        await File.WriteAllBytesAsync(productFile, cp949.GetBytes(content));
        await File.WriteAllTextAsync(Path.Combine(this._tempDirectory, "empty_categories.txt"), string.Empty);
        await File.WriteAllTextAsync(Path.Combine(this._tempDirectory, "empty_packages.txt"), string.Empty);

        var result = await this._parser.ParseCatalogAsync(
            Path.Combine(this._tempDirectory, "empty_categories.txt"),
            Path.Combine(this._tempDirectory, "empty_packages.txt"),
            productFile);

        Assert.That(result.Products, Has.Count.EqualTo(1));
        Assert.That(result.Products[0].ProductName, Is.EqualTo("한글"));
    }


    [Test]
    public async Task ParseProductsAsync_ValidFile_ParsesCorrectly()
    {
        // Arrange
        var productFile = Path.Combine(this._tempDirectory, "IBSProduct.txt");
        // Formato real IBS: ProductSeq@ProductName@PropertyName@Value@UnitName@Price@PriceSeq@PropertyType@MustFlag@ViewOrder@DeleteFlag@StorageGroup@ShareFlag@InGamePackageID@PropertySeq@ProductType@UnitType@
        // MustFlag: 145=required, 146=optional | DeleteFlag: 144=active | ShareFlag: 518=shareable
        var content = "101@Test Product@Property@Value@Unit@1000@201@1@145@0@144@2@518@ITEM001@5@3@1\n";
        await File.WriteAllTextAsync(productFile, content);
        await File.WriteAllTextAsync(Path.Combine(this._tempDirectory, "empty_categories.txt"), string.Empty);
        await File.WriteAllTextAsync(Path.Combine(this._tempDirectory, "empty_packages.txt"), string.Empty);

        // Act
        var result = await this._parser.ParseCatalogAsync(
            Path.Combine(this._tempDirectory, "empty_categories.txt"),
            Path.Combine(this._tempDirectory, "empty_packages.txt"),
            productFile);

        // Assert
        Assert.That(result.Products.Count, Is.EqualTo(1));

        var product = result.Products[0];
        Assert.That(product.ProductSeq, Is.EqualTo(101));
        Assert.That(product.ProductName, Is.EqualTo("Test Product"));
        Assert.That(product.PropertyName, Is.EqualTo("Property"));
        Assert.That(product.Value, Is.EqualTo("Value"));
        Assert.That(product.UnitName, Is.EqualTo("Unit"));
        Assert.That(product.Price, Is.EqualTo(1000));
        Assert.That(product.PriceSeq, Is.EqualTo(201));
        Assert.That(product.PropertyType, Is.EqualTo(1));
        Assert.That(product.MustFlag, Is.True);
        Assert.That(product.ViewOrder, Is.EqualTo(0));
        Assert.That(product.DeleteFlag, Is.EqualTo(144));
        Assert.That(product.StorageGroup, Is.EqualTo(2));
        Assert.That(product.ShareFlag, Is.True);
        Assert.That(product.InGamePackageID, Is.EqualTo("ITEM001"));
        Assert.That(product.PropertySeq, Is.EqualTo(5));
        Assert.That(product.ProductType, Is.EqualTo(3));
        Assert.That(product.UnitType, Is.EqualTo(1));
    }

    [Test]
    public async Task ParseCatalogAsync_EmptyFiles_ReturnsEmptyCollections()
    {
        // Arrange
        var categoryFile = Path.Combine(this._tempDirectory, "empty_categories.txt");
        var packageFile = Path.Combine(this._tempDirectory, "empty_packages.txt");
        var productFile = Path.Combine(this._tempDirectory, "empty_products.txt");
        await File.WriteAllTextAsync(categoryFile, string.Empty);
        await File.WriteAllTextAsync(packageFile, string.Empty);
        await File.WriteAllTextAsync(productFile, string.Empty);

        // Act
        var result = await this._parser.ParseCatalogAsync(categoryFile, packageFile, productFile);

        // Assert
        Assert.That(result.Categories, Is.Empty);
        Assert.That(result.Packages, Is.Empty);
        Assert.That(result.Products, Is.Empty);
        Assert.That(result.PackageProductRelations, Is.Empty);
    }

    [Test]
    public async Task ParsePackagesAsync_EmptyLists_ParsesCorrectly()
    {
        // Arrange
        var packageFile = Path.Combine(this._tempDirectory, "IBSPackage.txt");
        var content = "1@0@100@Package No Products@1@1000@Description@Caution@1@1@@@0@0@CashName@USD@0@0@1@@PKG001@301@0@@0@0@0\n";
        await File.WriteAllTextAsync(packageFile, content);
        await File.WriteAllTextAsync(Path.Combine(this._tempDirectory, "empty_categories.txt"), string.Empty);
        await File.WriteAllTextAsync(Path.Combine(this._tempDirectory, "empty_products.txt"), string.Empty);

        // Act
        var result = await this._parser.ParseCatalogAsync(
            Path.Combine(this._tempDirectory, "empty_categories.txt"),
            packageFile,
            Path.Combine(this._tempDirectory, "empty_products.txt"));

        // Assert
        Assert.That(result.Packages.Count, Is.EqualTo(1));
        Assert.That(result.PackageProductRelations, Is.Empty);

        var package = result.Packages[0];
        var productSeqList = JsonSerializer.Deserialize<List<int>>(package.ProductSeqList);
        var priceSeqList = JsonSerializer.Deserialize<List<int>>(package.PriceSeqList);

        Assert.That(productSeqList, Is.Empty);
        Assert.That(priceSeqList, Is.Empty);
    }

    [Test]
    public async Task ParseCategoriesAsync_InvalidLineFormat_SkipsInvalidLines()
    {
        // Arrange — formato real IBS: ProductDisplaySeq@CategoryName@EventFlag@OpenFlag@ParentProductDisplaySeq@DisplayOrder@Root
        var categoryFile = Path.Combine(this._tempDirectory, "IBSCategory.txt");
        var content = "10@Valid Category@200@201@10@1@1\n" +
                      "INVALID LINE\n" +
                      "13@Another Valid@200@201@10@2@0\n";
        await File.WriteAllTextAsync(categoryFile, content);
        await File.WriteAllTextAsync(Path.Combine(this._tempDirectory, "empty_packages.txt"), string.Empty);
        await File.WriteAllTextAsync(Path.Combine(this._tempDirectory, "empty_products.txt"), string.Empty);

        // Act / Assert - uma linha malformada interrompe o parse para não importar catálogo parcial.
        Assert.ThrowsAsync<InvalidDataException>(async () => await this._parser.ParseCatalogAsync(
            categoryFile,
            Path.Combine(this._tempDirectory, "empty_packages.txt"),
            Path.Combine(this._tempDirectory, "empty_products.txt")));
    }

    [Test]
    public async Task ParsePackagesAsync_MismatchedListLengths_UsesCartesianProduct()
    {
        // Arrange
        var packageFile = Path.Combine(this._tempDirectory, "IBSPackage.txt");
        // ProductSeqList tem 3 itens (101|102|103), PriceSeqList tem 2 (201|202): são 6 combinações.
        var content = "1@0@100@Package@1@1000@Desc@Caution@1@1@@@0@0@CashName@USD@0@0@1@101|102|103@PKG@301@2@201|202@0@0@0\n";
        await File.WriteAllTextAsync(packageFile, content);
        await File.WriteAllTextAsync(Path.Combine(this._tempDirectory, "empty_categories.txt"), string.Empty);
        await File.WriteAllTextAsync(Path.Combine(this._tempDirectory, "empty_products.txt"), string.Empty);

        // Act
        var result = await this._parser.ParseCatalogAsync(
            Path.Combine(this._tempDirectory, "empty_categories.txt"),
            packageFile,
            Path.Combine(this._tempDirectory, "empty_products.txt"));

        Assert.That(result.PackageProductRelations.Count, Is.EqualTo(6));
        Assert.That(result.PackageProductRelations[0].PackageProductSeq, Is.EqualTo(100));
        Assert.That(result.PackageProductRelations[0].ProductSeq, Is.EqualTo(101));
        Assert.That(result.PackageProductRelations[0].PriceSeq, Is.EqualTo(201));
        Assert.That(result.PackageProductRelations[1].PriceSeq, Is.EqualTo(202));
        Assert.That(result.PackageProductRelations[5].ProductSeq, Is.EqualTo(103));
        Assert.That(result.PackageProductRelations[5].PriceSeq, Is.EqualTo(202));
    }
}
