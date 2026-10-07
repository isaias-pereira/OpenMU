// <copyright file="CashShopBuyServiceTests.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests.CashShop;

using System;
using System.Collections;
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
using MUnique.OpenMU.Interfaces;
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.Persistence.InMemory;
using MUnique.OpenMU.PlugIns;
using NUnit.Framework;

/// <summary>
/// Testes do fluxo de compra da Cash Shop (<c>C1 D2 03</c>): valida catálogo, moeda, vigência,
/// ambigüidade e entrega no storage da conta antes de debitar o WCOIN.
/// </summary>
[TestFixture]
public class CashShopBuyServiceTests
{
    private const int WCoinPCashType = 509;
    private const int ActivePackageDeleteFlag = 181;
    private const int ActiveProductDeleteFlag = 144;
    private const int NormalPackageType = 170;
    private const int NormalEventFlag = 200;
    private const int NormalCashTypeFlag = 669;
    private const int SalesFlagAvailable = 182;
    private const int PackageProductSeq = 100;
    private const int CategoryProductDisplaySeq = 5;
    private const int ProductSeq = 528;
    private const int PriceSeq = 607;
    private const int ItemGroup = 31;
    private const int ItemNumber = 7;
    private const int ItemCode = (ItemGroup * 512) + ItemNumber;
    private const int Price = 500;

    private InMemoryPersistenceContextProvider _contextProvider = null!;
    private GameConfiguration _configuration = null!;
    private Mock<ICashShopCatalogService> _catalog = null!;
    private CashShopSelectionResolver _resolver = null!;
    private Mock<ICashShopStorageService> _storageService = null!;
    private CashShopBuyService _service = null!;
    private IGameContext _gameContext = null!;
    private Guid _accountId;

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
        this._configuration.Items.Add(new ItemDefinition { Group = ItemGroup, Number = ItemNumber, Name = "Test Item", Width = 1, Height = 1 });

        var map = new Mock<GameMapDefinition>();
        map.SetupAllProperties();
        map.Setup(m => m.DropItemGroups).Returns(new List<DropItemGroup>());
        map.Setup(m => m.MonsterSpawns).Returns(new List<MonsterSpawnArea>());
        map.Object.TerrainData = new byte[ushort.MaxValue + 3];
        this._configuration.Maps.Add(map.Object);
        this._configuration.RecoveryInterval = int.MaxValue;

        // The player must share this provider, otherwise the wallet and the storage rows
        // created here would live in a different in-memory repository than the player's.
        this._gameContext = CreateGameContext(this._contextProvider, this._configuration);

