// <copyright file="CashShopGiftServiceTests.cs" company="MUnique">
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
/// Testes do fluxo de presente da Cash Shop (<c>C1 D2 04</c>).
/// </summary>
/// <remarks>
/// O presente é validado pelas mesmas regras de catálogo da compra (<c>D2 03</c>, via
/// <see cref="CashShopSelectionResolver"/>), mas é cobrado do remetente e entregue na conta
/// <b>do destinatário</b>. Estes testes fixam esse contrato, sobretudo a garantia de que uma
/// falha ou um cancelamento não deixam débito nem presente parcial.
/// </remarks>
[TestFixture]
public class CashShopGiftServiceTests
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
    private const string ReceiverName = "Friend";
    private const int GiftableGiftFlag = 184;
    private const int NonGiftableGiftFlag = 182;

    private InMemoryPersistenceContextProvider _contextProvider = null!;
    private GameConfiguration _configuration = null!;
    private Mock<ICashShopCatalogService> _catalog = null!;
    private Mock<ICashShopStorageService> _storageService = null!;
    private CashShopGiftService _service = null!;
    private CashShopBuyService _buyService = null!;
    private IGameContext _gameContext = null!;
    private Guid _senderAccountId;
    private Guid _receiverAccountId;

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

        this._gameContext = CreateGameContext(this._contextProvider, this._configuration);

        this._catalog = new Mock<ICashShopCatalogService>();
        this._catalog
            .Setup(c => c.GetPackageBySeqAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<GameConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CashShopPackage?)null);
        this._catalog
            .Setup(c => c.GetPackageProductsByPriceSeqAsync(It.IsAny<int[]>(), It.IsAny<int>(), It.IsAny<GameConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<CashShopProduct>());

        this._storageService = new Mock<ICashShopStorageService>();
        this._storageService.Setup(s => s.Capacity).Returns(30);
        this.SetupStorageReservation(0, StorageIndexFull: false);

        var resolver = new CashShopSelectionResolver(this._catalog.Object, NullLogger<CashShopSelectionResolver>.Instance);
        this._buyService = new CashShopBuyService(resolver, this._storageService.Object, NullLogger<CashShopBuyService>.Instance, new UnsafeContextRetirement(NullLogger<UnsafeContextRetirement>.Instance));
        this._service = new CashShopGiftService(resolver, this._storageService.Object, NullLogger<CashShopGiftService>.Instance, new UnsafeContextRetirement(NullLogger<UnsafeContextRetirement>.Instance));
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

    private void SetupValidCatalog(Action<CashShopPackage>? configurePackage = null, IReadOnlyList<CashShopProduct>? products = null)
    {
        var package = new CashShopPackage
        {
            ProductDisplaySeq = CategoryProductDisplaySeq,
            PackageProductSeq = PackageProductSeq,
            PackageProductName = "Test Package",
            DeleteFlag = ActivePackageDeleteFlag,
            SalesFlag = SalesFlagAvailable,
            CashType = WCoinPCashType,
            CashTypeFlag = NormalCashTypeFlag,
            PackageProductType = NormalPackageType,
            EventFlag = NormalEventFlag,
            GiftFlag = GiftableGiftFlag,
            ProductSeqList = $"[{ProductSeq}]",
            PriceSeqList = $"[{PriceSeq}]",
        };
        configurePackage?.Invoke(package);

        this._catalog
            .Setup(c => c.GetPackageBySeqAsync(CategoryProductDisplaySeq, PackageProductSeq, It.IsAny<GameConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(package);
        this._catalog
            .Setup(c => c.GetPackageProductsByPriceSeqAsync(It.IsAny<int[]>(), It.IsAny<int>(), It.IsAny<GameConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(products ?? new[] { CreateProduct(PropertySeq: 1, productPrice: Price) });
    }

    private static CashShopProduct CreateProduct(int PropertySeq, int productPrice, int deleteFlag = ActiveProductDeleteFlag, string? inGamePackageId = null)
    {
        return new CashShopProduct
        {
            ProductSeq = ProductSeq,
            PriceSeq = PriceSeq,
            PropertySeq = PropertySeq,
            ProductName = "Test Product",
            Price = productPrice,
            DeleteFlag = deleteFlag,
            StorageGroup = 3,
            InGamePackageID = inGamePackageId ?? ItemCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    /// <summary>
    /// Configura a reserva de índice como o serviço real se comporta: cria a linha no contexto
    /// que grava a entrega, ou devolve <see cref="CashShopStorageReservation.Empty"/> se cheio.
    /// </summary>
    private void SetupStorageReservation(int storageIndex, bool StorageIndexFull)
    {
        this._storageService
            .Setup(s => s.ReserveNextStorageIndexAsync(It.IsAny<IContext>(), It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns((IContext context, Guid accountId, bool forGift, CancellationToken _) =>
            {
                if (StorageIndexFull)
                {
                    return ValueTask.FromResult(CashShopStorageReservation.Empty);
                }

                var storageItem = context.CreateNew<CashShopStorageItem>();
                storageItem.AccountId = accountId;
                storageItem.StorageIndex = storageIndex;
                storageItem.IsGift = forGift;
                return ValueTask.FromResult(new CashShopStorageReservation(storageItem, storageIndex));
            });
    }

    /// <summary>
    /// Cria o remetente com carteira e um destinatário em outra conta, para que o lookup por
    /// nome de personagem resolva a conta correta.
    /// </summary>
    private async ValueTask _seedReceiverAsync()
    {
        this._receiverAccountId = Guid.NewGuid();
        using var context = this._contextProvider.CreateNewContext(this._configuration);
        var receiverAccount = context.CreateNew<Account>();
        receiverAccount.Id = this._receiverAccountId;
        var receiverCharacter = context.CreateNew<Character>();
        receiverCharacter.Id = Guid.NewGuid();
        receiverCharacter.Name = ReceiverName;
        receiverAccount.Characters.Add(receiverCharacter);
        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    private async ValueTask<Player> CreateSenderAsync(long balance)
    {
        var player = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);
        player.SelectedCharacter!.Name = "Sender";
        this._senderAccountId = Guid.NewGuid();
        player.Account!.Id = this._senderAccountId;

        using var context = this._contextProvider.CreateNewContext(this._configuration);
        var wallet = context.CreateNew<WCoinWallet>();
        wallet.Id = this._senderAccountId;
        wallet.Balance = balance;
        await context.SaveChangesAsync().ConfigureAwait(false);

        await this._seedReceiverAsync().ConfigureAwait(false);

        return player;
    }

    private async ValueTask<PurchaseResult> SendGiftAsync(Player player, string receiverName, string giftText = "For you", CancellationToken cancellationToken = default)
    {
        return await this._service.ProcessGiftAsync(
            player,
            PackageProductSeq,
            CategoryProductDisplaySeq,
            PriceSeq,
            ItemCode,
            WCoinPCashType,
            false,
            receiverName,
            giftText,
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<(Player Player, CashShopBuyServiceTests.FailingSaveContext Context)> CreateSenderWithFailingSaveAsync(long balance)
    {
        var provider = new CashShopBuyServiceTests.FailingContextProvider(this._contextProvider, null);
        this._gameContext = CreateGameContext(provider, this._configuration);
        var player = await this.CreateSenderAsync(balance).ConfigureAwait(false);

        // The real resolver deliberately blocks gifts until GiftFlag is understood. Supply a
        // resolved selection only for these service failure tests; the production guard remains
        // covered by the catalog tests below and is not changed by this fixture.
        var resolver = new Mock<ICashShopSelectionResolver>();
        var product = CreateProduct(1, Price);
        resolver.Setup(r => r.ResolveAsync(player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CashShopSelectionResult.Resolved(product.ProductSeq, product.StorageGroup, product.ProductName, product.Price, PriceSeq, this._configuration.Items.Single()));
        this._service = new CashShopGiftService(resolver.Object, this._storageService.Object, NullLogger<CashShopGiftService>.Instance, new UnsafeContextRetirement(NullLogger<UnsafeContextRetirement>.Instance));
        return (player, provider.PlayerContext!);
    }

    /// <summary>
    /// Dirty gift reservations require retirement even without a debit, and failure to retire
    /// the sender must not be reported as completed retirement.
    /// </summary>
    /// <param name="balance">The sender's wallet balance.</param>
    /// <param name="retirementFails">Whether a disconnection handler fails.</param>
    [TestCase(1000, false)]
    [TestCase(1000, true)]
    [TestCase(0, false)]
    [TestCase(0, true)]
    public async Task ProcessGiftAsync_IncompleteRollback_ReportsActualRetirementOutcome(long balance, bool retirementFails)
    {
        var (player, context) = await this.CreateSenderWithFailingSaveAsync(balance).ConfigureAwait(false);
        context.ThrowOnDetach = true;
        var retirementAttempted = false;
        player.PlayerDisconnected += _ =>
        {
            retirementAttempted = true;
            return retirementFails
                ? ValueTask.FromException(new InvalidOperationException("Simulated retirement failure."))
                : ValueTask.CompletedTask;
        };

        var result = await this.SendGiftAsync(player, ReceiverName).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.RollbackIncomplete));
            Assert.That(context.DetachCallCount, Is.GreaterThan(0));
            Assert.That(retirementAttempted, Is.True, "An unsafe gift reservation must trigger retirement even without a debit.");
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
    /// Cancellation at commit must survive failures in both gift rollback and retirement.
    /// </summary>
    [Test]
    public async Task ProcessGiftAsync_CancelledCommitAndFailedRetirement_PreservesCancellation()
    {
        var (player, context) = await this.CreateSenderWithFailingSaveAsync(1000).ConfigureAwait(false);
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
            await this.SendGiftAsync(player, ReceiverName, cancellationToken: source.Token).ConfigureAwait(false));

        Assert.Multiple(() =>
        {
            Assert.That(exception!.CancellationToken, Is.EqualTo(source.Token));
            Assert.That(context.DetachCallCount, Is.GreaterThan(0), "Cancellation must occur after staging, not at lock acquisition.");
            Assert.That(retirementAttempted, Is.True);
        });
    }

    private async ValueTask<long> GetWalletBalanceAsync()
    {
        var wallet = await this._contextProvider.RepositoryProvider
            .GetRepository<WCoinWallet>()!.GetByIdAsync(this._senderAccountId).ConfigureAwait(false);
        return wallet?.Balance ?? 0;
    }

    private async ValueTask<IReadOnlyList<CashShopStorageItem>> GetStoredItemsAsync(Guid accountId)
    {
        var repository = this._contextProvider.RepositoryProvider.GetRepository<CashShopStorageItem>();
        if (repository is null)
        {
            return Array.Empty<CashShopStorageItem>();
        }

        var stored = await repository.GetAllAsync().ConfigureAwait(false);
        return stored.Where(item => item.AccountId == accountId).ToList();
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
    /// Pacote sem <c>GiftFlag = 184</c> nao pode ser presenteado: o botao de presente do cliente
    /// so habilita com 184 (<c>MsgBoxIGSBuySelectItem.cpp:67</c>). A recusa nao debita WCoin nem
    /// deixa estado pendente.
    /// </summary>
    [Test]
    public async Task ProcessGiftAsync_NonGiftableFlag_IsRejectedWithoutDebit()
    {
        var player = await this.CreateSenderAsync(1000).ConfigureAwait(false);
        this.SetupValidCatalog(package => package.GiftFlag = NonGiftableGiftFlag);

        var result = await this.SendGiftAsync(player, ReceiverName).ConfigureAwait(false);

        Assert.That(result.Success, Is.False, "A gift of a non-giftable package must be refused.");
        Assert.That(result.ErrorCode, Is.EqualTo(PurchaseErrorCode.CannotBeBought));

        var balance = await this.GetWalletBalanceAsync().ConfigureAwait(false);
        var receiverItems = await this.GetStoredItemsAsync(this._receiverAccountId).ConfigureAwait(false);
        var transactions = await this.GetTransactionsAsync().ConfigureAwait(false);
        Assert.Multiple(() =>
        {
            Assert.That(balance, Is.EqualTo(1000), "A refused gift must never charge the sender.");
            Assert.That(receiverItems, Is.Empty, "A refused gift must not deliver anything.");
            Assert.That(transactions, Is.Empty, "A refused gift must not leave a statement.");
        });

        // The context must be clean: a later save may not persist anything from the refusal.
        await player.PersistenceContext.SaveChangesAsync().ConfigureAwait(false);
        var afterSave = await this.GetStoredItemsAsync(this._receiverAccountId).ConfigureAwait(false);
        Assert.That(afterSave, Is.Empty, "A refused gift must not leave staged state behind.");
    }

    /// <summary>
    /// Somente <c>GiftFlag = 184</c> habilita o presente no cliente
    /// (<c>MsgBoxIGSBuySelectItem.cpp:67</c>): 0, 1 e 182 sao recusados sem debito.
    /// </summary>
    /// <param name="giftFlag">The catalog value under test.</param>
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(182)]
    public async Task ProcessGiftAsync_NonGiftableFlags_AreRejectedWithoutDebit(int giftFlag)
    {
        var player = await this.CreateSenderAsync(1000).ConfigureAwait(false);
        this.SetupValidCatalog(package => package.GiftFlag = giftFlag);

        var result = await this.SendGiftAsync(player, ReceiverName).ConfigureAwait(false);

        Assert.That(result.Success, Is.False, $"GiftFlag {giftFlag} is not giftable: only 184 enables the present button.");
        Assert.That(await this.GetWalletBalanceAsync().ConfigureAwait(false), Is.EqualTo(1000));
    }

    /// <summary>
    /// <c>GiftFlag = 184</c> habilita o botao de presente no cliente, e por isso o pacote tem de ser
    /// presenteavel. Este e o unico valor de <c>GiftFlag</c> com evidencia de codigo do cliente:
    /// <c>C:\MuMain\src\source\GameShop\MsgBoxIGSBuySelectItem.cpp:67</c> e
    /// <c>MsgBoxIGSBuyPackageItem.cpp:68</c> conditionam o botao exatamente assim:
    /// <c>if (pPackage-&gt;GiftFlag == 184) { m_BtnPresent.SetEnable(true); } else { m_BtnPresent.SetEnable(false); }</c>.
    /// <para>
    /// O numero nao vira aqui um booleano por suposicao: o teste reproduz o fluxo completo
    /// (remetente com saldo, destinatario valido, pacote ativo e moeda suportada) e so exige que a
    /// entrega chegue ao storage do destinatario e que o remetente seja cobrado uma unica vez.
    /// </para>
    /// </summary>
    [Test]
    public async Task ProcessGiftAsync_GiftFlag184_AllowsGiftDelivery()
    {
        // O valor real do catalogo (135 pacotes com 184 e 77 com 185 em IBSPackage.txt).
        const int catalogGiftFlag = 184;
        var player = await this.CreateSenderAsync(1000).ConfigureAwait(false);
        this.SetupValidCatalog(package => package.GiftFlag = catalogGiftFlag);

        var result = await this.SendGiftAsync(player, ReceiverName).ConfigureAwait(false);

        Assert.That(result.Success, Is.True, $"GiftFlag {catalogGiftFlag} habilita o presente no cliente: {result.ErrorMessage}");

        var senderBalance = await this.GetWalletBalanceAsync().ConfigureAwait(false);
        var receiverItems = await this.GetStoredItemsAsync(this._receiverAccountId).ConfigureAwait(false);
        var senderItems = await this.GetStoredItemsAsync(this._senderAccountId).ConfigureAwait(false);
        var transactions = await this.GetTransactionsAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(senderBalance, Is.EqualTo(1000 - Price), "O remetente paga o preco da oferta.");
            Assert.That(receiverItems, Has.Count.EqualTo(1), "A entrega vai para o storage do destinatario.");
            Assert.That(senderItems, Is.Empty, "Nenhum item pode ser entregue ao remetente.");
            Assert.That(transactions, Has.Count.EqualTo(1), "O debito e registrado uma unica vez.");
        });

        Assert.Multiple(() =>
        {
            Assert.That(receiverItems[0].IsGift, Is.True, "A linha no storage do destinatario e marcada como presente.");
            Assert.That(receiverItems[0].ProductSeq, Is.EqualTo(ProductSeq));
            Assert.That(receiverItems[0].PriceSeq, Is.EqualTo(PriceSeq));
            Assert.That(receiverItems[0].CashPoint, Is.EqualTo(Price));
            Assert.That(receiverItems[0].ItemType, Is.EqualTo(CashShopStorageItemType.Product));
            Assert.That(receiverItems[0].Item, Is.Not.Null, "A linha precisa referenciar o item entregue.");
        });
    }

    /// <summary>
    /// O presente nao restringe a compra: o buy flow nao consulta GiftFlag, entao o mesmo
    /// pacote continua compravel normalmente mesmo sem ser presenteavel.
    /// </summary>
    [Test]
    public async Task ProcessPurchaseAsync_NonGiftablePackage_IsStillPurchasable()
    {
        var player = await this.CreateSenderAsync(1000).ConfigureAwait(false);
        this.SetupValidCatalog();

        var result = await this._buyService.ProcessPurchaseAsync(
            player,
            PackageProductSeq,
            CategoryProductDisplaySeq,
            PriceSeq,
            ItemCode,
            WCoinPCashType,
            false).ConfigureAwait(false);

        Assert.That(result.Success, Is.True, result.ErrorMessage);
        var senderItems = await this.GetStoredItemsAsync(this._senderAccountId).ConfigureAwait(false);
        Assert.Multiple(() =>
        {
            Assert.That(senderItems, Has.Count.EqualTo(1));
            Assert.That(senderItems[0].IsGift, Is.False, "A purchase is not a gift row.");
        });
    }
}
