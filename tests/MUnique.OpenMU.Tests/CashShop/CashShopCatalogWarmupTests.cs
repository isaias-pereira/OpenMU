// <copyright file="CashShopCatalogWarmupTests.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests.CashShop;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.CashShop;
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.Persistence.InMemory;
using Moq;
using NUnit.Framework;

/// <summary>
/// GATE K1 (cold start): o <see cref="CashShopCatalogService.WarmupAsync"/> pré-carrega o
/// catálogo no cache — a primeira consulta do jogador NÃO dispara novo load do banco.
/// </summary>
[TestFixture]
public class CashShopCatalogWarmupTests
{
    /// <summary>
    /// Após o warmup, a primeira consulta de pacote é servida do cache: nenhum novo
    /// <c>CreateNewContext</c> acontece (nenhum novo load do banco) — o cold start do primeiro
    /// Buy desaparece.
    /// </summary>
    [Test]
    public async Task WarmupAsync_PopulatesCache_FirstQueryDoesNotReload()
    {
        var innerProvider = new InMemoryPersistenceContextProvider();
        var providerMock = new Mock<IPersistenceContextProvider>();
        providerMock
            .Setup(p => p.CreateNewContext(It.IsAny<GameConfiguration>()))
            .Returns(() => innerProvider.CreateNewContext());
        var service = new CashShopCatalogService(providerMock.Object, new MemoryCache(new MemoryCacheOptions()), NullLogger<CashShopCatalogService>.Instance);
        var configuration = new Mock<GameConfiguration>();
        configuration.SetupAllProperties();

        await service.WarmupAsync(configuration.Object).ConfigureAwait(false);
        var afterWarmup = providerMock.Invocations.Count;

        // A primeira consulta do jogador: servida do cache, sem novo load.
        await service.GetPackageBySeqAsync(31, 35, configuration.Object).ConfigureAwait(false);
        var afterQuery = providerMock.Invocations.Count;

        Assert.Multiple(() =>
        {
            Assert.That(afterWarmup, Is.GreaterThan(0), "O warmup carrega o catálogo (contextos criados).");
            Assert.That(afterQuery, Is.EqualTo(afterWarmup), "A primeira consulta NÃO deve recarregar o catálogo após o warmup.");
        });
    }

    /// <summary>
    /// O warmup é seguro com catálogo vazio (banco recém-criado): não lança e não quebra o boot.
    /// </summary>
    [Test]
    public async Task WarmupAsync_EmptyCatalog_DoesNotThrow()
    {
        var providerMock = new Mock<IPersistenceContextProvider>();
        providerMock
            .Setup(p => p.CreateNewContext(It.IsAny<GameConfiguration>()))
            .Returns(() => new InMemoryPersistenceContextProvider().CreateNewContext());
        var service = new CashShopCatalogService(providerMock.Object, new MemoryCache(new MemoryCacheOptions()), NullLogger<CashShopCatalogService>.Instance);
        var configuration = new Mock<GameConfiguration>();
        configuration.SetupAllProperties();

        Assert.DoesNotThrowAsync(async () => await service.WarmupAsync(configuration.Object).ConfigureAwait(false));
    }
}
