// <copyright file="CashShopCatalogService.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.CashShop;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.Persistence;

/// <summary>
/// Implementação do serviço de catálogo da Cash Shop com cache em memória de alta performance.
/// Utiliza SemaphoreSlim para garantir single-flight load e IMemoryCache com expiração absoluta.
/// </summary>
public class CashShopCatalogService : ICashShopCatalogService, IDisposable
{
    private const string CacheKeyAllCategories = "CashShop_AllCategories";
    private const string CacheKeyAllPackages = "CashShop_AllPackages";
    private const string CacheKeyAllProducts = "CashShop_AllProducts";

    private static readonly TimeSpan DefaultCacheDuration = TimeSpan.FromHours(1);

    private readonly IPersistenceContextProvider _persistenceContextProvider;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<CashShopCatalogService> _logger;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="CashShopCatalogService"/> class.
    /// </summary>
    /// <param name="persistenceContextProvider">Provedor de contexto de persistência.</param>
    /// <param name="memoryCache">Instância de cache em memória.</param>
    /// <param name="logger">Logger.</param>
    public CashShopCatalogService(
        IPersistenceContextProvider persistenceContextProvider,
        IMemoryCache memoryCache,
        ILogger<CashShopCatalogService> logger)
    {
        this._persistenceContextProvider = persistenceContextProvider ?? throw new ArgumentNullException(nameof(persistenceContextProvider));
        this._memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CashShopCategory>> GetRootCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var allCategories = await this.GetAllCategoriesCachedAsync(cancellationToken).ConfigureAwait(false);
        return allCategories
            .Where(c => c.IsRoot)
            .OrderBy(c => c.DisplayOrder)
            .ToList();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CashShopCategory>> GetSubCategoriesAsync(int parentProductDisplaySeq, CancellationToken cancellationToken = default)
    {
        var allCategories = await this.GetAllCategoriesCachedAsync(cancellationToken).ConfigureAwait(false);
        return allCategories
            .Where(c => !c.IsRoot && c.ParentProductDisplaySeq == parentProductDisplaySeq)
            .OrderBy(c => c.DisplayOrder)
            .ToList();
    }

    /// <inheritdoc />
    public async ValueTask<CashShopCategory?> GetCategoryByDisplaySeqAsync(int productDisplaySeq, CancellationToken cancellationToken = default)
    {
        var allCategories = await this.GetAllCategoriesCachedAsync(cancellationToken).ConfigureAwait(false);
        return allCategories.FirstOrDefault(c => c.ProductDisplaySeq == productDisplaySeq);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CashShopPackage>> GetPackagesByCategoryAsync(int productDisplaySeq, CancellationToken cancellationToken = default)
    {
        var allPackages = await this.GetAllPackagesCachedAsync(cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        return allPackages
            .Where(p => p.ProductDisplaySeq == productDisplaySeq &&
                        p.DeleteFlag == 0 &&
                        (p.StartDate == default || p.StartDate <= now) &&
                        (p.EndDate == default || p.EndDate >= now))
            .OrderBy(p => p.ViewOrder)
            .ToList();
    }

    /// <inheritdoc />
    public async ValueTask<CashShopPackage?> GetPackageBySeqAsync(int packageProductSeq, CancellationToken cancellationToken = default)
    {
        var allPackages = await this.GetAllPackagesCachedAsync(cancellationToken).ConfigureAwait(false);
        return allPackages.FirstOrDefault(p => p.PackageProductSeq == packageProductSeq);
    }

    /// <inheritdoc />
    public async ValueTask<CashShopProduct?> GetProductByPriceSeqAsync(int priceSeq, CancellationToken cancellationToken = default)
    {
        var allProducts = await this.GetAllProductsCachedAsync(cancellationToken).ConfigureAwait(false);
        return allProducts.FirstOrDefault(p => p.PriceSeq == priceSeq);
    }

    /// <inheritdoc />
    public async Task InvalidateAndReloadCacheAsync(CancellationToken cancellationToken = default)
    {
        await this._reloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            this._memoryCache.Remove(CacheKeyAllCategories);
            this._memoryCache.Remove(CacheKeyAllPackages);
            this._memoryCache.Remove(CacheKeyAllProducts);

            this._logger.LogInformation("CashShop catalog cache invalidated. Reloading fresh snapshot from database...");
            await this.LoadCatalogSnapshotFromDbAsync(cancellationToken).ConfigureAwait(false);
            this._logger.LogInformation("CashShop catalog snapshot reloaded successfully.");
        }
        finally
        {
            this._reloadLock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases unmanaged and - optionally - managed resources.
    /// </summary>
    /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!this._disposed)
        {
            if (disposing)
            {
                this._reloadLock.Dispose();
            }

            this._disposed = true;
        }
    }

    private async ValueTask<IReadOnlyList<CashShopCategory>> GetAllCategoriesCachedAsync(CancellationToken cancellationToken)
    {
        if (this._memoryCache.TryGetValue(CacheKeyAllCategories, out IReadOnlyList<CashShopCategory>? cached) && cached != null)
        {
            return cached;
        }

        await this.EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);
        return this._memoryCache.Get<IReadOnlyList<CashShopCategory>>(CacheKeyAllCategories) ?? Array.Empty<CashShopCategory>();
    }

    private async ValueTask<IReadOnlyList<CashShopPackage>> GetAllPackagesCachedAsync(CancellationToken cancellationToken)
    {
        if (this._memoryCache.TryGetValue(CacheKeyAllPackages, out IReadOnlyList<CashShopPackage>? cached) && cached != null)
        {
            return cached;
        }

        await this.EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);
        return this._memoryCache.Get<IReadOnlyList<CashShopPackage>>(CacheKeyAllPackages) ?? Array.Empty<CashShopPackage>();
    }