        this._storageService = new Mock<ICashShopStorageService>();
        this._storageService.Setup(s => s.Capacity).Returns(30);
        this._storageService.Setup(s => s.GetItemCountAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        this._storageService.Setup(s => s.GetNextStorageIndexAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        this.SetupStorageReservation(0, StorageIndexFull: false);

        this._catalog = new Mock<ICashShopCatalogService>();
        this._catalog
            .Setup(c => c.GetPackageBySeqAsync(CategoryProductDisplaySeq, PackageProductSeq, It.IsAny<GameConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CashShopPackage?)null);
        this._catalog
            .Setup(c => c.GetPackageProductsByPriceSeqAsync(It.IsAny<int[]>(), It.IsAny<int>(), It.IsAny<GameConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CashShopProduct>());

        // The buy service resolves the selection through the shared resolver, which is where the
        // catalog rules live; the mocked catalog is injected into it so the tests keep asserting
        // against the same catalog data as before.
        this._resolver = new CashShopSelectionResolver(this._catalog.Object, NullLogger<CashShopSelectionResolver>.Instance);
        this._service = new CashShopBuyService(this._resolver, this._storageService.Object, NullLogger<CashShopBuyService>.Instance, new UnsafeContextRetirement(NullLogger<UnsafeContextRetirement>.Instance));
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

    private void SetupValidCatalog(Action<CashShopPackage>? configurePackage = null, IReadOnlyList<CashShopProduct>? products = null, int? productDisplaySeq = null, int? packageProductSeq = null)
    {
        var displaySeq = productDisplaySeq ?? CategoryProductDisplaySeq;
        var packageSeq = packageProductSeq ?? PackageProductSeq;
        var package = new CashShopPackage
        {
            ProductDisplaySeq = displaySeq,
            PackageProductSeq = packageSeq,
            PackageProductName = "Test Package",
            DeleteFlag = ActivePackageDeleteFlag,
            SalesFlag = SalesFlagAvailable,
            CashType = WCoinPCashType,
            CashTypeFlag = NormalCashTypeFlag,
            PackageProductType = NormalPackageType,
            EventFlag = NormalEventFlag,
            ProductSeqList = $"[{ProductSeq}]",
            PriceSeqList = $"[{PriceSeq}]",
        };
        configurePackage?.Invoke(package);

        this._catalog
            .Setup(c => c.GetPackageBySeqAsync(displaySeq, packageSeq, It.IsAny<GameConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(package);

        // The real CashShopCatalogService filters by the requested price sequence and by the
        // product sequences of the package. The mock reproduces both filters, otherwise a product
        // of another price sequence would reach the resolver and be charged as an inconsistent price.
        var configuredProducts =
            products ?? new[] { this.CreateProduct(PropertySeq: 1, productPrice: Price) };
        this._catalog
            .Setup(c => c.GetPackageProductsByPriceSeqAsync(It.IsAny<int[]>(), It.IsAny<int>(), It.IsAny<GameConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                int[] productSequences,
                int requestedPriceSeq,
                GameConfiguration _,
                CancellationToken _) =>
                configuredProducts
                    .Where(product =>
                        product.PriceSeq == requestedPriceSeq
                        && productSequences.Contains(product.ProductSeq))
                    .ToList());
    }

    /// <summary>
    /// Configura <see cref="ICashShopStorageService.ReserveNextStorageIndexAsync"/> como o
    /// serviço real se comporta: a reserva cria a linha no contexto que vai gravar a entrega, e
    /// devolve <see cref="CashShopStorageReservation.Empty"/> quando o storage está cheio.
    /// </summary>
    /// <param name="storageIndex">O índice a reservar, ou <c>-1</c> para simular storage cheio.</param>
    /// <param name="StorageIndexFull">
    /// Indica que o storage está cheio. Mantido separado do valor para o texto da intenção ficar
    /// explícito na chamada.
    /// </param>
    private void SetupStorageReservation(int storageIndex, bool StorageIndexFull)
    {
        this._storageService
            .Setup(s => s.ReserveNextStorageIndexAsync(
                It.IsAny<IContext>(),
                It.IsAny<Guid>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns((IContext context, Guid accountId, bool forGift, CancellationToken _) =>
            {
                if (StorageIndexFull)
                {
                    return ValueTask.FromResult(CashShopStorageReservation.Empty);
                }

                // A linha é criada no contexto do jogador, como faz o serviço real: é isso que
                // faz a reserva participar da mesma transação do débito.
                var storageItem = context.CreateNew<CashShopStorageItem>();
                storageItem.AccountId = accountId;
                storageItem.StorageIndex = storageIndex;
                return ValueTask.FromResult(new CashShopStorageReservation(storageItem, storageIndex));
            });
    }

    private CashShopProduct CreateProduct(int PropertySeq, int productPrice, int deleteFlag = ActiveProductDeleteFlag, string? inGamePackageId = null, int? productSequence = null)
    {
        return new CashShopProduct
        {
            ProductSeq = productSequence ?? ProductSeq,
            PriceSeq = PriceSeq,
            PropertySeq = PropertySeq,
            ProductName = "Test Product",
            Price = productPrice,
            DeleteFlag = deleteFlag,
            StorageGroup = 3,
            InGamePackageID = inGamePackageId ?? ItemCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    private async ValueTask<Player> CreatePlayerWithBalanceAsync(long balance)
    {
        var player = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);

        // The test account is a Moq object whose Id is Guid.Empty, and the in-memory context
        // replaces an empty Id with a fresh one when creating entities, so the wallet would
        // never be found under the account key. Give the account a real Id first.
        this._accountId = Guid.NewGuid();
        player.Account!.Id = this._accountId;

        var context = this._contextProvider.CreateNewContext(this._configuration);
        var wallet = context.CreateNew<WCoinWallet>();
        wallet.Id = this._accountId;
        wallet.Balance = balance;
        await context.SaveChangesAsync().ConfigureAwait(false);

        return player;
    }

    /// <summary>
    /// Cria um jogador cujo <see cref="Player.PersistenceContext"/> é um
    /// <see cref="FailingSaveContext"/>, devolvendo o player e o contexto envolvido.
    /// </summary>
    private async ValueTask<(Player Player, FailingSaveContext Context)> CreatePlayerWithFailingSaveAsync(long balance, Exception? exception = null)
    {
        var provider = new FailingContextProvider(this._contextProvider, exception);
        this._gameContext = CreateGameContext(provider, this._configuration);
        var player = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);
        this._accountId = Guid.NewGuid();
        player.Account!.Id = this._accountId;

        var context = this._contextProvider.CreateNewContext(this._configuration);
        var wallet = context.CreateNew<WCoinWallet>();
        wallet.Id = this._accountId;
        wallet.Balance = balance;
        await context.SaveChangesAsync().ConfigureAwait(false);

        return (player, provider.PlayerContext!);
    }

    private async ValueTask<long> GetWalletBalanceAsync()
    {
        var wallet = await this._contextProvider.RepositoryProvider
            .GetRepository<WCoinWallet>()!.GetByIdAsync(this._accountId).ConfigureAwait(false);
        return wallet?.Balance ?? 0;
    }

    private async ValueTask<IReadOnlyList<CashShopStorageItem>> GetStoredItemsAsync()
    {
        var repository = this._contextProvider.RepositoryProvider.GetRepository<CashShopStorageItem>();
        if (repository is null)
        {
            return Array.Empty<CashShopStorageItem>();
        }

        var stored = await repository.GetAllAsync().ConfigureAwait(false);
        return stored.ToList();
    }

    /// <summary>
    /// Cria uma linha de <c>IBSProduct</c> com os mesmos 17 campos do arquivo real, para os
    /// testes que reproduzem uma oferta concreta do catálogo.
    /// </summary>
    private static CashShopProduct CreateCatalogRow(
        int productSeq,
        int priceSeq,
        string itemCode,
        int propertySeq,
        string propertyName,
        string value,
        string unitName,
        int unitType,
        bool mustFlag,
        int viewOrder,
        int price,
        int productType)
    {
        return new CashShopProduct
        {
            ProductSeq = productSeq,
            PriceSeq = priceSeq,
            InGamePackageID = itemCode,
            PropertySeq = propertySeq,
            ProductName = "Wizard Ring",
            PropertyName = propertyName,
            Value = value,
            UnitName = unitName,
            UnitType = unitType,
            MustFlag = mustFlag,
            ViewOrder = viewOrder,
            ProductType = productType,
            Price = price,
            PropertyType = 142,
            StorageGroup = 673,
            ShareFlag = true,
            DeleteFlag = ActiveProductDeleteFlag,
        };
    }

    /// <summary>
    /// Uma compra válida debita o preço e grava a entrega no storage da conta.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_ValidPurchase_DebitsAndDeliversToStorage()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupValidCatalog();

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.That(result.Success, Is.True, result.ErrorMessage);
        Assert.That(result.DeliveredItem, Is.EqualTo("Test Product"));
        Assert.That(await this.GetWalletBalanceAsync(), Is.EqualTo(1000 - Price));

        var stored = await this.GetStoredItemsAsync();
        Assert.That(stored, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(stored[0].AccountId, Is.EqualTo(this._accountId));
            Assert.That(stored[0].ProductSeq, Is.EqualTo(ProductSeq));
            Assert.That(stored[0].PriceSeq, Is.EqualTo(PriceSeq));
            Assert.That(stored[0].CashPoint, Is.EqualTo(Price));
            Assert.That(stored[0].ItemType, Is.EqualTo(CashShopStorageItemType.Product));
            Assert.That(stored[0].Item, Is.Not.Null, "The storage line must reference the delivered item.");
        });
    }

    /// <summary>
    /// O pacote real 35 (Wizard Ring, categoria 31) é vendido em duas variantes de preço — 95 a 1 dia
    /// e 96 a 7 dias — e cada variante tem três linhas em <c>IBSProduct.txt</c>: PropertySeq 10
    /// (<c>Duration</c>), 37 (<c>기간제-감가상각기간</c>) e 53 (<c>기간아이템 유효기간</c>).
    /// <para>
    /// Essas três linhas são atributos complementares da <i>mesma</i> oferta, não variantes
    /// concorrentes: o cliente as percorre todas em <c>GetPriceSeqNext</c> e devolve um único pedido,
    /// e nos 874 grupos reais do catálogo nenhum tem duas linhas de duração, nem preços ou
    /// <c>InGamePackageID</c> divergentes. Portanto a compra tem de ser aceita.
    /// </para>
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_RealPackage35_ComplementaryPropertyRows_ResolveAndDeliver()
    {
        // InGamePackageID 6676 = group 13, number 20 (codigo = group * 512 + number).
        this._configuration.Items.Add(new ItemDefinition { Group = 13, Number = 20, Name = "Wizard Ring", Width = 2, Height = 2 });

        var player = await this.CreatePlayerWithBalanceAsync(1000);

        this.SetupValidCatalog(
            package =>
            {
                package.ProductDisplaySeq = 31;
                package.PackageProductSeq = 35;
                package.PackageProductName = "[Wizard Ring]";
                package.ProductSeqList = "[58]";
                package.PriceSeqList = "[95,96]";
                package.Price = 100;
            },
            products: new[]
            {
                CreateCatalogRow(58, 95, "6676", 10, "Duration", "86400", "Sec.", 386, true, 1, 100, 140),
                CreateCatalogRow(58, 95, "6676", 37, "기간제-감가상각기간", "365", "Day(s)", 151, false, 9, 100, 140),
                CreateCatalogRow(58, 95, "6676", 53, "기간아이템 유효기간", "365", "Day(s)", 151, false, 9, 100, 140),
            },
            productDisplaySeq: 31,
            packageProductSeq: 35);

        var result = await this._service.ProcessPurchaseAsync(player, 35, 31, 95, 6676, WCoinPCashType, false);

        Assert.That(result.Success, Is.True, $"O pacote real 35 deve ser compravel: {result.ErrorMessage}");

        var stored = await this.GetStoredItemsAsync();
        Assert.Multiple(() =>
        {
            Assert.That(stored, Has.Count.EqualTo(1));
            Assert.That(stored[0].ProductSeq, Is.EqualTo(58));
            Assert.That(stored[0].PriceSeq, Is.EqualTo(95));
            Assert.That(stored[0].CashPoint, Is.EqualTo(100), "O preco cobrado e o da variante de 1 dia.");
            Assert.That(stored[0].Item, Is.Not.Null);
        });
        Assert.That(await this.GetWalletBalanceAsync(), Is.EqualTo(1000 - 100));
    }

    /// <summary>
    /// A segunda variante do pacote 35 (PriceSeq 96, 7 dias) tem preco distinto da primeira
    /// (PriceSeq 95, 1 dia). O PriceSeq e o que o cliente escolhe na lista, entao cobrar o preco
    /// errado seria cobrar a oferta errada.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_RealPackage35_PriceSeq96_ChargesTheSevenDayVariantPrice()
    {
        this._configuration.Items.Add(new ItemDefinition { Group = 13, Number = 20, Name = "Wizard Ring", Width = 2, Height = 2 });
        var player = await this.CreatePlayerWithBalanceAsync(1000);

        this.SetupValidCatalog(
            package =>
            {
                package.ProductDisplaySeq = 31;
                package.PackageProductSeq = 35;
                package.ProductSeqList = "[58]";
                package.PriceSeqList = "[95,96]";
                package.Price = 100;
            },
            products: new[]
            {
                CreateCatalogRow(58, 96, "6676", 10, "Duration", "604800", "Sec.", 386, true, 1, 500, 140),
                CreateCatalogRow(58, 96, "6676", 37, "기간제-감가상각기간", "365", "Day(s)", 151, false, 9, 500, 140),
                CreateCatalogRow(58, 96, "6676", 53, "기간아이템 유효기간", "365", "Day(s)", 151, false, 9, 500, 140),
            },
            productDisplaySeq: 31,
            packageProductSeq: 35);

        var result = await this._service.ProcessPurchaseAsync(player, 35, 31, 96, 6676, WCoinPCashType, false);

        Assert.That(result.Success, Is.True, result.ErrorMessage);
        var stored = await this.GetStoredItemsAsync();
        Assert.Multiple(() =>
        {
            Assert.That(stored, Has.Count.EqualTo(1));
            Assert.That(stored[0].PriceSeq, Is.EqualTo(96));
            Assert.That(stored[0].CashPoint, Is.EqualTo(500), "A variante de 7 dias custa 500, nao 100.");
        });
        Assert.That(await this.GetWalletBalanceAsync(), Is.EqualTo(1000 - 500));
    }

    /// <summary>
    /// O padrao 10/37/53 (273 grupos reais) e o de item de periodo: as tres linhas descrevem uma
    /// oferta unica e precisam ser aceitas como uma so.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_Pattern10_37_53_AggregatesIntoASingleOffer()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupValidCatalog(products: new[]
        {
            CreateCatalogRow(ProductSeq, PriceSeq, ItemCode.ToString(), 10, "Duration", "86400", "Sec.", 386, true, 1, Price, 140),
            CreateCatalogRow(ProductSeq, PriceSeq, ItemCode.ToString(), 37, "기간제-감가상각기간", "365", "Day(s)", 151, false, 9, Price, 140),
            CreateCatalogRow(ProductSeq, PriceSeq, ItemCode.ToString(), 53, "기간아이템 유효기간", "365", "Day(s)", 151, false, 9, Price, 140),
        });

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.That(result.Success, Is.True, result.ErrorMessage);
    }

    /// <summary>
    /// O padrao 7/35/51 (487 grupos reais, o mais frequente do catalogo) e o de consumivel:
    /// quantidade em 7, metadados em 35 e 51. Tambem e uma oferta unica.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_Pattern7_35_51_AggregatesIntoASingleOffer()
    {
        // Quantity 20 of a stackable item needs Durability >= 20 as stack capacity
        // (IsStackable = !wearable && Durability > 1). The resolver takes the first
        // matching definition, so the SetUp item itself is made stackable here.
        this._configuration.Items.First(item => item.Group == ItemGroup && item.Number == ItemNumber).Durability = 20;
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupValidCatalog(products: new[]
        {
            CreateCatalogRow(ProductSeq, PriceSeq, ItemCode.ToString(), 7, "Quantity", "20", "EA", 680, true, 1, Price, 138),
            CreateCatalogRow(ProductSeq, PriceSeq, ItemCode.ToString(), 35, "소모성-감가상각기간", "365", "Day(s)", 151, false, 9, Price, 138),
            CreateCatalogRow(ProductSeq, PriceSeq, ItemCode.ToString(), 51, "소모아이템 유효기간", "365", "Day(s)", 151, false, 9, Price, 138),
        });

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.That(result.Success, Is.True, result.ErrorMessage);
        var stored = await this.GetStoredItemsAsync();
        Assert.That(stored, Has.Count.EqualTo(1), "Quantity 20 fits one stack of capacity 20.");
        Assert.That(stored[0].Item?.Durability, Is.EqualTo(20));
    }

    /// <summary>
    /// Quantidade acima da capacidade de stack e recusada: dividir em varias linhas mudaria o
    /// que a oferta promete, e reduzir silenciosamente entregaria menos do que o catalogo cobra.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_QuantityAboveStackCapacity_IsRejected()
    {
        // Item empilhavel com capacidade 20; a oferta pede 25 unidades.
        this._configuration.Items.First(item => item.Group == ItemGroup && item.Number == ItemNumber).Durability = 20;
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupValidCatalog(products: new[]
        {
            CreateCatalogRow(ProductSeq, PriceSeq, ItemCode.ToString(), 7, "Quantity", "25", "EA", 680, true, 1, Price, 138),
        });

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False, "A quantity above the stack capacity must be refused, not split.");
            Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.CannotBeBought));
        });
        Assert.That(await this.GetWalletBalanceAsync(), Is.EqualTo(1000), "A refused purchase must not debit the wallet.");
        Assert.That(await this.GetStoredItemsAsync(), Is.Empty, "A refused purchase must not deliver anything.");
    }

    /// <summary>
    /// Quantidade acima de um em item nao-empilhavel e recusada: um item que nao empilha nao
    /// carrega mais de uma unidade, e o catalogo nao pode ser entregue parcialmente.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_QuantityAboveOneOfNonStackable_IsRejected()
    {
        // O item do SetUp nao tem Durability (0) nem ItemSlot: nao empilha. A oferta pede 20.
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupValidCatalog(products: new[]
        {
            CreateCatalogRow(ProductSeq, PriceSeq, ItemCode.ToString(), 7, "Quantity", "20", "EA", 680, true, 1, Price, 138),
        });

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False, "A quantity above one of a non-stackable item must be refused.");
            Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.CannotBeBought));
        });
        Assert.That(await this.GetWalletBalanceAsync(), Is.EqualTo(1000), "A refused purchase must not debit the wallet.");
        Assert.That(await this.GetStoredItemsAsync(), Is.Empty, "A refused purchase must not deliver anything.");
    }

    /// <summary>
    /// O padrao 9/10/37/53 (23 grupos reais) traz quantidade e duracao na mesma oferta.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_Pattern9_10_37_53_AggregatesQuantityAndDuration()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupValidCatalog(products: new[]
        {
            CreateCatalogRow(ProductSeq, PriceSeq, ItemCode.ToString(), 9, "Quantity", "1", "&nbsp;", 147, true, 1, Price, 140),
            CreateCatalogRow(ProductSeq, PriceSeq, ItemCode.ToString(), 10, "Duration", "259200", "Sec.", 386, true, 1, Price, 140),
            CreateCatalogRow(ProductSeq, PriceSeq, ItemCode.ToString(), 37, "기간제-감가상각기간", "365", "Day(s)", 151, false, 9, Price, 140),
            CreateCatalogRow(ProductSeq, PriceSeq, ItemCode.ToString(), 53, "기간아이템 유효기간", "365", "Day(s)", 151, false, 9, Price, 140),
        });

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.That(result.Success, Is.True, result.ErrorMessage);
    }

    /// <summary>
    /// Duas linhas com o mesmo atributo de duracao (PropertySeq 10 e 2, ambos em
    /// <c>IGS_PRODUCT_ATT_TYPE_USE_LIMIT_PERIOD</c>) sao conflito real: nao existe qual delas
    /// definiria a duracao. Nenhum dos 874 grupos reais tem esse caso, mas a regra precisa recusar.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_TwoDurationRows_AreRejectedAsRealConflict()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupValidCatalog(products: new[]
        {
            CreateCatalogRow(ProductSeq, PriceSeq, ItemCode.ToString(), 10, "Duration", "86400", "Sec.", 386, true, 1, Price, 140),
            CreateCatalogRow(ProductSeq, PriceSeq, ItemCode.ToString(), 2, "Duration", "604800", "Sec.", 386, true, 1, Price, 140),
        });

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.SelectionUnavailable));
        });
        Assert.That(await this.GetWalletBalanceAsync(), Is.EqualTo(1000), "Uma recusa nao pode debitar.");
    }

    /// <summary>
    /// Duas linhas com o mesmo atributo de quantidade (PropertySeq 7 e 9, ambos em
    /// <c>IGS_PRODUCT_ATT_TYPE_NUM</c>) sao conflito real.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_TwoQuantityRows_AreRejectedAsRealConflict()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupValidCatalog(products: new[]
        {
            CreateCatalogRow(ProductSeq, PriceSeq, ItemCode.ToString(), 7, "Quantity", "1", "EA", 680, true, 1, Price, 138),
            CreateCatalogRow(ProductSeq, PriceSeq, ItemCode.ToString(), 9, "Quantity", "5", "&nbsp;", 147, true, 1, Price, 138),
        });

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.SelectionUnavailable));
        });
        Assert.That(await this.GetWalletBalanceAsync(), Is.EqualTo(1000));
    }

    /// <summary>
    /// Precos divergentes dentro do mesmo grupo sao conflito real: nao existe qual cobrar.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_DivergingPrice_IsRejectedAsRealConflict()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupValidCatalog(products: new[]
        {
            CreateCatalogRow(ProductSeq, PriceSeq, ItemCode.ToString(), 10, "Duration", "86400", "Sec.", 386, true, 1, 100, 140),
            CreateCatalogRow(ProductSeq, PriceSeq, ItemCode.ToString(), 37, "기간제-감가상각기간", "365", "Day(s)", 151, false, 9, 200, 140),
        });

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.CannotBeBought));
        });
        Assert.That(await this.GetWalletBalanceAsync(), Is.EqualTo(1000));
    }

    /// <summary>
    /// <c>InGamePackageID</c> e o filtro exato do item entregue (o codigo que o cliente manda no
    /// D2 03). Uma linha de outro item, dentro do mesmo <c>PriceSeq</c>, nao participa da oferta e
    /// tambem nao pode criar conflito.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_RowWithAnotherItemCode_IsExcludedInsteadOfConflicting()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupValidCatalog(products: new[]
        {
            CreateCatalogRow(ProductSeq, PriceSeq, ItemCode.ToString(), 10, "Duration", "86400", "Sec.", 386, true, 1, Price, 140),
            CreateCatalogRow(ProductSeq, PriceSeq, ItemCode.ToString(), 37, "기간제-감가상각기간", "365", "Day(s)", 151, false, 9, Price, 140),
            CreateCatalogRow(ProductSeq, PriceSeq, ItemCode.ToString(), 53, "기간아이템 유효기간", "365", "Day(s)", 151, false, 9, Price, 140),
            // Mesmo ProductSeq e PriceSeq, item diferente: pertence a outra oferta.
            CreateCatalogRow(ProductSeq, PriceSeq, ((ItemGroup * 512) + 99).ToString(), 1, "IP Quantity", "1", "EA", 680, false, 9, 999, 138),
        });

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.That(result.Success, Is.True, result.ErrorMessage);
    }

    /// <summary>
    /// Grupo sem nenhuma linha de PropertySeq {2,12,28,58,10} e um item permanente: precisa
    /// resolver e entregar, sem duracao a extrair.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_GroupWithoutDurationRow_IsResolvedAsPermanent()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupValidCatalog(products: new[]
        {
            CreateCatalogRow(ProductSeq, PriceSeq, ItemCode.ToString(), 53, "기간아이템 유효기간", "365", "Day(s)", 151, false, 9, Price, 140),
            CreateCatalogRow(ProductSeq, PriceSeq, ItemCode.ToString(), 61, "감가상각기간", "120", "Day(s)", 151, false, 9, Price, 140),
        });

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.That(result.Success, Is.True, result.ErrorMessage);
        var stored = await this.GetStoredItemsAsync();
        Assert.That(stored, Has.Count.EqualTo(1), "Um item permanente tambem precisa ser entregue.");
    }

    /// <summary>
    /// Um pacote inexistente é recusado com o código de seleção indisponível, sem débito.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_UnknownPackage_IsRejected()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.SelectionUnavailable));
        });
            Assert.That(await this.GetWalletBalanceAsync(), Is.EqualTo(1000));
    }

    /// <summary>
    /// Um pacote apagado (DeleteFlag diferente do valor ativo) é recusado.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_DeletedPackage_IsRejected()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupValidCatalog(package => package.DeleteFlag = 0);

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.PackageUnavailable));
            Assert.That(await this.GetWalletBalanceAsync(), Is.EqualTo(1000));
    }

    /// <summary>
    /// Um pacote expirado é recusado mesmo tendo saldo.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_ExpiredPackage_IsRejected()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupValidCatalog(package => package.EndDate = DateTime.UtcNow.AddDays(-1));

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.PackageUnavailable));
            Assert.That(await this.GetWalletBalanceAsync(), Is.EqualTo(1000));
    }

    /// <summary>
    /// Um pacote que ainda não começou é recusado.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_NotYetStartedPackage_IsRejected()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupValidCatalog(package => package.StartDate = DateTime.UtcNow.AddDays(1));

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.PackageUnavailable));
            Assert.That(await this.GetWalletBalanceAsync(), Is.EqualTo(1000));
    }

    /// <summary>
    /// Datas zeradas significam "sem limite": o catálogo lista o pacote nesse caso, então a
    /// compra precisa aceitá-lo com a mesma semântica.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_ZeroedDates_AreTreatedAsNoLimit()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupValidCatalog(package =>
        {
            package.StartDate = default;
            package.EndDate = default;
        });

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.That(result.Success, Is.True, result.ErrorMessage);
        Assert.That(await this.GetWalletBalanceAsync(), Is.EqualTo(1000 - Price));
    }

    /// <summary>
    /// Uma moeda diferente da do pacote é recusada com o código 9 do cliente, sem débito.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_MismatchedCoinType_IsRejected()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupValidCatalog();

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, 508, false);

        Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.InvalidCoinType));
            Assert.That(await this.GetWalletBalanceAsync(), Is.EqualTo(1000));
    }

    /// <summary>
    /// Mileage não tem implementação: a compra é recusada em vez de debitar WCOIN.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_MileageRequest_IsRejected()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupValidCatalog();

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, true);

        Assert.That(result.Success, Is.False);
            Assert.That(await this.GetWalletBalanceAsync(), Is.EqualTo(1000));
    }

    /// <summary>
    /// Saldo insuficiente devolve o código 1 e não cria nenhuma linha de storage: o jogador
    /// nunca recebe o item de graça.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_InsufficientBalance_IsRejectedAndNothingIsDelivered()
    {
        var player = await this.CreatePlayerWithBalanceAsync(Price - 1);
        this.SetupValidCatalog();

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.InsufficientBalance));
        Assert.That(await this.GetWalletBalanceAsync(), Is.EqualTo(Price - 1));
        Assert.That(await this.GetStoredItemsAsync(), Is.Empty, "No item may be delivered when the balance is insufficient.");
    }

    /// <summary>
    /// Storage cheio devolve o código 2 do cliente (falta de espaço no storage) e não cobra.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_FullStorage_ReturnsNoDeliverySpace()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupValidCatalog();
        this.SetupStorageReservation(-1, StorageIndexFull: true);

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.NoDeliverySpace));
            Assert.That(await this.GetWalletBalanceAsync(), Is.EqualTo(1000));
    }

    /// <summary>
    /// PropertySeq não é enviado pelo D2 03: linhas que diferem apenas nesse atributo
    /// representam variantes do mesmo produto lógico e devem resultar numa única compra.
    /// </summary>
    /// <summary>
    /// Duas variantes que divergem em StorageGroup nao sao intercambiaveis: o item seria
    /// entregue em outro storage. A selecao deve ser recusada como conflito real.
    /// </summary>
    /// <summary>
    /// UnitName divergente entre linhas complementares (Sec. vs Day(s)) e o formato real do
    /// catalogo para os atributos 37/53; como 37/53 nao sao lidos como duracao, a oferta nao
    /// fica ambigua e a compra passa.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_DivergingUnitName_IsAcceptedAsComplementary()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        var first = this.CreateProduct(PropertySeq: 10, productPrice: Price);
        var second = this.CreateProduct(PropertySeq: 37, productPrice: Price);
        first.UnitName = "Day(s)";
        second.UnitName = "Hour(s)";
        this.SetupValidCatalog(products: new[] { first, second });

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, "UnitName divergence between complementary rows is part of the catalog shape: {0}", result.ErrorMessage);
            Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.None));
        });
        Assert.That(await this.GetWalletBalanceAsync(), Is.EqualTo(1000 - Price));
    }

    /// <summary>
    /// UnitType divergente entre linhas complementares descreve a unidade de cada atributo, nao
    /// moedas distintas: a moeda vem de CashType e o cliente escolhe a variante pelo PriceSeq.
    /// O caso real e o pacote 35 (386 segundos vs 151 dias na mesma oferta) e precisa ser aceito.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_DivergingUnitType_IsAcceptedAsComplementary()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        var first = this.CreateProduct(PropertySeq: 10, productPrice: Price);
        var second = this.CreateProduct(PropertySeq: 37, productPrice: Price);
        first.UnitType = 151;
        second.UnitType = 680;
        this.SetupValidCatalog(products: new[] { first, second });

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, "UnitType divergence between complementary rows is complementary catalog data: {0}", result.ErrorMessage);
            Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.None));
        });
        Assert.That(await this.GetWalletBalanceAsync(), Is.EqualTo(1000 - Price));
    }

    /// <summary>
    /// Linhas complementares da mesma oferta podem divergir em MustFlag: o atributo e lido por
    /// linha pelo cliente, e as linhas reais do pacote 35 divergem nesse campo (10 = true,
    /// 37/53 = false). Divergencia real e uma segunda linha do MESMO atributo com valor diferente.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_DivergingMustFlag_IsAcceptedAsComplementary()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        var first = this.CreateProduct(PropertySeq: 10, productPrice: Price);
        var second = this.CreateProduct(PropertySeq: 37, productPrice: Price);
        first.MustFlag = true;
        second.MustFlag = false;
        this.SetupValidCatalog(products: new[] { first, second });

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, "MustFlag divergence between complementary rows is part of the catalog shape: {0}", result.ErrorMessage);
            Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.None));
        });
        Assert.That(await this.GetWalletBalanceAsync(), Is.EqualTo(1000 - Price));
    }

    /// <summary>
    /// ShareFlag altera como o item e entregue; variantes divergentes nao podem ser agrupadas.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_DivergingShareFlag_IsRejected()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        var first = this.CreateProduct(PropertySeq: 10, productPrice: Price);
        var second = this.CreateProduct(PropertySeq: 37, productPrice: Price);
        first.ShareFlag = true;
        second.ShareFlag = false;
        this.SetupValidCatalog(products: new[] { first, second });

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);
        var balance = await this.GetWalletBalanceAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.SelectionUnavailable), "A different ShareFlag must be refused as a conflict.");
            Assert.That(balance, Is.EqualTo(1000), "A refused selection must not debit the wallet.");
        });
    }

    /// <summary>
    /// O filtro de produto ativo roda antes do agrupamento: uma variante apagada e descartada,
    /// e as variantes validas restantes ainda resolvem um unico produto logico.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_DeletedVariant_IsFilteredBeforeGrouping()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        var kept = this.CreateProduct(PropertySeq: 10, productPrice: Price);
        var alsoKept = this.CreateProduct(PropertySeq: 37, productPrice: Price);
        var deleted = this.CreateProduct(PropertySeq: 53, productPrice: Price);
        deleted.DeleteFlag = 145;
        this.SetupValidCatalog(products: new[] { kept, alsoKept, deleted });

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);
        var balance = await this.GetWalletBalanceAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.None), "A deleted variant is filtered out and must not create a conflict.");
            Assert.That(balance, Is.EqualTo(1000 - Price), "The remaining variants resolve one logical product.");
        });
    }

    /// <summary>
    /// O filtro por ItemCode roda antes do agrupamento: um produto de outro item nao entra no grupo.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_AnotherItemCode_IsFilteredBeforeGrouping()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        var first = this.CreateProduct(PropertySeq: 10, productPrice: Price);
        var second = this.CreateProduct(PropertySeq: 37, productPrice: Price);
        var third = this.CreateProduct(PropertySeq: 53, productPrice: Price);
        var other = this.CreateProduct(PropertySeq: 1, productPrice: Price);
        other.InGamePackageID = ((ItemGroup * 512) + 99).ToString(System.Globalization.CultureInfo.InvariantCulture);
        this.SetupValidCatalog(products: new[] { first, second, third, other });

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);
        var balance = await this.GetWalletBalanceAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.None), "A product for another item must be filtered out before the grouping.");
            Assert.That(balance, Is.EqualTo(1000 - Price), "The three matching variants resolve one logical product.");
        });
    }

    /// <summary>
    /// O filtro por PriceSeq roda antes do agrupamento: um produto de outra sequencia de preco
    /// nao entra no grupo, mesmo com o mesmo ProductSeq.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_AnotherPriceSeq_IsFilteredBeforeGrouping()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        var first = this.CreateProduct(PropertySeq: 10, productPrice: Price);
        var second = this.CreateProduct(PropertySeq: 37, productPrice: Price);
        var otherPriceSeq = this.CreateProduct(PropertySeq: 53, productPrice: Price + 100);
        otherPriceSeq.PriceSeq = PriceSeq + 1;
        this.SetupValidCatalog(products: new[] { first, second, otherPriceSeq });

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);
        var balance = await this.GetWalletBalanceAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.None), "A product of another price sequence must be filtered out before the grouping.");
            Assert.That(balance, Is.EqualTo(1000 - Price), "The two matching variants resolve one logical product.");
        });
    }

    [Test]
    public async Task ProcessPurchaseAsync_DivergingStorageGroup_IsRejected()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        var first = this.CreateProduct(PropertySeq: 10, productPrice: Price);
        var second = this.CreateProduct(PropertySeq: 37, productPrice: Price);
        second.StorageGroup = 9;
        this.SetupValidCatalog(products: new[] { first, second });

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);
        var balance = await this.GetWalletBalanceAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.SelectionUnavailable), "A different storage group must be refused as a conflict.");
            Assert.That(balance, Is.EqualTo(1000), "A refused selection must not debit the wallet.");
        });
    }

    public async Task ProcessPurchaseAsync_PropertyVariants_ResolveAsOneLogicalProduct()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupValidCatalog(products: new[]
        {
            this.CreateProduct(PropertySeq: 10, productPrice: Price),
            this.CreateProduct(PropertySeq: 37, productPrice: Price),
            this.CreateProduct(PropertySeq: 53, productPrice: Price),
        });

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.None));
        Assert.That(await this.GetWalletBalanceAsync(), Is.EqualTo(1000 - Price));
        var storedItems = await this.GetStoredItemsAsync();
        Assert.That(storedItems, Has.Count.EqualTo(1));
        Assert.That(storedItems[0].ProductSeq, Is.EqualTo(ProductSeq));
        Assert.That(storedItems[0].PriceSeq, Is.EqualTo(PriceSeq));
        Assert.That(storedItems[0].Item?.Definition?.Group, Is.EqualTo(ItemGroup));
        Assert.That(storedItems[0].Item?.Definition?.Number, Is.EqualTo(ItemNumber));
    }

    /// <summary>
    /// As tres linhas reais do pacote 35 em IBSProduct.txt (versao 512.2012.084) descrevem uma
    /// unica oferta complementar: apos a semantica de agregacao, a compra e aceita e entregue.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_RealPackage35PropertySet_ResolvesAndDelivers()
    {
        // Product fields are copied from IBSProduct.txt lines 283–285 in version 512.2012.084.
        // Only the package validity window is adjusted in this fixture: this reproduces the
        // OPENMU_CASH_SHOP_REOPEN_PACKAGES effect without changing any product property.
        // InGamePackageID 6676 = group 13, number 20.
        this._configuration.Items.Add(new ItemDefinition { Group = 13, Number = 20, Name = "Wizard Ring", Width = 2, Height = 2 });
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        var package = new CashShopPackage
        {
            ProductDisplaySeq = 31,
            ViewOrder = 3,
            PackageProductSeq = 35,
            PackageProductName = "[Wizard Ring]",
            PackageProductType = 170,
            Price = 100,
            Description = "Attack(Wizardry) power +10%#Attack speed increase +10#100 W Coin - 1Day#500 W Coin - 7Day",
            Caution = string.Empty,
            SalesFlag = 182,
            GiftFlag = 184,
            CapsuleFlag = 177,
            CapsuleCount = 1,
            ProductCashName = "W Coin(P)",
            PriceUnitName = "W Coin(P)",
            DeleteFlag = 181,
            EventFlag = 200,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(1),
            ProductSeqList = "[58]",
            InGamePackageID = "6676",
            ProductCashSeq = 3,
            PriceCount = 2,
            PriceSeqList = "[95,96]",
            ProductAmount = 0,
            DeductMileageFlag = false,
            CashType = 509,
            CashTypeFlag = 669,
        };
        this._catalog
            .Setup(c => c.GetPackageBySeqAsync(31, 35, It.IsAny<GameConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(package);

        var products = new[]
        {
            new CashShopProduct
            {
                ProductSeq = 58,
                ProductName = "Wizard Ring",
                PropertyName = "Duration",
                Value = "86400",
                UnitName = "Sec.",
                Price = 100,
                PriceSeq = 95,
                PropertyType = 142,
                MustFlag = true,
                ViewOrder = 1,
                DeleteFlag = 144,
                StorageGroup = 673,
                ShareFlag = true,
                InGamePackageID = "6676",
                PropertySeq = 10,
                ProductType = 140,
                UnitType = 386,
            },
            new CashShopProduct
            {
                ProductSeq = 58,
                ProductName = "Wizard Ring",
                PropertyName = "기간제-감가상각기간",
                Value = "365",
                UnitName = "Day(s)",
                Price = 100,
                PriceSeq = 95,
                PropertyType = 142,
                MustFlag = false,
                ViewOrder = 9,
                DeleteFlag = 144,
                StorageGroup = 673,
                ShareFlag = true,
                InGamePackageID = "6676",
                PropertySeq = 37,
                ProductType = 140,
                UnitType = 151,
            },
            new CashShopProduct
            {
                ProductSeq = 58,
                ProductName = "Wizard Ring",
                PropertyName = "기간아이템 유효기간",
                Value = "365",
                UnitName = "Day(s)",
                Price = 100,
                PriceSeq = 95,
                PropertyType = 142,
                MustFlag = false,
                ViewOrder = 9,
                DeleteFlag = 144,
                StorageGroup = 673,
                ShareFlag = true,
                InGamePackageID = "6676",
                PropertySeq = 53,
                ProductType = 140,
                UnitType = 151,
            },
        };
        this._catalog
            .Setup(c => c.GetPackageProductsByPriceSeqAsync(It.IsAny<int[]>(), It.IsAny<int>(), It.IsAny<GameConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int[] productSequences, int requestedPriceSeq, GameConfiguration _, CancellationToken _) =>
                products
                    .Where(product => product.PriceSeq == requestedPriceSeq && productSequences.Contains(product.ProductSeq))
                    .ToList());

        var balanceBefore = await this.GetWalletBalanceAsync();
        var storageBefore = await this.GetStoredItemsAsync();

        var result = await this._service.ProcessPurchaseAsync(player, 35, 31, 95, 6676, 509, false);

        var balanceAfter = await this.GetWalletBalanceAsync();
        var storageAfter = await this.GetStoredItemsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, "The real package 35 property set is complementary and must be accepted: {0}", result.ErrorMessage);
            Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.None));
            Assert.That(balanceAfter, Is.EqualTo(balanceBefore - 100), "The 1-day variant costs 100 WCoinP.");
            Assert.That(storageBefore, Is.Empty);
            Assert.That(storageAfter, Has.Count.EqualTo(1), "A successful purchase delivers to Cash Shop Storage.");
            Assert.That(storageAfter[0].ProductSeq, Is.EqualTo(58));
            Assert.That(storageAfter[0].PriceSeq, Is.EqualTo(95));
        });

        this._catalog.Verify(c => c.GetPackageBySeqAsync(31, 35, It.IsAny<GameConfiguration>(), It.IsAny<CancellationToken>()), Times.Once);
        this._catalog.Verify(c => c.GetPackageProductsByPriceSeqAsync(
            It.Is<int[]>(sequences => sequences.Length == 1 && sequences[0] == 58),
            95,
            It.IsAny<GameConfiguration>(),
            It.IsAny<CancellationToken>()), Times.Once);
        this._storageService.Verify(s => s.ReserveNextStorageIndexAsync(
            It.IsAny<IContext>(),
            It.IsAny<Guid>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Characterizes that the current purchase flow rejects package 34's CashType before product grouping.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_RealPackage34CashType508_IsRejectedWithoutDebit()
    {
        // Only the validity window is adjusted because the local IBS package is expired;
        // this makes the test reach the current CashType validation without changing catalog properties.
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        var package = new CashShopPackage
        {
            ProductDisplaySeq = 24,
            ViewOrder = 3,
            PackageProductSeq = 34,
            PackageProductName = "Wizard Ring",
            PackageProductType = 170,
            Price = 100,
            Description = "Attack(Wizardry) power +10%#Attack speed increase +10#100 W Coin - 1Day#500 W Coin - 7Day",
            Caution = string.Empty,
            SalesFlag = 182,
            GiftFlag = 185,
            CapsuleFlag = 177,
            CapsuleCount = 1,
            ProductCashName = "W Coin(C)",
            PriceUnitName = "W Coin(C)",
            DeleteFlag = 181,
            EventFlag = 200,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(1),
            ProductSeqList = "[58]",
            InGamePackageID = "6676",
            ProductCashSeq = 2,
            PriceCount = 2,
            PriceSeqList = "[95,96]",
            ProductAmount = 0,
            DeductMileageFlag = false,
            CashType = 508,
            CashTypeFlag = 669,
        };
        this._catalog
            .Setup(c => c.GetPackageBySeqAsync(24, 34, It.IsAny<GameConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(package);

        var balanceBefore = await this.GetWalletBalanceAsync();
        var storageBefore = await this.GetStoredItemsAsync();

        var result = await this._service.ProcessPurchaseAsync(player, 34, 24, 95, 6676, 508, false);

        var balanceAfter = await this.GetWalletBalanceAsync();
        var storageAfter = await this.GetStoredItemsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.InvalidCoinType));
            Assert.That(result.ErrorMessage, Is.EqualTo("The package is not purchasable with WCoinP."));
            Assert.That(result.DeliveredItem, Is.Null, "A rejected purchase must not report an item delivery.");
            Assert.That(balanceAfter, Is.EqualTo(balanceBefore), "An unsupported CashType must not debit the wallet.");
            Assert.That(storageBefore, Is.Empty);
            Assert.That(storageAfter, Is.EqualTo(storageBefore), "A rejected purchase must not change Cash Shop Storage.");
        });

        this._catalog.Verify(c => c.GetPackageBySeqAsync(24, 34, It.IsAny<GameConfiguration>(), It.IsAny<CancellationToken>()), Times.Once);
        this._catalog.Verify(c => c.GetPackageProductsByPriceSeqAsync(
            It.IsAny<int[]>(),
            It.IsAny<int>(),
            It.IsAny<GameConfiguration>(),
            It.IsAny<CancellationToken>()), Times.Never);
        this._storageService.Verify(s => s.ReserveNextStorageIndexAsync(
            It.IsAny<IContext>(),
            It.IsAny<Guid>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Produtos com o mesmo price sequence e preços diferentes tornam o catálogo inconsistente
    /// e a compra é recusada em vez de cobrar um valor arbitrário.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_InconsistentPrices_AreRejected()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupValidCatalog(products: new[]
        {
            this.CreateProduct(PropertySeq: 1, productPrice: Price),
            this.CreateProduct(PropertySeq: 2, productPrice: Price + 1),
        });

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.CannotBeBought));
            Assert.That(await this.GetWalletBalanceAsync(), Is.EqualTo(1000));
    }

    [Test]
    public async Task ProcessPurchaseAsync_ConflictingLogicalProductSequences_AreRejected()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupValidCatalog(
            package =>
                package.ProductSeqList = $"[{ProductSeq},{ProductSeq + 1}]",
            products: new[]
            {
                this.CreateProduct(PropertySeq: 10, productPrice: Price),
                this.CreateProduct(PropertySeq: 37, productPrice: Price, productSequence: ProductSeq + 1),
            });

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.SelectionUnavailable));
            Assert.That(await this.GetWalletBalanceAsync(), Is.EqualTo(1000));
        Assert.That(await this.GetStoredItemsAsync(), Is.Empty);
    }

    /// <summary>
    /// O produto aponta para um item code que o cliente pediu, mas esse item não existe na
    /// configuração do jogo: não há como instanciá-lo, então a compra é recusada.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_ItemNotInGameConfiguration_IsRejected()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupValidCatalog(products: new[] { this.CreateProduct(PropertySeq: 1, productPrice: Price) });

        // Remove a definição do item, mantendo o produto com o mesmo item code do request.
        var matching = this._configuration.Items.FirstOrDefault(item => item.Group == ItemGroup && item.Number == ItemNumber);
        if (matching is not null)
        {
            this._configuration.Items.Remove(matching);
        }

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.CannotBeBought));
            Assert.That(await this.GetWalletBalanceAsync(), Is.EqualTo(1000));
    }

    /// <summary>
    /// Um produto apagado (DeleteFlag diferente do valor ativo) não é elegível.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_DeletedProduct_IsRejected()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupValidCatalog(products: new[] { this.CreateProduct(PropertySeq: 1, productPrice: Price, deleteFlag: 0) });

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.SelectionUnavailable));
            Assert.That(await this.GetWalletBalanceAsync(), Is.EqualTo(1000));
    }

    /// <summary>
    /// Um item de evento não pode ser comprado.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_EventItem_IsRejected()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupValidCatalog(package => package.EventFlag = 999);

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.EventItemCannotBeBought));
            Assert.That(await this.GetWalletBalanceAsync(), Is.EqualTo(1000));
    }

    /// <summary>
    /// Uma compra válida consome exatamente um crédito, mesmo com requisições repetidas em
    /// paralelo: o lock por conta serializa e a checagem de saldo acontece dentro dele.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_ConcurrentRequests_DebitOnlyWhatTheyCanAfford()
    {
        // Saldo para exatamente uma compra.
        var player = await this.CreatePlayerWithBalanceAsync(Price);
        this.SetupValidCatalog();

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false).AsTask()));

        Assert.That(results.Count(r => r.Success), Is.EqualTo(1), "Only one purchase may succeed with a single credit.");
            Assert.That(await this.GetWalletBalanceAsync(), Is.EqualTo(0));
        Assert.That(await this.GetStoredItemsAsync(), Has.Count.EqualTo(1), "Delivered items must match the successful purchases.");
    }

    /// <summary>
    /// Um <see cref="SaveChangesAsync"/> que lança não pode deixar o débito nem o extrato
    /// pendentes: um salvamento posterior do mesmo contexto não pode cobrar o jogador
    /// por uma compra que nunca foi entregue.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_SaveThrows_DoesNotLeaveDebitOrStatementPending()
    {
        var (player, context) = await this.CreatePlayerWithFailingSaveAsync(1000, new InvalidOperationException("Simulated save failure."));
        this.SetupValidCatalog();

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.TransactionFailed));

        // A later save of the very same context must not persist the aborted purchase.
        context.FailNextSave = false;
        await player.PersistenceContext.SaveChangesAsync();

        var stored = await this.GetStoredItemsAsync();
        var transactions = await this.GetTransactionsAsync();
        var balance = await this.GetWalletBalanceAsync();
        Assert.Multiple(() =>
        {
            Assert.That(balance, Is.EqualTo(1000), "The wallet must not be debited for a purchase that failed to persist.");
            Assert.That(stored, Is.Empty, "A failed purchase must not leave a storage row behind.");
            Assert.That(transactions, Is.Empty, "A failed purchase must not leave a statement behind.");
        });
    }

    /// <summary>
    /// Um <see cref="SaveChangesAsync"/> que devolve <c>false</c> sem lançar tem o mesmo efeito:
    /// nada da compra pode sobrar pendente para um salvamento posterior.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_SaveReturnsFalse_DoesNotLeaveDebitOrStatementPending()
    {
        var (player, context) = await this.CreatePlayerWithFailingSaveAsync(1000);
        this.SetupValidCatalog();
        context.ReturnFalse = true;

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.That(result.Success, Is.False);
        Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.TransactionFailed));

        context.ReturnFalse = false;
        context.FailNextSave = false;
        await player.PersistenceContext.SaveChangesAsync();

        var stored = await this.GetStoredItemsAsync();
        var transactions = await this.GetTransactionsAsync();
        var balance = await this.GetWalletBalanceAsync();
        Assert.Multiple(() =>
        {
            Assert.That(balance, Is.EqualTo(1000));
            Assert.That(stored, Is.Empty);
            Assert.That(transactions, Is.Empty);
        });
    }

    /// <summary>
    /// Uma compra bem-sucedida persiste junto o débito, o extrato, o item e a linha de storage.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_ValidPurchase_PersistsDebitStatementAndDeliveryTogether()
    {
        var player = await this.CreatePlayerWithBalanceAsync(1000);
        this.SetupValidCatalog();

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.That(result.Success, Is.True, result.ErrorMessage);

        var transactions = await this.GetTransactionsAsync();
        var stored = await this.GetStoredItemsAsync();
        var balance = await this.GetWalletBalanceAsync();
        Assert.Multiple(() =>
        {
            Assert.That(balance, Is.EqualTo(1000 - Price), "The wallet is debited.");
            Assert.That(transactions, Has.Count.EqualTo(1), "The statement is persisted with the purchase.");
            Assert.That(transactions[0].Type, Is.EqualTo(WCoinTransactionType.Purchase));
            Assert.That(transactions[0].Amount, Is.EqualTo(Price));
            Assert.That(transactions[0].BalanceAfter, Is.EqualTo(1000 - Price));
            Assert.That(stored, Has.Count.EqualTo(1), "The delivery is persisted with the purchase.");
            Assert.That(stored[0].Item, Is.Not.Null);
        });
    }

    /// <summary>
    /// Uma violação da constraint única <c>(AccountId, StorageIndex)</c> no commit — o que
    /// acontece quando outro processo ocupa o slot entre a reserva e a gravação — rejeita a
    /// entrega inteira, e o débito é revertido junto: o jogador não paga por nada.
    /// </summary>
    /// <remarks>
    /// A falha é injetada no <see cref="IContext.SaveChangesAsync"/> (e não na criação da linha),
    /// porque é no commit que o banco aplica a constraint única. O contexto em memória não
    /// reproduz constraints, então a violação precisa ser simulada nesse ponto — injetá-la antes
    /// testaria um caminho de erro diferente do que o PostgreSQL produz.
    /// </remarks>
    [Test]
    public async Task ProcessPurchaseAsync_StorageIndexConstraintViolation_DoesNotChargeThePlayer()
    {
        var (player, context) = await this.CreatePlayerWithFailingSaveAsync(
            1000,
            new InvalidOperationException("23505: duplicate key value violates unique constraint \"IX_CashShopStorageItem_AccountId_StorageIndex\""));
        this.SetupValidCatalog();

        // The whole unit of work is staged first and only then committed, so the save has to be
        // the failing step: the index the reservation handed out is already occupied by the
        // concurrent process and the unique index rejects the delivery.
        context.FailNextSave = true;

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.That(result.Success, Is.False, "A storage index conflict must not be reported as a success.");
        Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.TransactionFailed));

        // The context must be clean: a later save of the same context may not charge the player
        // or deliver the item that the rejected commit was supposed to persist.
        context.FailNextSave = false;
        await player.PersistenceContext.SaveChangesAsync();

        var stored = await this.GetStoredItemsAsync();
        var transactions = await this.GetTransactionsAsync();
        var balance = await this.GetWalletBalanceAsync();
        Assert.Multiple(() =>
        {
            Assert.That(balance, Is.EqualTo(1000), "A failed delivery must not be charged.");
            Assert.That(stored, Is.Empty, "A rejected storage row must not be persisted.");
            Assert.That(transactions, Is.Empty, "A rejected purchase must not leave a statement behind.");
        });
    }

    /// <summary>
    /// Uma falha de commit não pode deixar o saldo da carteira debitado em memória: o objeto da
    /// carteira é o mesmo que o serviço de WCOIN altera, e o rollback precisa restaurar a
    /// <i>mesma instância</i> — um salvamento posterior gravaria o saldo errado se a restauração
    /// falhasse.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_CommitFailure_RestoresTheWalletInstanceInMemory()
    {
        var (player, context) = await this.CreatePlayerWithFailingSaveAsync(1000, new InvalidOperationException("Simulated save failure."));
        this.SetupValidCatalog();

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.TransactionFailed));

        // The wallet instance the purchase debited must be back to its original balance, without
        // any reload: a later save of this context would otherwise persist the debited value.
        var wallet = await player.PersistenceContext.GetByIdAsync<WCoinWallet>(this._accountId);
        Assert.That(wallet, Is.Not.Null, "The wallet must still be tracked by the context.");
        Assert.That(wallet!.Balance, Is.EqualTo(1000), "The in-memory balance must be restored, not left debited.");

        context.FailNextSave = false;
        await player.PersistenceContext.SaveChangesAsync();
        Assert.That(await this.GetWalletBalanceAsync(), Is.EqualTo(1000), "The restored balance is what gets persisted.");
    }

    /// <summary>
    /// Um commit cancelado tem de desfazer o que foi preparado antes de propagar o
    /// cancelamento: caso contrário, um <c>SaveChangesAsync</c> posterior do mesmo contexto
    /// gravaria a entrega e o débito de uma compra que o cliente abandonou.
    /// </summary>
    /// <remarks>
    /// O cancelamento não pode ser convertido em <c>TransactionFailed</c> — quem chamou precisa
    /// distinguir "o servidor está encerrando" de "a compra falhou".
    /// </remarks>
    [Test]
    public async Task ProcessPurchaseAsync_CancelledCommit_RollsBackAndPropagatesCancellation()
    {
        // The wallet must exist and cover the price, otherwise the purchase is refused for
        // insufficient balance before it ever reaches the commit that this test cancels.
        var (player, context) = await this.CreatePlayerWithFailingSaveAsync(1000).ConfigureAwait(false);
        this.SetupValidCatalog();
        context.ThrowOperationCanceledWhenCancelled = true;

        // The save must fail *because* of the cancellation, not before it, so the generic
        // failure path does not short-circuit the test.
        context.FailNextSave = false;

        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false, source.Token).ConfigureAwait(false));

        // The reservation and the delivery must not survive the cancelled attempt. The in-memory
        // context adds every created entity to its repository immediately, so a leftover row
        // would be visible here.
        var stored = await this.GetStoredItemsAsync().ConfigureAwait(false);
        var transactions = await this.GetTransactionsAsync().ConfigureAwait(false);
        Assert.Multiple(() =>
        {
            Assert.That(stored, Is.Empty, "A cancelled purchase must not leave a storage row staged in the context.");
            Assert.That(transactions, Is.Empty, "A cancelled purchase must not leave a statement behind.");
        });
    }

    /// <summary>
    /// Um <c>Detach</c> que falha durante o rollback não pode ser engolido em silêncio: o
    /// contexto fica capaz de cobrar o jogador por uma compra que nunca foi entregue. O resultado
    /// tem de distinguir esse caso de uma falha limpa, e a sessão do jogador precisa sair do fluxo
    /// para que nenhum salvamento posterior persista o estado sujo.
    /// </summary>
    /// <remarks>
    /// Teste unitário com <see cref="InMemoryContext"/>: verifica o fluxo de controle do serviço
    /// (rollback, código de resultado e retirada da sessão). Não valida transação nem constraint
    /// de PostgreSQL.
    /// </remarks>
    [Test]
    public async Task ProcessPurchaseAsync_RollbackFailure_ReportsIncompleteAndRetiresSession()
    {
        var (player, context) = await this.CreatePlayerWithFailingSaveAsync(1000, new InvalidOperationException("Simulated save failure."));
        this.SetupValidCatalog();
        context.ThrowOnDetach = true;

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False, "A purchase whose rollback failed must not be reported as a success.");
            Assert.That(
                result.ErrorCode,
                Is.EqualTo(PurchaseErrorCode.RollbackIncomplete),
                "An incomplete rollback must be distinguishable from a clean transaction failure.");
            Assert.That(context.DetachCallCount, Is.GreaterThan(0), "The rollback must have attempted to detach the staged rows.");
        });

        // The session must have left the game flow, so no later save persists the dirty context.
        Assert.That(
            player.PlayerState.CurrentState,
            Is.EqualTo(PlayerState.Finished).Or.EqualTo(PlayerState.Disconnected),
            "A session with an unsafe context must be taken out of the game flow.");
        Assert.That(player.IsConnected, Is.False, "The player must no longer be connected after retirement.");
    }

    /// <summary>
    /// O mesmo para o caminho de cancelamento: um commit cancelado cujo rollback falha não pode
    /// deixar o contexto sujo em uso, e o cancelamento precisa continuar propagando — nunca virar
    /// sucesso nem um código de erro de protocolo inventado.
    /// </summary>
    /// <remarks>
    /// Teste unitário com <see cref="InMemoryContext"/>; não valida transação PostgreSQL.
    /// </remarks>
    [Test]
    public void ProcessPurchaseAsync_CancelledWithIncompleteRollback_RetiresSessionAndPropagates()
    {
        var (player, context) = this.CreatePlayerWithFailingSaveAsync(1000, new InvalidOperationException("Simulated save failure.")).GetAwaiter().GetResult();
        this.SetupValidCatalog();
        context.ThrowOnDetach = true;
        context.ThrowOperationCanceledWhenCancelled = true;
        context.FailNextSave = false;

        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false, source.Token).ConfigureAwait(false));

        Assert.That(
            player.PlayerState.CurrentState,
            Is.EqualTo(PlayerState.Finished).Or.EqualTo(PlayerState.Disconnected),
            "A cancelled purchase with an incomplete rollback must retire the session.");
    }

    /// <summary>
    /// O rollback de uma compra que falhou não corrompe o contexto: a próxima compra da mesma
    /// conta é entregue e cobrada normalmente.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_SuccessfulPurchaseAfterFailedSave_StillDelivers()
    {
        var (player, context) = await this.CreatePlayerWithFailingSaveAsync(1000, new InvalidOperationException("Simulated save failure."));
        this.SetupValidCatalog();

        var failed = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);
        Assert.That(failed.Success, Is.False);

        context.FailNextSave = false;
        var retried = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false);

        Assert.That(retried.Success, Is.True, retried.ErrorMessage);
        var stored = await this.GetStoredItemsAsync();
        var transactions = await this.GetTransactionsAsync();
        var balance = await this.GetWalletBalanceAsync();
        Assert.Multiple(() =>
        {
            Assert.That(balance, Is.EqualTo(1000 - Price), "Only the successful purchase is charged.");
            Assert.That(stored, Has.Count.EqualTo(1));
            Assert.That(transactions, Has.Count.EqualTo(1), "Only the successful purchase has a statement.");
        });
    }

    /// <summary>
    /// A failed discard requires retirement even when the wallet refused the debit, and its
    /// result must distinguish successful retirement from a failed disconnection handler.
    /// </summary>
    /// <param name="balance">The wallet balance before the request.</param>
    /// <param name="retirementFails">Whether a disconnection handler fails.</param>
    [TestCase(1000, false)]
    [TestCase(1000, true)]
    [TestCase(0, false)]
    [TestCase(0, true)]
    public async Task ProcessPurchaseAsync_IncompleteRollback_ReportsActualRetirementOutcome(long balance, bool retirementFails)
    {
        var (player, context) = await this.CreatePlayerWithFailingSaveAsync(balance).ConfigureAwait(false);
        this.SetupValidCatalog();
        context.ThrowOnDetach = true;
        var retirementAttempted = false;
        player.PlayerDisconnected += _ =>
        {
            retirementAttempted = true;
            return retirementFails
                ? ValueTask.FromException(new InvalidOperationException("Simulated retirement failure."))
                : ValueTask.CompletedTask;
        };

        var result = await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.RollbackIncomplete));
            Assert.That(context.DetachCallCount, Is.GreaterThan(0));
            Assert.That(retirementAttempted, Is.True, "An unsafe reservation must trigger retirement even without a debit.");
            if (retirementFails)
            {
                Assert.That(result.ErrorMessage, Does.Contain("Session retirement could not be completed; manual inspection is required."));
                Assert.That(result.ErrorMessage, Does.Not.Contain("session was retired").IgnoreCase);
            }
            else
            {
                Assert.That(result.ErrorMessage, Does.Contain("session was retired").IgnoreCase);
            }
        });
    }

    /// <summary>
    /// Cancellation at commit must survive failures in both rollback and session retirement.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_CancelledCommitAndFailedRetirement_PreservesCancellation()
    {
        var (player, context) = await this.CreatePlayerWithFailingSaveAsync(1000).ConfigureAwait(false);
        this.SetupValidCatalog();
        context.ThrowOnDetach = true;
        context.ThrowOperationCanceledWhenCancelled = true;
        using var source = new CancellationTokenSource();
        context.BeforeSave = source.Cancel;
        var retirementAttempted = false;
        player.PlayerDisconnected += _ =>
        {
            retirementAttempted = true;
            return ValueTask.FromException(new InvalidOperationException("Simulated retirement failure."));
        };

        var exception = Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await this._service.ProcessPurchaseAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false, source.Token).ConfigureAwait(false));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.CancellationToken, Is.EqualTo(source.Token));
            Assert.That(context.DetachCallCount, Is.GreaterThan(0), "Cancellation must occur after staging, not at lock acquisition.");
            Assert.That(retirementAttempted, Is.True);
        });
    }

    private async ValueTask<IReadOnlyList<WCoinTransaction>> GetTransactionsAsync()
    {
        var repository = this._contextProvider.RepositoryProvider.GetRepository<WCoinTransaction>();
        if (repository is null)
        {
            return Array.Empty<WCoinTransaction>();
        }

        var transactions = await repository.GetAllAsync().ConfigureAwait(false);
        return transactions.ToList();
    }

    /// <summary>
    /// Um <see cref="IContext"/> que delega tudo ao contexto real, mas faz o
    /// <see cref="IContext.SaveChangesAsync"/> falhar, para exercitar o caminho de erro do commit.
    /// </summary>
    internal sealed class FailingSaveContext : IPlayerContext
    {
        private readonly IContext _inner;
        private readonly Exception? _exception;

        public FailingSaveContext(IContext inner, Exception? exception = null)
        {
            this._inner = inner;
            this._exception = exception;
        }

        /// <summary>
        /// Gets or sets a value indicating whether the next save still has to fail.
        /// </summary>
        public bool FailNextSave { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether a failing save returns <c>false</c>
        /// instead of throwing.
        /// </summary>
        public bool ReturnFalse { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether a cancelled save throws
        /// <see cref="OperationCanceledException"/>, the way a real database round-trip does.
        /// </summary>
        public bool ThrowOperationCanceledWhenCancelled { get; set; }

        /// <summary>
        /// Gets or sets the action invoked when a commit is reached, after staging the purchase.
        /// </summary>
        public Action? BeforeSave { get; set; }

        public bool HasChanges => this._inner.HasChanges;

        public IDisposable SuspendChangeNotifications() => this._inner.SuspendChangeNotifications();

        public bool Detach(object item)
        {
            this.DetachCallCount++;
            if (this.ThrowOnDetach)
            {
                throw new InvalidOperationException("Simulated detach failure.");
            }

            return this._inner.Detach(item);
        }

        /// <summary>
        /// Gets or sets a value indicating whether detaching throws. This stands in for a rollback
        /// that cannot be completed, which the service must report instead of swallowing.
        /// </summary>
        public bool ThrowOnDetach { get; set; }

        /// <summary>
        /// Gets the number of <see cref="Detach"/> calls made through this context.
        /// </summary>
        public int DetachCallCount { get; private set; }

        public void Attach(object item) => this._inner.Attach(item);

        public T CreateNew<T>(params object?[] args)
            where T : class => this._inner.CreateNew<T>(args);

        public object CreateNew(Type type, params object?[] args) => this._inner.CreateNew(type, args);

        public ValueTask<bool> DeleteAsync<T>(T obj)
            where T : class => this._inner.DeleteAsync(obj);

        public Task<T?> GetByIdAsync<T>(Guid id, CancellationToken cancellationToken = default)
            where T : class => this._inner.GetByIdAsync<T>(id, cancellationToken);

        public Task<object?> GetByIdAsync(Guid id, Type type, CancellationToken cancellationToken = default) =>
            this._inner.GetByIdAsync(id, type, cancellationToken);

        public ValueTask<IEnumerable<T>> GetAsync<T>(CancellationToken cancellationToken = default)
            where T : class => this._inner.GetAsync<T>(cancellationToken);

        public ValueTask<IEnumerable> GetAsync(Type type, CancellationToken cancellationToken = default) =>
            this._inner.GetAsync(type, cancellationToken);

        public bool IsSupporting(Type type) => this._inner.IsSupporting(type);

        public async ValueTask<bool> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            this.BeforeSave?.Invoke();
            if (this.ThrowOperationCanceledWhenCancelled && cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (this.FailNextSave)
            {
                if (this.ReturnFalse)
                {
                    return false;
                }

                throw this._exception ?? new InvalidOperationException("Simulated save failure.");
            }

            return await this._inner.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            // The inner context is owned by the player, so it must not be disposed here.
        }

        public ValueTask<LetterBody?> GetLetterBodyByHeaderIdAsync(Guid headerId, CancellationToken cancellationToken = default) =>
            ((IPlayerContext)this._inner).GetLetterBodyByHeaderIdAsync(headerId, cancellationToken);

        public ValueTask<bool> CanSaveLetterAsync(LetterHeader letterHeader, CancellationToken cancellationToken = default) =>
            ((IPlayerContext)this._inner).CanSaveLetterAsync(letterHeader, cancellationToken);

        public ValueTask<AccountState?> AuthenticateAsync(string loginName, string password, CancellationToken cancellationToken = default) =>
            ((IPlayerContext)this._inner).AuthenticateAsync(loginName, password, cancellationToken);

        public ValueTask<Account?> GetAccountByLoginNameAsync(string loginName, string password, CancellationToken cancellationToken = default) =>
            ((IPlayerContext)this._inner).GetAccountByLoginNameAsync(loginName, password, cancellationToken);

        public ValueTask<Account?> GetAccountByLoginNameAsync(string loginName, CancellationToken cancellationToken = default) =>
            ((IPlayerContext)this._inner).GetAccountByLoginNameAsync(loginName, cancellationToken);

        public ValueTask<IEnumerable<Account>> GetAccountsOrderedByLoginNameAsync(int skip, int count, CancellationToken cancellationToken = default) =>
            ((IPlayerContext)this._inner).GetAccountsOrderedByLoginNameAsync(skip, count, cancellationToken);

        public ValueTask<Account?> GetAccountByCharacterNameAsync(string characterName, CancellationToken cancellationToken = default) =>
            ((IPlayerContext)this._inner).GetAccountByCharacterNameAsync(characterName, cancellationToken);
    }

    /// <summary>
    /// Um <see cref="IPersistenceContextProvider"/> que entrega ao jogador o contexto de
    /// persistência envolvido pelo <see cref="FailingSaveContext"/>. É necessário porque
    /// <see cref="Player.PersistenceContext"/> é somente leitura e é criado pelo provider.
    /// </summary>
    internal sealed class FailingContextProvider : IPersistenceContextProvider
    {
        private readonly IPersistenceContextProvider _inner;
        private readonly Exception? _exception;

        public FailingContextProvider(IPersistenceContextProvider inner, Exception? exception)
        {
            this._inner = inner;
            this._exception = exception;
        }

        /// <summary>
        /// Gets the wrapped context that was handed to the player.
        /// </summary>
        public FailingSaveContext? PlayerContext { get; private set; }

        public IRepositoryProvider RepositoryProvider => this._inner.RepositoryProvider;

        public IContext CreateNewContext() => this._inner.CreateNewContext();

        public IContext CreateNewContext(GameConfiguration gameConfiguration) => this._inner.CreateNewContext(gameConfiguration);

        public IConfigurationContext CreateNewConfigurationContext() => this._inner.CreateNewConfigurationContext();

        public IContext CreateNewTradeContext() => this._inner.CreateNewTradeContext();

        public IPlayerContext CreateNewPlayerContext(GameConfiguration gameConfiguration)
        {
            var wrapped = new FailingSaveContext(this._inner.CreateNewPlayerContext(gameConfiguration), this._exception);
            this.PlayerContext = wrapped;
            return wrapped;
        }

        public IFriendServerContext CreateNewFriendServerContext() => this._inner.CreateNewFriendServerContext();

        public IGuildServerContext CreateNewGuildContext() => this._inner.CreateNewGuildContext();

        public IContext CreateNewTypedContext(Type editType, bool useCache, GameConfiguration? gameConfiguration = null) =>
            this._inner.CreateNewTypedContext(editType, useCache, gameConfiguration);
    }
}
