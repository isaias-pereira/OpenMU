// <copyright file="CashShopDurationContractTests.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full information.
// </copyright>

namespace MUnique.OpenMU.Tests.CashShop;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.CashShop;
using MUnique.OpenMU.GameLogic.PlayerActions.CashShop;
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.Persistence.InMemory;
using MUnique.OpenMU.PlugIns;
using NUnit.Framework;

/// <summary>
/// GATE A1/B (Rodada 2AU-B): crava a semântica de duração do <see cref="CashShopSelectionResolver"/> —
/// a fonte de <c>DurationSeconds</c> que alimentará a expiração server-side (Rodada 2AV/2AW).
/// <para>
/// Dados reais do banco de homologação (consultados em 19/10, tabela data."CashShopProduct"):
/// pacote 35, product 58, price seq 95 = 86.400 s (1 Day) e price seq 96 = 604.800 s (7 Days),
/// unit type 386 (segundos), PropertySeq 10; pacote 37, product 57, price 93 = 86.400 s e
/// price 94 = 604.800 s (Panda Ring).
/// </para>
/// </summary>
[TestFixture]
public class CashShopDurationContractTests
{
    private const int ItemGroup = 13;
    private const int ItemNumber = 20;
    private const int ItemCode = (ItemGroup * 512) + ItemNumber;
    private const int WCoinPCashType = 509;

    private InMemoryPersistenceContextProvider _contextProvider = null!;
    private GameConfiguration _configuration = null!;
    private IGameContext _gameContext = null!;
    private Mock<ICashShopCatalogService> _catalog = null!;
    private CashShopSelectionResolver _resolver = null!;

    [SetUp]
    public void SetUp()
    {
        this._contextProvider = new InMemoryPersistenceContextProvider();
        var configurationMock = new Mock<GameConfiguration>();
        configurationMock.SetupAllProperties();
        configurationMock.Setup(c => c.Items).Returns(new List<ItemDefinition>());
        configurationMock.Setup(c => c.Maps).Returns(new List<GameMapDefinition>());
        configurationMock.Setup(c => c.Skills).Returns(new List<Skill>());
        configurationMock.Setup(c => c.PlugInConfigurations).Returns(new List<PlugInConfiguration>());
        configurationMock.Setup(c => c.CharacterClasses).Returns(new List<CharacterClass>());
        configurationMock.Setup(c => c.GlobalAttributeCombinations).Returns(new List<MUnique.OpenMU.AttributeSystem.AttributeRelationship>());
        configurationMock.Setup(c => c.GlobalBaseAttributeValues).Returns(new List<MUnique.OpenMU.AttributeSystem.ConstValueAttribute> { new(1, MUnique.OpenMU.GameLogic.Attributes.Stats.MoneyAmountRate) });
        this._configuration = configurationMock.Object;
        this._configuration.Items.Add(new ItemDefinition { Group = ItemGroup, Number = ItemNumber, Name = "Wizard Ring", Width = 2, Height = 2 });

        var map = new Mock<GameMapDefinition>();
        map.SetupAllProperties();
        map.Setup(m => m.DropItemGroups).Returns(new List<DropItemGroup>());
        map.Setup(m => m.MonsterSpawns).Returns(new List<MonsterSpawnArea>());
        map.Object.TerrainData = new byte[ushort.MaxValue + 3];
        this._configuration.Maps.Add(map.Object);
        this._configuration.RecoveryInterval = int.MaxValue;

        this._gameContext = CreateGameContext(this._contextProvider, this._configuration);
        this._catalog = new Mock<ICashShopCatalogService>();
        this._catalog
            .Setup(c => c.GetPackageBySeqAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<GameConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CashShopPackage?)null);
        this._catalog
            .Setup(c => c.GetPackageProductsByPriceSeqAsync(It.IsAny<int[]>(), It.IsAny<int>(), It.IsAny<GameConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CashShopProduct>());
        this._resolver = new CashShopSelectionResolver(this._catalog.Object, NullLogger<CashShopSelectionResolver>.Instance);
    }

    private static IGameContext CreateGameContext(IPersistenceContextProvider contextProvider, GameConfiguration configuration)
    {
        var mapInitializer = new MapInitializer(configuration, new NullLogger<MapInitializer>(), NullDropGenerator.Instance, null);
        var gameContext = new GameContext(
            configuration,
            contextProvider,
            mapInitializer,
            new NullLoggerFactory(),
            new PlugInManager(null, new NullLoggerFactory(), null, null),
            NullDropGenerator.Instance,
            new ConfigurationChangeMediator());
        mapInitializer.PlugInManager = gameContext.PlugInManager;
        mapInitializer.PathFinderPool = gameContext.PathFinderPool;
        return gameContext;
    }

