// <copyright file="ICashShopCatalogService.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.CashShop;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
    ValueTask<IReadOnlyList<CashShopCategory>> GetRootCategoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtém todas as subcategorias pertencentes a uma categoria pai específica.
    /// </summary>
    /// <param name="parentProductDisplaySeq">ProductDisplaySeq da categoria pai.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Lista de subcategorias ordenadas por DisplayOrder.</returns>
    ValueTask<IReadOnlyList<CashShopCategory>> GetSubCategoriesAsync(int parentProductDisplaySeq, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtém uma categoria específica pelo seu identificador de protocolo (ProductDisplaySeq).
    /// </summary>
    /// <param name="productDisplaySeq">Identificador IBS da categoria.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>A categoria encontrada ou null.</returns>
    ValueTask<CashShopCategory?> GetCategoryByDisplaySeqAsync(int productDisplaySeq, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtém todos os pacotes ativos pertencentes a uma categoria (ou subcategoria).
    /// </summary>
    /// <param name="productDisplaySeq">ProductDisplaySeq da categoria.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Lista de pacotes ordenados por ViewOrder.</returns>
    ValueTask<IReadOnlyList<CashShopPackage>> GetPackagesByCategoryAsync(int productDisplaySeq, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtém um pacote específico pelo seu identificador de protocolo de compra (PackageProductSeq).
    /// </summary>
    /// <param name="packageProductSeq">Identificador IBS do pacote.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>O pacote encontrado com seus produtos associados ou null.</returns>
    ValueTask<CashShopPackage?> GetPackageBySeqAsync(int packageProductSeq, CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtém uma variação específica de produto pelo seu PriceSeq.
    /// </summary>
    /// <param name="priceSeq">Identificador IBS da variação de preço.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>O produto encontrado ou null.</returns>
    ValueTask<CashShopProduct?> GetProductByPriceSeqAsync(int priceSeq, CancellationToken cancellationToken = default);

    /// <summary>
    /// Força a invalidação e recarregamento síncrono/assíncrono do cache de catálogo a partir do banco de dados.
    /// </summary>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    Task InvalidateAndReloadCacheAsync(CancellationToken cancellationToken = default);
}
