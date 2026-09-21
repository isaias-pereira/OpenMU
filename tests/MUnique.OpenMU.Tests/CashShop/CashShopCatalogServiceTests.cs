// <copyright file="CashShopCatalogServiceTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests.CashShop;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.CashShop;
using MUnique.OpenMU.Persistence;
using NUnit.Framework;

/// <summary>
/// Testes unitários para <see cref="CashShopCatalogService"/>.
/// Valida o comportamento de cache em memória e thread-safety.
/// </summary>
[TestFixture]
public class CashShopCatalogServiceTests
{
    private Mock<IPersistenceContextProvider> _contextProviderMock = null!;
    private Mock<IContext> _contextMock = null!;
    private IMemoryCache _memoryCache = null!;
    private CashShopCatalogService _service = null!;

    [SetUp]
    public void SetUp()
    {
        this._contextProviderMock = new Mock<IPersistenceContextProvider>();
        this._contextMock = new Mock<IContext>();
        this._contextProviderMock.Setup(p => p.CreateNewContext()).Returns(this._contextMock.Object);

        this._memoryCache = new MemoryCache(new MemoryCacheOptions());
        var loggerMock = new Mock<ILogger<CashShopCatalogService>>();
        this._service = new CashShopCatalogService(
            this._contextProviderMock.Object,
            this._memoryCache,
            loggerMock.Object);
    }

    [TearDown]
    public void TearDown()
    {
        this._service.Dispose();
        this._memoryCache.Dispose();
    }

