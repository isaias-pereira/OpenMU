// <copyright file="ICashShopCatalogService.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.CashShop;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Entities;

/// <summary>
/// Interface para o serviço de catálogo da Cash Shop.
/// Provê acesso em cache às categorias, pacotes e produtos cadastrados no sistema.
/// </summary>
public interface ICashShopCatalogService
{
    /// <summary>
    /// Obtém todas as categorias raiz (Root=1 / IsRoot=true) ordenadas por DisplayOrder.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Lista de categorias raiz com suas subcategorias carregadas.</returns>
    ValueTask<IReadOnlyList<CashShopCategory>> GetRootCategoriesAsync(GameConfiguration gameConfiguration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtém todas as subcategorias pertencentes a uma categoria pai específica.
    /// </summary>
    /// <param name="parentProductDisplaySeq">ProductDisplaySeq da categoria pai.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Lista de subcategorias ordenadas por DisplayOrder.</returns>
    ValueTask<IReadOnlyList<CashShopCategory>> GetSubCategoriesAsync(int parentProductDisplaySeq, GameConfiguration gameConfiguration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtém uma categoria específica pelo seu identificador de protocolo (ProductDisplaySeq).
    /// </summary>
    /// <param name="productDisplaySeq">Identificador IBS da categoria.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>A categoria encontrada ou null.</returns>
    ValueTask<CashShopCategory?> GetCategoryByDisplaySeqAsync(int productDisplaySeq, GameConfiguration gameConfiguration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtém todos os pacotes ativos pertencentes a uma categoria (ou subcategoria).
    /// </summary>
    /// <param name="productDisplaySeq">ProductDisplaySeq da categoria.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Lista de pacotes ordenados por ViewOrder.</returns>
    ValueTask<IReadOnlyList<CashShopPackage>> GetPackagesByCategoryAsync(int productDisplaySeq, GameConfiguration gameConfiguration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtém um pacote específico pelo seu identificador de protocolo de compra (PackageProductSeq).
    /// </summary>
    /// <param name="packageProductSeq">Identificador IBS do pacote.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>O pacote encontrado com seus produtos associados ou null.</returns>
    ValueTask<CashShopPackage?> GetPackageBySeqAsync(int productDisplaySeq, int packageProductSeq, GameConfiguration gameConfiguration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a package by its legacy sequence-only lookup. The IBS can repeat the sequence across categories.
    /// </summary>
    ValueTask<CashShopPackage?> GetPackageBySeqAsync(int packageProductSeq, GameConfiguration gameConfiguration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtém uma variação específica de produto pelo seu PriceSeq.
    /// </summary>
    /// <param name="priceSeq">Identificador IBS da variação de preço.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>O produto encontrado ou null.</returns>
    ValueTask<CashShopProduct?> GetProductByPriceSeqAsync(int productSeq, int priceSeq, int propertySeq, GameConfiguration gameConfiguration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets every product row that matches a price sequence, regardless of its product sequence.
    /// </summary>
    /// <remarks>
    /// <see cref="PriceSeq"/> is NOT unique in the catalog: the real IBS reuses it across
    /// different <see cref="CashShopProduct.ProductSeq"/> values (verified: 874 distinct
    /// <c>PriceSeq</c> values for 2720 product rows, up to 5 rows per value). Callers that only
    /// have a <c>PriceSeq</c> must therefore resolve against every candidate and pick the row
    /// that also satisfies the remaining constraints of the request.
    /// </remarks>
    /// <param name="priceSeq">The IBS price sequence sent by the client.</param>
    /// <param name="gameConfiguration">The game configuration used for cache scoping.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>All matching product rows, in catalog order.</returns>
    ValueTask<IReadOnlyList<CashShopProduct>> GetProductsByPriceSeqAsync(int priceSeq, GameConfiguration gameConfiguration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets every product row that belongs to one of the given product sequences AND matches the price sequence.
    /// </summary>
    /// <remarks>
    /// This is the lookup the buy flow needs: the package declares its own
    /// <c>ProductSeqList</c>, and a <c>PriceSeq</c> on its own is ambiguous. The result is expected to
    /// be narrowed further by the caller (delete flag, cash type, item code).
    /// </remarks>
    /// <param name="productSequences">The product sequences declared by the package.</param>
    /// <param name="priceSeq">The price sequence sent by the client.</param>
    /// <param name="gameConfiguration">The game configuration used for cache scoping.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>All matching product rows, in catalog order.</returns>
    ValueTask<IReadOnlyList<CashShopProduct>> GetPackageProductsByPriceSeqAsync(
        int[] productSequences,
        int priceSeq,
        GameConfiguration gameConfiguration,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Força a invalidação e recarregamento síncrono/assíncrono do cache de catálogo a partir do banco de dados.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task InvalidateAndReloadCacheAsync(GameConfiguration gameConfiguration, CancellationToken cancellationToken = default);
}
