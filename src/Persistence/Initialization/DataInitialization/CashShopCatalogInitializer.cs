// <copyright file="CashShopCatalogInitializer.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.DataInitialization;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.Persistence;

/// <summary>
/// Inicializador do catálogo Cash Shop IBS no banco de dados PostgreSQL.
/// Processa arquivos parseados e persiste entidades no schema 'data'.
/// </summary>
public class CashShopCatalogInitializer
{
    private readonly IPersistenceContextProvider _persistenceContextProvider;
    private readonly ILogger<CashShopCatalogInitializer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CashShopCatalogInitializer"/> class.
    /// </summary>
    /// <param name="persistenceContextProvider">Provedor de contexto de persistência.</param>
    /// <param name="logger">Logger instance.</param>
    public CashShopCatalogInitializer(
        IPersistenceContextProvider persistenceContextProvider,
        ILogger<CashShopCatalogInitializer> logger)
    {
        this._persistenceContextProvider = persistenceContextProvider ?? throw new ArgumentNullException(nameof(persistenceContextProvider));
        this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Inicializa o catálogo completo a partir dos dados parseados.
    /// </summary>
    /// <param name="catalogData">Dados parseados do catálogo IBS.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    public async Task InitializeAsync(
        CashShopCatalogParser.CatalogData catalogData,
        CancellationToken cancellationToken = default)
    {
        this._logger.LogInformation("Starting CashShop catalog initialization...");

        using var context = this._persistenceContextProvider.CreateNewContext();

        // Passo 1: Persistir Categorias
        var categoryMap = await this.InitializeCategoriesAsync(context, catalogData.Categories, cancellationToken).ConfigureAwait(false);
        this._logger.LogInformation("Persisted {Count} categories.", categoryMap.Count);

        // Passo 2: Persistir Products
        var productMap = await this.InitializeProductsAsync(context, catalogData.Products, cancellationToken).ConfigureAwait(false);
        this._logger.LogInformation("Persisted {Count} products.", productMap.Count);

        // Passo 3: Persistir Packages (requer categoryMap para FK)
        var packageMap = await this.InitializePackagesAsync(context, catalogData.Packages, categoryMap, cancellationToken).ConfigureAwait(false);
        this._logger.LogInformation("Persisted {Count} packages.", packageMap.Count);

        // Passo 4: Criar relações many-to-many via CashShopPackageProduct
        await this.InitializePackageProductRelationsAsync(
            context,
            catalogData.PackageProductRelations,
            packageMap,
            productMap,
            cancellationToken).ConfigureAwait(false);
        this._logger.LogInformation("Created {Count} package-product relations.", catalogData.PackageProductRelations.Count);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        this._logger.LogInformation("CashShop catalog initialization completed successfully.");
    }

    /// <summary>
    /// Persiste categorias e retorna mapeamento ProductDisplaySeq -> Guid Id.
    /// </summary>
    private async Task<Dictionary<int, Guid>> InitializeCategoriesAsync(
        IContext context,
        List<CashShopCategory> categories,
        CancellationToken cancellationToken)
    {
        var categoryMap = new Dictionary<int, Guid>();

        foreach (var category in categories)
        {
            var entity = context.CreateNew<CashShopCategory>();
            entity.Id = Guid.NewGuid();
            entity.ProductDisplaySeq = category.ProductDisplaySeq;
            entity.IsRoot = category.IsRoot;
            entity.CategoryName = category.CategoryName;
            entity.DisplayOrder = category.DisplayOrder;
            entity.ParentProductDisplaySeq = category.ParentProductDisplaySeq;
            entity.EventFlag = category.EventFlag;
            entity.OpenFlag = category.OpenFlag;

            categoryMap[category.ProductDisplaySeq] = entity.Id;
        }

        // Após criar todas as categorias, resolver navegação ParentCategory / SubCategories
        var allCategories = await context.GetAsync<CashShopCategory>(cancellationToken).ConfigureAwait(false);
        var categoryLookup = allCategories.ToDictionary(c => c.ProductDisplaySeq);

        foreach (var cat in allCategories)
        {
            if (cat.ParentProductDisplaySeq != 0 &&
                categoryLookup.TryGetValue(cat.ParentProductDisplaySeq, out var parent))
            {
                cat.ParentCategory = parent;
            }
        }

        return categoryMap;
    }

    /// <summary>
    /// Persiste produtos e retorna mapeamento PriceSeq -> Guid Id.
    /// </summary>
    private async Task<Dictionary<int, Guid>> InitializeProductsAsync(
        IContext context,
        List<CashShopProduct> products,
        CancellationToken cancellationToken)
    {
        var productMap = new Dictionary<int, Guid>();

        foreach (var product in products)
        {
            var entity = context.CreateNew<CashShopProduct>();
            entity.Id = Guid.NewGuid();
            entity.ProductSeq = product.ProductSeq;
            entity.ProductName = product.ProductName;
            entity.PropertyName = product.PropertyName;
            entity.Value = product.Value;
            entity.UnitName = product.UnitName;
            entity.Price = product.Price;
            entity.PriceSeq = product.PriceSeq;
            entity.PropertyType = product.PropertyType;
            entity.MustFlag = product.MustFlag;
            entity.ViewOrder = product.ViewOrder;
            entity.DeleteFlag = product.DeleteFlag;
            entity.StorageGroup = product.StorageGroup;
            entity.ShareFlag = product.ShareFlag;
            entity.InGamePackageID = product.InGamePackageID;
            entity.PropertySeq = product.PropertySeq;
            entity.ProductType = product.ProductType;
            entity.UnitType = product.UnitType;

            productMap[product.PriceSeq] = entity.Id;
        }

        return productMap;
    }