    [Test]
    public async Task GetRootCategoriesAsync_LoadsFromDatabaseOnFirstCall()
    {
        // Arrange
        var categories = new List<CashShopCategory>
        {
            new() { Id = Guid.NewGuid(), ProductDisplaySeq = 1, IsRoot = true, CategoryName = "Root 1", DisplayOrder = 0 },
            new() { Id = Guid.NewGuid(), ProductDisplaySeq = 2, IsRoot = true, CategoryName = "Root 2", DisplayOrder = 1 },
            new() { Id = Guid.NewGuid(), ProductDisplaySeq = 3, IsRoot = false, CategoryName = "Sub 1", DisplayOrder = 0, ParentProductDisplaySeq = 1 },
        };

        this._contextMock.Setup(c => c.GetAsync<CashShopCategory>(It.IsAny<CancellationToken>()))
            .ReturnsAsync(categories);
        this._contextMock.Setup(c => c.GetAsync<CashShopPackage>(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CashShopPackage>());
        this._contextMock.Setup(c => c.GetAsync<CashShopProduct>(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CashShopProduct>());

        // Act
        var result = await this._service.GetRootCategoriesAsync();

        // Assert
        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result[0].CategoryName, Is.EqualTo("Root 1"));
        Assert.That(result[1].CategoryName, Is.EqualTo("Root 2"));

        // Verifica que o contexto foi chamado uma vez
        this._contextMock.Verify(c => c.GetAsync<CashShopCategory>(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetRootCategoriesAsync_UsesCacheOnSecondCall()
    {
        // Arrange
        var categories = new List<CashShopCategory>
        {
            new() { Id = Guid.NewGuid(), ProductDisplaySeq = 1, IsRoot = true, CategoryName = "Root 1", DisplayOrder = 0 },
        };

        this._contextMock.Setup(c => c.GetAsync<CashShopCategory>(It.IsAny<CancellationToken>()))
            .ReturnsAsync(categories);
        this._contextMock.Setup(c => c.GetAsync<CashShopPackage>(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CashShopPackage>());
        this._contextMock.Setup(c => c.GetAsync<CashShopProduct>(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CashShopProduct>());

        // Act - primeira chamada carrega do banco
        await this._service.GetRootCategoriesAsync();

        // Act - segunda chamada usa o cache
        var result = await this._service.GetRootCategoriesAsync();

        // Assert
        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].CategoryName, Is.EqualTo("Root 1"));

        // Verifica que o contexto foi chamado apenas uma vez (primeira chamada)
        this._contextMock.Verify(c => c.GetAsync<CashShopCategory>(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task GetSubCategoriesAsync_FiltersCorrectly()
    {
        // Arrange
        var categories = new List<CashShopCategory>
        {
            new() { Id = Guid.NewGuid(), ProductDisplaySeq = 1, IsRoot = true, CategoryName = "Root", DisplayOrder = 0 },
            new() { Id = Guid.NewGuid(), ProductDisplaySeq = 2, IsRoot = false, CategoryName = "Sub 1", DisplayOrder = 0, ParentProductDisplaySeq = 1 },
            new() { Id = Guid.NewGuid(), ProductDisplaySeq = 3, IsRoot = false, CategoryName = "Sub 2", DisplayOrder = 1, ParentProductDisplaySeq = 1 },
            new() { Id = Guid.NewGuid(), ProductDisplaySeq = 4, IsRoot = false, CategoryName = "Other Sub", DisplayOrder = 0, ParentProductDisplaySeq = 99 },
        };

        this._contextMock.Setup(c => c.GetAsync<CashShopCategory>(It.IsAny<CancellationToken>()))
            .ReturnsAsync(categories);
        this._contextMock.Setup(c => c.GetAsync<CashShopPackage>(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CashShopPackage>());
        this._contextMock.Setup(c => c.GetAsync<CashShopProduct>(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CashShopProduct>());

        // Act
        var result = await this._service.GetSubCategoriesAsync(1);

        // Assert
        Assert.That(result.Count, Is.EqualTo(2));
        Assert.That(result[0].CategoryName, Is.EqualTo("Sub 1"));
        Assert.That(result[1].CategoryName, Is.EqualTo("Sub 2"));
        Assert.That(result.All(c => c.ParentProductDisplaySeq == 1), Is.True);
    }

    [Test]
    public async Task GetPackagesByCategoryAsync_FiltersActivePackagesOnly()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var packages = new List<CashShopPackage>
        {
            new() { Id = Guid.NewGuid(), PackageProductSeq = 1, ProductDisplaySeq = 100, PackageProductName = "Active", DeleteFlag = 0, StartDate = now.AddDays(-1), EndDate = now.AddDays(1), ViewOrder = 0 },
            new() { Id = Guid.NewGuid(), PackageProductSeq = 2, ProductDisplaySeq = 100, PackageProductName = "Deleted", DeleteFlag = 1, StartDate = now.AddDays(-1), EndDate = now.AddDays(1), ViewOrder = 1 },
            new() { Id = Guid.NewGuid(), PackageProductSeq = 3, ProductDisplaySeq = 100, PackageProductName = "Not Started", DeleteFlag = 0, StartDate = now.AddDays(1), EndDate = now.AddDays(2), ViewOrder = 2 },
            new() { Id = Guid.NewGuid(), PackageProductSeq = 4, ProductDisplaySeq = 100, PackageProductName = "Expired", DeleteFlag = 0, StartDate = now.AddDays(-2), EndDate = now.AddDays(-1), ViewOrder = 3 },
            new() { Id = Guid.NewGuid(), PackageProductSeq = 5, ProductDisplaySeq = 200, PackageProductName = "Other Category", DeleteFlag = 0, StartDate = now.AddDays(-1), EndDate = now.AddDays(1), ViewOrder = 4 },
        };

        this._contextMock.Setup(c => c.GetAsync<CashShopCategory>(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CashShopCategory>());
        this._contextMock.Setup(c => c.GetAsync<CashShopPackage>(It.IsAny<CancellationToken>()))
            .ReturnsAsync(packages);
        this._contextMock.Setup(c => c.GetAsync<CashShopProduct>(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CashShopProduct>());

        // Act
        var result = await this._service.GetPackagesByCategoryAsync(100);

        // Assert - apenas o pacote "Active" deve ser retornado
        Assert.That(result.Count, Is.EqualTo(1));
        Assert.That(result[0].PackageProductName, Is.EqualTo("Active"));
        Assert.That(result[0].DeleteFlag, Is.EqualTo(0));
    }

    [Test]
    public async Task GetPackageBySeqAsync_ReturnsCorrectPackage()
    {
        // Arrange
        var packages = new List<CashShopPackage>
        {
            new() { Id = Guid.NewGuid(), PackageProductSeq = 100, PackageProductName = "Package 1", ProductDisplaySeq = 1, DeleteFlag = 0, ViewOrder = 0 },
            new() { Id = Guid.NewGuid(), PackageProductSeq = 200, PackageProductName = "Package 2", ProductDisplaySeq = 1, DeleteFlag = 0, ViewOrder = 1 },
        };

        this._contextMock.Setup(c => c.GetAsync<CashShopCategory>(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CashShopCategory>());
        this._contextMock.Setup(c => c.GetAsync<CashShopPackage>(It.IsAny<CancellationToken>()))
            .ReturnsAsync(packages);
        this._contextMock.Setup(c => c.GetAsync<CashShopProduct>(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CashShopProduct>());

        // Act
        var result = await this._service.GetPackageBySeqAsync(200);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.PackageProductName, Is.EqualTo("Package 2"));
        Assert.That(result.PackageProductSeq, Is.EqualTo(200));
    }

    [Test]
    public async Task GetProductByPriceSeqAsync_ReturnsCorrectProduct()
    {
        // Arrange
        var products = new List<CashShopProduct>
        {
            new() { Id = Guid.NewGuid(), PriceSeq = 1001, ProductName = "Product 1", ProductSeq = 1, DeleteFlag = 0 },
            new() { Id = Guid.NewGuid(), PriceSeq = 1002, ProductName = "Product 2", ProductSeq = 2, DeleteFlag = 0 },
        };

        this._contextMock.Setup(c => c.GetAsync<CashShopCategory>(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CashShopCategory>());
        this._contextMock.Setup(c => c.GetAsync<CashShopPackage>(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CashShopPackage>());
        this._contextMock.Setup(c => c.GetAsync<CashShopProduct>(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        // Act
        var result = await this._service.GetProductByPriceSeqAsync(1002);

        // Assert
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.ProductName, Is.EqualTo("Product 2"));
        Assert.That(result.PriceSeq, Is.EqualTo(1002));
    }

    [Test]
    public async Task InvalidateAndReloadCacheAsync_ClearsAndReloadsCache()
    {
        // Arrange
        var initialCategories = new List<CashShopCategory>
        {
            new() { Id = Guid.NewGuid(), ProductDisplaySeq = 1, IsRoot = true, CategoryName = "Initial", DisplayOrder = 0 },
        };

        var updatedCategories = new List<CashShopCategory>
        {
            new() { Id = Guid.NewGuid(), ProductDisplaySeq = 1, IsRoot = true, CategoryName = "Updated", DisplayOrder = 0 },
        };

        this._contextMock.SetupSequence(c => c.GetAsync<CashShopCategory>(It.IsAny<CancellationToken>()))
            .ReturnsAsync(initialCategories)
            .ReturnsAsync(updatedCategories);
        this._contextMock.Setup(c => c.GetAsync<CashShopPackage>(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CashShopPackage>());
        this._contextMock.Setup(c => c.GetAsync<CashShopProduct>(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CashShopProduct>());

        // Act - carrega cache inicial
        var result1 = await this._service.GetRootCategoriesAsync();
        Assert.That(result1[0].CategoryName, Is.EqualTo("Initial"));

        // Act - invalida e recarrega
        await this._service.InvalidateAndReloadCacheAsync();

        // Act - lê novamente após invalidação
        var result2 = await this._service.GetRootCategoriesAsync();

        // Assert - deve ter recarregado com dados atualizados
        Assert.That(result2[0].CategoryName, Is.EqualTo("Updated"));

        // Verifica que o contexto foi chamado duas vezes (carga inicial + reload)
        this._contextMock.Verify(c => c.GetAsync<CashShopCategory>(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public async Task ConcurrentCalls_LoadDatabaseOnlyOnce()
    {
        // Arrange
        var categories = new List<CashShopCategory>
        {
            new() { Id = Guid.NewGuid(), ProductDisplaySeq = 1, IsRoot = true, CategoryName = "Root", DisplayOrder = 0 },
        };

        this._contextMock.Setup(c => c.GetAsync<CashShopCategory>(It.IsAny<CancellationToken>()))
            .ReturnsAsync(categories);
        this._contextMock.Setup(c => c.GetAsync<CashShopPackage>(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CashShopPackage>());
        this._contextMock.Setup(c => c.GetAsync<CashShopProduct>(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CashShopProduct>());

        // Act - 10 chamadas concorrentes
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => this._service.GetRootCategoriesAsync().AsTask())
            .ToArray();

        var results = await Task.WhenAll(tasks);

        // Assert - todas retornam o mesmo resultado
        Assert.That(results.All(r => r.Count == 1), Is.True);
        Assert.That(results.All(r => r[0].CategoryName == "Root"), Is.True);

        // Verifica que o banco foi consultado apenas uma vez (single-flight load)
        this._contextMock.Verify(c => c.GetAsync<CashShopCategory>(It.IsAny<CancellationToken>()), Times.Once);
    }
}