    private void SetupCatalog(int productSeq, int priceSeq, IEnumerable<(int PropertySeq, string Value, int UnitType)> rows, int price)
    {
        var package = new CashShopPackage
        {
            ProductDisplaySeq = 31,
            PackageProductSeq = 35,
            PackageProductName = "[Wizard Ring]",
            DeleteFlag = 181,
            SalesFlag = 182,
            CashType = WCoinPCashType,
            CashTypeFlag = 669,
            PackageProductType = 170,
            EventFlag = 200,
            ProductSeqList = $"[{productSeq}]",
            PriceSeqList = $"[{priceSeq}]",
        };
        this._catalog
            .Setup(c => c.GetPackageBySeqAsync(31, 35, It.IsAny<GameConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(package);
        var products = rows.Select(r => new CashShopProduct
        {
            ProductSeq = productSeq,
            PriceSeq = priceSeq,
            PropertySeq = r.PropertySeq,
            ProductName = "[Wizard Ring]",
            Price = price,
            Value = r.Value,
            UnitName = "Sec.",
            UnitType = r.UnitType,
            MustFlag = r.PropertySeq == 10,
            ViewOrder = 1,
            ProductType = 1,
            StorageGroup = 673,
            ShareFlag = true,
            DeleteFlag = 144,
            InGamePackageID = ItemCode.ToString(),
        }).ToList();
        this._catalog
            .Setup(c => c.GetPackageProductsByPriceSeqAsync(It.IsAny<int[]>(), priceSeq, It.IsAny<GameConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);
    }

    private async Task<Player> CreatePlayerWithBalanceAsync(long balance)
    {
        var player = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);
        using var context = this._contextProvider.CreateNewContext(this._configuration);
        var wallet = context.CreateNew<WCoinWallet>();
        wallet.Id = player.Account!.Id;
        wallet.Balance = balance;
        await context.SaveChangesAsync().ConfigureAwait(false);
        return player;
    }

    /// <summary>O PriceSeq 95 real (Wizard Ring, 1 dia) agrega 86.400 segundos — a duração contratada do contrato de expiração.</summary>
    [Test]
    public async Task ResolveAsync_OneDayOffer_Aggregates86400Seconds()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupCatalog(58, 95, new[] { (10, "86400", 386), (37, "365", 151), (53, "365", 151) }, 100);

        var result = await this._resolver.ResolveAsync(player, 35, 31, 95, ItemCode, WCoinPCashType, false, false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.DurationSeconds, Is.EqualTo(86_400), "1 Day = 86.400 s (unit 386, PropertySeq 10).");
        });
    }

    /// <summary>O PriceSeq 96 real (7 dias) agrega 604.800 segundos.</summary>
    [Test]
    public async Task ResolveAsync_SevenDayOffer_Aggregates604800Seconds()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupCatalog(58, 96, new[] { (10, "604800", 386), (37, "365", 151), (53, "365", 151) }, 500);

        var result = await this._resolver.ResolveAsync(player, 35, 31, 96, ItemCode, WCoinPCashType, false, false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.DurationSeconds, Is.EqualTo(604_800), "7 Days = 604.800 s.");
        });
    }

    /// <summary>Oferta sem linha de duração é permanente: DurationSeconds 0 (zero real, não NULL).</summary>
    [Test]
    public async Task ResolveAsync_PermanentOffer_AggregatesZeroSeconds()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupCatalog(58, 95, new[] { (7, "20", 680), (35, "365", 151), (51, "365", 151) }, 100);

        var result = await this._resolver.ResolveAsync(player, 35, 31, 95, ItemCode, WCoinPCashType, false, false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.DurationSeconds, Is.EqualTo(0), "Oferta sem row de duração = permanente (zero).");
        });
    }

    /// <summary>Duas linhas de duração divergentes recusam a oferta: a duração contratada precisa ser inequívoca.</summary>
    [Test]
    public async Task ResolveAsync_ConflictingDurations_RefusesSelection()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupCatalog(58, 95, new[] { (10, "86400", 386), (12, "604800", 386), (37, "365", 151), (53, "365", 151) }, 100);

        var result = await this._resolver.ResolveAsync(player, 35, 31, 95, ItemCode, WCoinPCashType, false, false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False, "Durações divergentes não podem virar um prazo contratado.");
            Assert.That(result.ErrorMessage, Does.Contain("conflicting duration"));
        });
    }
}