    /// <summary>
    /// Persiste pacotes e retorna mapeamento PackageProductSeq -> Guid Id.
    /// Requer categoryMap para resolver FK CategoryId.
    /// </summary>
    private async Task<Dictionary<int, Guid>> InitializePackagesAsync(
        IContext context,
        List<CashShopPackage> packages,
        Dictionary<int, Guid> categoryMap,
        CancellationToken cancellationToken)
    {
        var packageMap = new Dictionary<int, Guid>();
        var allCategories = await context.GetAsync<CashShopCategory>(cancellationToken).ConfigureAwait(false);
        var categoryLookup = allCategories.ToDictionary(c => c.ProductDisplaySeq);

        foreach (var package in packages)
        {
            var entity = context.CreateNew<CashShopPackage>();
            entity.Id = Guid.NewGuid();
            entity.ProductDisplaySeq = package.ProductDisplaySeq;
            entity.ViewOrder = package.ViewOrder;
            entity.PackageProductSeq = package.PackageProductSeq;
            entity.PackageProductName = package.PackageProductName;
            entity.PackageProductType = package.PackageProductType;
            entity.Price = package.Price;
            entity.Description = package.Description;
            entity.Caution = package.Caution;
            entity.SalesFlag = package.SalesFlag;
            entity.GiftFlag = package.GiftFlag;
            entity.StartDate = package.StartDate;
            entity.EndDate = package.EndDate;
            entity.CapsuleFlag = package.CapsuleFlag;
            entity.CapsuleCount = package.CapsuleCount;
            entity.ProductCashName = package.ProductCashName;
            entity.PriceUnitName = package.PriceUnitName;
            entity.DeleteFlag = package.DeleteFlag;
            entity.EventFlag = package.EventFlag;
            entity.ProductAmount = package.ProductAmount;
            entity.ProductSeqList = package.ProductSeqList;
            entity.InGamePackageID = package.InGamePackageID;
            entity.ProductCashSeq = package.ProductCashSeq;
            entity.PriceCount = package.PriceCount;
            entity.PriceSeqList = package.PriceSeqList;
            entity.DeductMileageFlag = package.DeductMileageFlag;
            entity.CashType = package.CashType;
            entity.CashTypeFlag = package.CashTypeFlag;

            // Resolver FK Category
            if (categoryLookup.TryGetValue(package.ProductDisplaySeq, out var category))
            {
                entity.Category = category;
                entity.CategoryId = category.Id;
            }
            else
            {
                this._logger.LogWarning(
                    "Package {PackageProductSeq} references unknown ProductDisplaySeq {ProductDisplaySeq}.",
                    package.PackageProductSeq,
                    package.ProductDisplaySeq);
            }

            packageMap[package.PackageProductSeq] = entity.Id;
        }

        return packageMap;
    }

    /// <summary>
    /// Cria entidades de junção CashShopPackageProduct para relacionamento many-to-many.
    /// </summary>
    private async Task InitializePackageProductRelationsAsync(
        IContext context,
        List<CashShopCatalogParser.PackageProductRelation> relations,
        Dictionary<int, Guid> packageMap,
        Dictionary<int, Guid> productMap,
        CancellationToken cancellationToken)
    {
        var allPackages = await context.GetAsync<CashShopPackage>(cancellationToken).ConfigureAwait(false);
        var packageLookup = allPackages.ToDictionary(p => p.PackageProductSeq);

        var allProducts = await context.GetAsync<CashShopProduct>(cancellationToken).ConfigureAwait(false);
        var productLookup = allProducts.ToDictionary(p => p.PriceSeq);

        foreach (var relation in relations)
        {
            if (!packageLookup.TryGetValue(relation.PackageProductSeq, out var package))
            {
                this._logger.LogWarning(
                    "Relation references unknown PackageProductSeq {PackageProductSeq}.",
                    relation.PackageProductSeq);
                continue;
            }

            if (!productLookup.TryGetValue(relation.PriceSeq, out var product))
            {
                this._logger.LogWarning(
                    "Relation references unknown PriceSeq {PriceSeq}.",
                    relation.PriceSeq);
                continue;
            }

            var junction = context.CreateNew<CashShopPackageProduct>();
            junction.PackageId = package.Id;
            junction.Package = package;
            junction.ProductId = product.Id;
            junction.Product = product;
        }
    }
}