    private async ValueTask<IReadOnlyList<CashShopProduct>> GetAllProductsCachedAsync(CancellationToken cancellationToken)
    {
        if (this._memoryCache.TryGetValue(CacheKeyAllProducts, out IReadOnlyList<CashShopProduct>? cached) && cached != null)
        {
            return cached;
        }

        await this.EnsureCacheLoadedAsync(cancellationToken).ConfigureAwait(false);
        return this._memoryCache.Get<IReadOnlyList<CashShopProduct>>(CacheKeyAllProducts) ?? Array.Empty<CashShopProduct>();
    }

    private async Task EnsureCacheLoadedAsync(CancellationToken cancellationToken)
    {
        await this._reloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (this._memoryCache.TryGetValue(CacheKeyAllCategories, out _) &&
                this._memoryCache.TryGetValue(CacheKeyAllPackages, out _) &&
                this._memoryCache.TryGetValue(CacheKeyAllProducts, out _))
            {
                return;
            }

            await this.LoadCatalogSnapshotFromDbAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            this._reloadLock.Release();
        }
    }

    private async Task LoadCatalogSnapshotFromDbAsync(CancellationToken cancellationToken)
    {
        using var context = this._persistenceContextProvider.CreateNewContext();

        var categories = (await context.GetAsync<CashShopCategory>(cancellationToken).ConfigureAwait(false)).ToList();
        var packages = (await context.GetAsync<CashShopPackage>(cancellationToken).ConfigureAwait(false)).ToList();
        var products = (await context.GetAsync<CashShopProduct>(cancellationToken).ConfigureAwait(false)).ToList();

        var cacheEntryOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(DefaultCacheDuration)
            .SetPriority(CacheItemPriority.High);

        this._memoryCache.Set(CacheKeyAllCategories, (IReadOnlyList<CashShopCategory>)categories, cacheEntryOptions);
        this._memoryCache.Set(CacheKeyAllPackages, (IReadOnlyList<CashShopPackage>)packages, cacheEntryOptions);
        this._memoryCache.Set(CacheKeyAllProducts, (IReadOnlyList<CashShopProduct>)products, cacheEntryOptions);

        this._logger.LogInformation(
            "CashShop catalog snapshot loaded: {CategoriesCount} categories, {PackagesCount} packages, {ProductsCount} products.",
            categories.Count,
            packages.Count,
            products.Count);
    }
}
