// <copyright file="CashShopCatalogInitializer.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.DataInitialization;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.Persistence;

/// <summary>
/// Inicializador do catálogo Cash Shop IBS no banco de dados PostgreSQL.
/// Processa arquivos parseados e persiste entidades no schema 'data'.
/// </summary>
public class CashShopCatalogInitializer
{
    /// <summary>
    /// Environment variable containing the directory with the three IBS catalog files.
    /// </summary>
    public const string CatalogPathEnvironmentVariable = "OPENMU_CASH_SHOP_CATALOG_PATH";

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
        GameConfiguration gameConfiguration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalogData);
        ArgumentNullException.ThrowIfNull(gameConfiguration);

        ValidateCatalogData(catalogData);
        this._logger.LogInformation(
            "Starting CashShop catalog initialization for configuration {ConfigurationId}.",
            gameConfiguration.GetId());

        using var context = this._persistenceContextProvider.CreateNewContext(gameConfiguration);

        var categoryMap = await this.InitializeCategoriesAsync(context, gameConfiguration, catalogData.Categories, cancellationToken).ConfigureAwait(false);
        var productMap = await this.InitializeProductsAsync(context, gameConfiguration, catalogData.Products, cancellationToken).ConfigureAwait(false);
        var packageMap = await this.InitializePackagesAsync(context, gameConfiguration, catalogData.Packages, categoryMap, cancellationToken).ConfigureAwait(false);
        await this.InitializePackageProductRelationsAsync(context, catalogData.PackageProductRelations, packageMap, productMap, cancellationToken).ConfigureAwait(false);

        // All validation and object construction happens before this single save. EF Core executes it as one unit of work.
        if (!await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The CashShop catalog could not be saved.");
        }

        this._logger.LogInformation(
            "CashShop catalog initialization completed: {CategoryCount} categories, {ProductCount} products, {PackageCount} packages.",
            categoryMap.Count,
            productMap.Count,
            packageMap.Count);
    }

    /// <summary>
    /// Persiste categorias e retorna mapeamento ProductDisplaySeq -> Guid Id.
    /// </summary>
    private async Task<Dictionary<int, CashShopCategory>> InitializeCategoriesAsync(
        IContext context,
        GameConfiguration gameConfiguration,
        List<CashShopCategory> categories,
        CancellationToken cancellationToken)
    {
        var existingCategories = (await context.GetAsync<CashShopCategory>(cancellationToken).ConfigureAwait(false)).ToList();
        var categoryMap = new Dictionary<int, CashShopCategory>();
        foreach (var existing in existingCategories)
        {
            if (!categoryMap.TryAdd(existing.ProductDisplaySeq, existing))
            {
                throw new InvalidDataException($"The configuration already contains duplicate category {existing.ProductDisplaySeq}.");
            }
        }

        foreach (var source in categories)
        {
            if (categoryMap.TryGetValue(source.ProductDisplaySeq, out var existing))
            {
                if (!CategoriesEqual(existing, source))
                {
                    this._logger.LogWarning("Preserving existing category {ProductDisplaySeq}; imported values differ.", source.ProductDisplaySeq);
                }

                continue;
            }

            var entity = context.CreateNew<CashShopCategory>();
            entity.Id = source.Id;
            entity.ProductDisplaySeq = source.ProductDisplaySeq;
            entity.IsRoot = source.IsRoot;
            entity.CategoryName = source.CategoryName;
            entity.DisplayOrder = source.DisplayOrder;
            entity.ParentProductDisplaySeq = source.ParentProductDisplaySeq;
            entity.EventFlag = source.EventFlag;
            entity.OpenFlag = source.OpenFlag;
            gameConfiguration.CashShopCategories.Add(entity);
            categoryMap.Add(source.ProductDisplaySeq, entity);
        }

        foreach (var category in categoryMap.Values)
        {
            if (category.ParentProductDisplaySeq != 0 &&
                categoryMap.TryGetValue(category.ParentProductDisplaySeq, out var parent))
            {
                category.ParentCategory = parent;
            }
        }

        return categoryMap;
    }

    /// <summary>
    /// Persiste produtos e retorna mapeamento PriceSeq -> Guid Id.
    /// </summary>
    private async Task<Dictionary<(int ProductSeq, int PriceSeq, int PropertySeq), CashShopProduct>> InitializeProductsAsync(
        IContext context,
        GameConfiguration gameConfiguration,
        List<CashShopProduct> products,
        CancellationToken cancellationToken)
    {
        var existingProducts = (await context.GetAsync<CashShopProduct>(cancellationToken).ConfigureAwait(false)).ToList();
        var productMap = new Dictionary<(int ProductSeq, int PriceSeq, int PropertySeq), CashShopProduct>();
        foreach (var existing in existingProducts)
        {
            var key = (existing.ProductSeq, existing.PriceSeq, existing.PropertySeq);
            if (!productMap.TryAdd(key, existing))
            {
                throw new InvalidDataException($"The configuration already contains duplicate product {key.ProductSeq}/{key.PriceSeq}/{key.PropertySeq}.");
            }
        }

        foreach (var source in products)
        {
            var key = (source.ProductSeq, source.PriceSeq, source.PropertySeq);
            if (productMap.TryGetValue(key, out var existing))
            {
                if (!ProductsEqual(existing, source))
                {
                    this._logger.LogWarning("Preserving existing product {ProductSeq}/{PriceSeq}/{PropertySeq}; imported values differ.", key.ProductSeq, key.PriceSeq, key.PropertySeq);
                }

                continue;
            }

            var entity = context.CreateNew<CashShopProduct>();
            entity.Id = source.Id;
            entity.ProductSeq = source.ProductSeq;
            entity.ProductName = source.ProductName;
            entity.PropertyName = source.PropertyName;
            entity.Value = source.Value;
            entity.UnitName = source.UnitName;
            entity.Price = source.Price;
            entity.PriceSeq = source.PriceSeq;
            entity.PropertyType = source.PropertyType;
            entity.MustFlag = source.MustFlag;
            entity.ViewOrder = source.ViewOrder;
            entity.DeleteFlag = source.DeleteFlag;
            entity.StorageGroup = source.StorageGroup;
            entity.ShareFlag = source.ShareFlag;
            entity.InGamePackageID = source.InGamePackageID;
            entity.PropertySeq = source.PropertySeq;
            entity.ProductType = source.ProductType;
            entity.UnitType = source.UnitType;
            gameConfiguration.CashShopProducts.Add(entity);
            productMap.Add(key, entity);
        }

        return productMap;
    }

    /// <summary>
    /// Persiste pacotes e retorna mapeamento PackageProductSeq -> Guid Id.
    /// Requer categoryMap para resolver FK CategoryId.
    /// </summary>
    private async Task<Dictionary<(int ProductDisplaySeq, int PackageProductSeq), CashShopPackage>> InitializePackagesAsync(
        IContext context,
        GameConfiguration gameConfiguration,
        List<CashShopPackage> packages,
        Dictionary<int, CashShopCategory> categoryMap,
        CancellationToken cancellationToken)
    {
        var existingPackages = (await context.GetAsync<CashShopPackage>(cancellationToken).ConfigureAwait(false)).ToList();
        var packageMap = new Dictionary<(int ProductDisplaySeq, int PackageProductSeq), CashShopPackage>();
        foreach (var existing in existingPackages)
        {
            var key = (existing.ProductDisplaySeq, existing.PackageProductSeq);
            if (!packageMap.TryAdd(key, existing))
            {
                throw new InvalidDataException($"The configuration already contains duplicate package {key.PackageProductSeq} in category {key.ProductDisplaySeq}.");
            }
        }

        foreach (var source in packages)
        {
            if (!categoryMap.TryGetValue(source.ProductDisplaySeq, out var category))
            {
                throw new InvalidDataException($"Package {source.PackageProductSeq} references unknown category {source.ProductDisplaySeq}.");
            }

            var key = (source.ProductDisplaySeq, source.PackageProductSeq);
            if (packageMap.TryGetValue(key, out var existing))
            {
                if (!PackagesEqual(existing, source))
                {
                    this._logger.LogWarning("Preserving existing package {PackageProductSeq} in category {ProductDisplaySeq}; imported values differ.", source.PackageProductSeq, source.ProductDisplaySeq);
                }

                existing.Category = category;
                existing.CategoryId = category.Id;
                continue;
            }

            var entity = context.CreateNew<CashShopPackage>();
            entity.Id = source.Id;
            entity.ProductDisplaySeq = source.ProductDisplaySeq;
            entity.ViewOrder = source.ViewOrder;
            entity.PackageProductSeq = source.PackageProductSeq;
            entity.PackageProductName = source.PackageProductName;
            entity.PackageProductType = source.PackageProductType;
            entity.Price = source.Price;
            entity.Description = source.Description;
            entity.Caution = source.Caution;
            entity.SalesFlag = source.SalesFlag;
            entity.GiftFlag = source.GiftFlag;
            entity.StartDate = source.StartDate;
            entity.EndDate = source.EndDate;
            entity.CapsuleFlag = source.CapsuleFlag;
            entity.CapsuleCount = source.CapsuleCount;
            entity.ProductCashName = source.ProductCashName;
            entity.PriceUnitName = source.PriceUnitName;
            entity.DeleteFlag = source.DeleteFlag;
            entity.EventFlag = source.EventFlag;
            entity.ProductAmount = source.ProductAmount;
            entity.ProductSeqList = source.ProductSeqList;
            entity.InGamePackageID = source.InGamePackageID;
            entity.ProductCashSeq = source.ProductCashSeq;
            entity.PriceCount = source.PriceCount;
            entity.PriceSeqList = source.PriceSeqList;
            entity.DeductMileageFlag = source.DeductMileageFlag;
            entity.CashType = source.CashType;
            entity.CashTypeFlag = source.CashTypeFlag;
            entity.Category = category;
            entity.CategoryId = category.Id;
            gameConfiguration.CashShopPackages.Add(entity);
            packageMap.Add(key, entity);
        }

        return packageMap;
    }

    /// <summary>
    /// Cria entidades de junção CashShopPackageProduct para relacionamento many-to-many.
    /// </summary>
    private async Task InitializePackageProductRelationsAsync(
        IContext context,
        List<CashShopCatalogParser.PackageProductRelation> relations,
        Dictionary<(int ProductDisplaySeq, int PackageProductSeq), CashShopPackage> packageMap,
        Dictionary<(int ProductSeq, int PriceSeq, int PropertySeq), CashShopProduct> productMap,
        CancellationToken cancellationToken)
    {
        // The existing relations are resolved from the navigation properties, not from
        // PackageId/ProductId of the data model type. The Entity Framework model declares those two
        // identifiers as nullable properties that shadow the non-nullable ones of
        // MUnique.OpenMU.DataModel.Entities.CashShopPackageProduct, and the repository projects the
        // row into that derived type without populating them. Reading them therefore yields
        // Guid.Empty for every existing row, the de-duplication below never matches, and a second
        // seed run tries to insert a relation whose (PackageId, ProductId) key is already tracked.
        // The navigations are the values that actually carry the identifiers.
        var existingRelations = new HashSet<(Guid PackageId, Guid ProductId)>();
        foreach (var package in packageMap.Values)
        {
            foreach (var relation in package.PackageProducts)
            {
                if (relation.Package is { } relatedPackage && relation.Product is { } relatedProduct)
                {
                    existingRelations.Add((relatedPackage.Id, relatedProduct.Id));
                }
            }
        }

        var nextOrder = packageMap.Values.ToDictionary(
            package => package.Id,
            package => package.PackageProducts.Select(product => product.Order).DefaultIfEmpty(-1).Max() + 1);

        foreach (var relation in relations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!packageMap.TryGetValue((relation.ProductDisplaySeq, relation.PackageProductSeq), out var package))
            {
                throw new InvalidDataException($"Relation references unknown package {relation.PackageProductSeq} in category {relation.ProductDisplaySeq}.");
            }

            var matchingProducts = productMap.Values
                .Where(product => product.ProductSeq == relation.ProductSeq && product.PriceSeq == relation.PriceSeq)
                .ToList();
            if (matchingProducts.Count == 0)
            {
                throw new InvalidDataException($"Relation references unknown product {relation.ProductSeq}/{relation.PriceSeq}.");
            }

            foreach (var product in matchingProducts)
            {
                if (!existingRelations.Add((package.Id, product.Id)))
                {
                    continue;
                }

                var junction = context.CreateNew<CashShopPackageProduct>();
                junction.PackageId = package.Id;
                junction.Package = package;
                junction.ProductId = product.Id;
                junction.Product = product;
                junction.Order = nextOrder[package.Id]++;
                package.PackageProducts.Add(junction);
                product.PackageProducts.Add(junction);
            }
        }
    }
    private static void ValidateCatalogData(CashShopCatalogParser.CatalogData catalogData)
    {
        var categoryKeys = new HashSet<int>();
        foreach (var category in catalogData.Categories)
        {
            if (!categoryKeys.Add(category.ProductDisplaySeq))
            {
                throw new InvalidDataException($"Duplicate category ProductDisplaySeq {category.ProductDisplaySeq}.");
            }
        }

        var packageKeys = new HashSet<(int ProductDisplaySeq, int PackageProductSeq)>();
        foreach (var package in catalogData.Packages)
        {
            if (!categoryKeys.Contains(package.ProductDisplaySeq))
            {
                throw new InvalidDataException($"Package {package.PackageProductSeq} references unknown category {package.ProductDisplaySeq}.");
            }

            if (!packageKeys.Add((package.ProductDisplaySeq, package.PackageProductSeq)))
            {
                throw new InvalidDataException($"Duplicate package {package.PackageProductSeq} in category {package.ProductDisplaySeq}.");
            }
        }

        var productKeys = new HashSet<(int ProductSeq, int PriceSeq, int PropertySeq)>();
        foreach (var product in catalogData.Products)
        {
            if (!productKeys.Add((product.ProductSeq, product.PriceSeq, product.PropertySeq)))
            {
                throw new InvalidDataException($"Duplicate product row {product.ProductSeq}/{product.PriceSeq}/{product.PropertySeq}.");
            }
        }

        foreach (var relation in catalogData.PackageProductRelations)
        {
            if (!packageKeys.Contains((relation.ProductDisplaySeq, relation.PackageProductSeq)))
            {
                throw new InvalidDataException($"Relation references unknown package {relation.PackageProductSeq} in category {relation.ProductDisplaySeq}.");
            }

            if (!catalogData.Products.Any(product => product.ProductSeq == relation.ProductSeq && product.PriceSeq == relation.PriceSeq))
            {
                throw new InvalidDataException($"Relation references unknown product {relation.ProductSeq}/{relation.PriceSeq}.");
            }
        }
    }

    private static bool CategoriesEqual(CashShopCategory left, CashShopCategory right) =>
        left.ProductDisplaySeq == right.ProductDisplaySeq &&
        left.CategoryName == right.CategoryName &&
        left.EventFlag == right.EventFlag &&
        left.OpenFlag == right.OpenFlag &&
        left.ParentProductDisplaySeq == right.ParentProductDisplaySeq &&
        left.DisplayOrder == right.DisplayOrder &&
        left.IsRoot == right.IsRoot;

    private static bool ProductsEqual(CashShopProduct left, CashShopProduct right) =>
        left.ProductSeq == right.ProductSeq &&
        left.ProductName == right.ProductName &&
        left.PropertyName == right.PropertyName &&
        left.Value == right.Value &&
        left.UnitName == right.UnitName &&
        left.Price == right.Price &&
        left.PriceSeq == right.PriceSeq &&
        left.PropertyType == right.PropertyType &&
        left.MustFlag == right.MustFlag &&
        left.ViewOrder == right.ViewOrder &&
        left.DeleteFlag == right.DeleteFlag &&
        left.StorageGroup == right.StorageGroup &&
        left.ShareFlag == right.ShareFlag &&
        left.InGamePackageID == right.InGamePackageID &&
        left.PropertySeq == right.PropertySeq &&
        left.ProductType == right.ProductType &&
        left.UnitType == right.UnitType;

    private static bool PackagesEqual(CashShopPackage left, CashShopPackage right) =>
        left.ProductDisplaySeq == right.ProductDisplaySeq &&
        left.ViewOrder == right.ViewOrder &&
        left.PackageProductSeq == right.PackageProductSeq &&
        left.PackageProductName == right.PackageProductName &&
        left.PackageProductType == right.PackageProductType &&
        left.Price == right.Price &&
        left.Description == right.Description &&
        left.Caution == right.Caution &&
        left.SalesFlag == right.SalesFlag &&
        left.GiftFlag == right.GiftFlag &&
        left.StartDate == right.StartDate &&
        left.EndDate == right.EndDate &&
        left.CapsuleFlag == right.CapsuleFlag &&
        left.CapsuleCount == right.CapsuleCount &&
        left.ProductCashName == right.ProductCashName &&
        left.PriceUnitName == right.PriceUnitName &&
        left.DeleteFlag == right.DeleteFlag &&
        left.EventFlag == right.EventFlag &&
        left.ProductAmount == right.ProductAmount &&
        left.ProductSeqList == right.ProductSeqList &&
        left.InGamePackageID == right.InGamePackageID &&
        left.ProductCashSeq == right.ProductCashSeq &&
        left.PriceCount == right.PriceCount &&
        left.PriceSeqList == right.PriceSeqList &&
        left.DeductMileageFlag == right.DeductMileageFlag &&
        left.CashType == right.CashType &&
        left.CashTypeFlag == right.CashTypeFlag;


}
