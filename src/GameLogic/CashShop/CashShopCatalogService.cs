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
using MUnique.OpenMU.DataModel.Configuration;
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
    private const int ActivePackageDeleteFlag = 181;

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
    public async ValueTask<IReadOnlyList<CashShopCategory>> GetRootCategoriesAsync(GameConfiguration gameConfiguration, CancellationToken cancellationToken = default)
    {
        var allCategories = await this.GetAllCategoriesCachedAsync(gameConfiguration, cancellationToken).ConfigureAwait(false);
        return allCategories
            .Where(c => c.IsRoot)
            .OrderBy(c => c.DisplayOrder)
            .ToList();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CashShopCategory>> GetSubCategoriesAsync(int parentProductDisplaySeq, GameConfiguration gameConfiguration, CancellationToken cancellationToken = default)
    {
        var allCategories = await this.GetAllCategoriesCachedAsync(gameConfiguration, cancellationToken).ConfigureAwait(false);
        return allCategories
            .Where(c => !c.IsRoot && c.ParentProductDisplaySeq == parentProductDisplaySeq)
            .OrderBy(c => c.DisplayOrder)
            .ToList();
    }

    /// <inheritdoc />
    public async ValueTask<CashShopCategory?> GetCategoryByDisplaySeqAsync(int productDisplaySeq, GameConfiguration gameConfiguration, CancellationToken cancellationToken = default)
    {
        var allCategories = await this.GetAllCategoriesCachedAsync(gameConfiguration, cancellationToken).ConfigureAwait(false);
        return allCategories.FirstOrDefault(c => c.ProductDisplaySeq == productDisplaySeq);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CashShopPackage>> GetPackagesByCategoryAsync(int productDisplaySeq, GameConfiguration gameConfiguration, CancellationToken cancellationToken = default)
    {
        var allPackages = await this.GetAllPackagesCachedAsync(gameConfiguration, cancellationToken).ConfigureAwait(false);
        var now = DateTime.UtcNow;
        return allPackages
            .Where(p => p.ProductDisplaySeq == productDisplaySeq &&
                        p.DeleteFlag == ActivePackageDeleteFlag &&
                        (p.StartDate == default || p.StartDate <= now) &&
                        (p.EndDate == default || p.EndDate >= now))
            .OrderBy(p => p.ViewOrder)
            .ToList();
    }

    /// <inheritdoc />
    public async ValueTask<CashShopPackage?> GetPackageBySeqAsync(int productDisplaySeq, int packageProductSeq, GameConfiguration gameConfiguration, CancellationToken cancellationToken = default)
    {
        var allPackages = await this.GetAllPackagesCachedAsync(gameConfiguration, cancellationToken).ConfigureAwait(false);
        return allPackages.FirstOrDefault(p => p.ProductDisplaySeq == productDisplaySeq && p.PackageProductSeq == packageProductSeq);
    }

    /// <inheritdoc />
    public async ValueTask<CashShopPackage?> GetPackageBySeqAsync(int packageProductSeq, GameConfiguration gameConfiguration, CancellationToken cancellationToken = default)
    {
        var allPackages = await this.GetAllPackagesCachedAsync(gameConfiguration, cancellationToken).ConfigureAwait(false);
        return allPackages.FirstOrDefault(p => p.PackageProductSeq == packageProductSeq);
    }

    /// <inheritdoc />
    public async ValueTask<CashShopProduct?> GetProductByPriceSeqAsync(int productSeq, int priceSeq, int propertySeq, GameConfiguration gameConfiguration, CancellationToken cancellationToken = default)
    {
        var allProducts = await this.GetAllProductsCachedAsync(gameConfiguration, cancellationToken).ConfigureAwait(false);
        return allProducts.FirstOrDefault(p => p.ProductSeq == productSeq && p.PriceSeq == priceSeq && p.PropertySeq == propertySeq);
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CashShopProduct>> GetProductsByPriceSeqAsync(int priceSeq, GameConfiguration gameConfiguration, CancellationToken cancellationToken = default)
    {
        var allProducts = await this.GetAllProductsCachedAsync(gameConfiguration, cancellationToken).ConfigureAwait(false);
        return allProducts.Where(p => p.PriceSeq == priceSeq).ToList();
    }

    /// <inheritdoc />
    public async ValueTask<IReadOnlyList<CashShopProduct>> GetPackageProductsByPriceSeqAsync(
        int[] productSequences,
        int priceSeq,
        GameConfiguration gameConfiguration,
        CancellationToken cancellationToken = default)
    {
        if (productSequences is null || productSequences.Length == 0)
        {
            return Array.Empty<CashShopProduct>();
        }

        var allProducts = await this.GetAllProductsCachedAsync(gameConfiguration, cancellationToken).ConfigureAwait(false);
        var wanted = productSequences.ToHashSet();
        return allProducts.Where(p => p.PriceSeq == priceSeq && wanted.Contains(p.ProductSeq)).ToList();
    }

    /// <inheritdoc />
    public async Task InvalidateAndReloadCacheAsync(GameConfiguration gameConfiguration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gameConfiguration);
        await this._reloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var categoryCacheKey = GetCacheKey(CacheKeyAllCategories, gameConfiguration);
            var packageCacheKey = GetCacheKey(CacheKeyAllPackages, gameConfiguration);
            var productCacheKey = GetCacheKey(CacheKeyAllProducts, gameConfiguration);
            this._memoryCache.Remove(categoryCacheKey);
            this._memoryCache.Remove(packageCacheKey);
            this._memoryCache.Remove(productCacheKey);

            this._logger.LogInformation("CashShop catalog cache invalidated for configuration {ConfigurationId}.", gameConfiguration.GetId());
            await this.LoadCatalogSnapshotFromDbAsync(gameConfiguration, cancellationToken).ConfigureAwait(false);
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

    private async ValueTask<IReadOnlyList<CashShopCategory>> GetAllCategoriesCachedAsync(GameConfiguration gameConfiguration, CancellationToken cancellationToken)
    {
        var cacheKey = GetCacheKey(CacheKeyAllCategories, gameConfiguration);
        if (this._memoryCache.TryGetValue(cacheKey, out IReadOnlyList<CashShopCategory>? cached) && cached is not null)
        {
            return cached;
        }

        await this.EnsureCacheLoadedAsync(gameConfiguration, cancellationToken).ConfigureAwait(false);
        return this._memoryCache.Get<IReadOnlyList<CashShopCategory>>(cacheKey) ?? Array.Empty<CashShopCategory>();
    }

    private async ValueTask<IReadOnlyList<CashShopPackage>> GetAllPackagesCachedAsync(GameConfiguration gameConfiguration, CancellationToken cancellationToken)
    {
        var cacheKey = GetCacheKey(CacheKeyAllPackages, gameConfiguration);
        if (this._memoryCache.TryGetValue(cacheKey, out IReadOnlyList<CashShopPackage>? cached) && cached is not null)
        {
            return cached;
        }

        await this.EnsureCacheLoadedAsync(gameConfiguration, cancellationToken).ConfigureAwait(false);
        return this._memoryCache.Get<IReadOnlyList<CashShopPackage>>(cacheKey) ?? Array.Empty<CashShopPackage>();
    }

    private async ValueTask<IReadOnlyList<CashShopProduct>> GetAllProductsCachedAsync(GameConfiguration gameConfiguration, CancellationToken cancellationToken)
    {
        var cacheKey = GetCacheKey(CacheKeyAllProducts, gameConfiguration);
        if (this._memoryCache.TryGetValue(cacheKey, out IReadOnlyList<CashShopProduct>? cached) && cached is not null)
        {
            return cached;
        }

        await this.EnsureCacheLoadedAsync(gameConfiguration, cancellationToken).ConfigureAwait(false);
        return this._memoryCache.Get<IReadOnlyList<CashShopProduct>>(cacheKey) ?? Array.Empty<CashShopProduct>();
    }

    private async Task EnsureCacheLoadedAsync(GameConfiguration gameConfiguration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gameConfiguration);
        var categoryCacheKey = GetCacheKey(CacheKeyAllCategories, gameConfiguration);
        var packageCacheKey = GetCacheKey(CacheKeyAllPackages, gameConfiguration);
        var productCacheKey = GetCacheKey(CacheKeyAllProducts, gameConfiguration);

        await this._reloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (this._memoryCache.TryGetValue(categoryCacheKey, out _) &&
                this._memoryCache.TryGetValue(packageCacheKey, out _) &&
                this._memoryCache.TryGetValue(productCacheKey, out _))
            {
                return;
            }

            await this.LoadCatalogSnapshotFromDbAsync(gameConfiguration, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            this._reloadLock.Release();
        }
    }

    private async Task LoadCatalogSnapshotFromDbAsync(GameConfiguration gameConfiguration, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gameConfiguration);
        using var context = this._persistenceContextProvider.CreateNewContext(gameConfiguration);

        var categories = (await context.GetAsync<CashShopCategory>(cancellationToken).ConfigureAwait(false)).ToList();
        var packages = (await context.GetAsync<CashShopPackage>(cancellationToken).ConfigureAwait(false)).ToList();
        var products = (await context.GetAsync<CashShopProduct>(cancellationToken).ConfigureAwait(false)).ToList();

        var cacheEntryOptions = new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(DefaultCacheDuration)
            .SetPriority(CacheItemPriority.High);

        this._memoryCache.Set(GetCacheKey(CacheKeyAllCategories, gameConfiguration), (IReadOnlyList<CashShopCategory>)categories, cacheEntryOptions);
        this._memoryCache.Set(GetCacheKey(CacheKeyAllPackages, gameConfiguration), (IReadOnlyList<CashShopPackage>)packages, cacheEntryOptions);
        this._memoryCache.Set(GetCacheKey(CacheKeyAllProducts, gameConfiguration), (IReadOnlyList<CashShopProduct>)products, cacheEntryOptions);

        this._logger.LogInformation(
            "CashShop catalog snapshot loaded for configuration {ConfigurationId}: {CategoriesCount} categories, {PackagesCount} packages, {ProductsCount} products.",
            gameConfiguration.GetId(),
            categories.Count,
            packages.Count,
            products.Count);
    }

    private static string GetCacheKey(string cacheKey, GameConfiguration gameConfiguration) => $"{cacheKey}:{gameConfiguration.GetId()}";
}
