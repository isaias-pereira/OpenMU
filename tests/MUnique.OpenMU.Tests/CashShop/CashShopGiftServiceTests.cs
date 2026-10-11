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
    /// FASE 10 Rodada 2C: mensagem acima do limite de BYTES do fio (199) é persistida já
    /// truncada na fronteira UTF-8 — o texto no banco é exatamente o que o cliente exibirá,
    /// sem divergência silenciosa entre persistência e tela.
    /// </summary>
    [Test]
    public async Task ProcessGiftAsync_MessageAboveWireByteLimit_PersistsExactlyWhatTheClientWillShow()
    {
        var player = await this.CreateSenderAsync(1000).ConfigureAwait(false);
        this.SetupValidCatalog();
        var message = new string('á', 150); // 300 bytes UTF-8 — acima dos 199 úteis do D2 0E.

        var result = await this.SendGiftAsync(player, ReceiverName, giftText: message).ConfigureAwait(false);

        Assert.That(result.Success, Is.True, result.ErrorMessage);
        var stored = await this.GetStoredItemsAsync(this._receiverAccountId).ConfigureAwait(false);
        var expected = new string('á', 99); // 99 × 2 bytes = 198 ≤ 199; o 100º 'á' não cabe.
        Assert.Multiple(() =>
        {
            Assert.That(stored, Has.Count.EqualTo(1));
            Assert.That(stored[0].GiftMessage, Is.EqualTo(expected), "O texto persistido é o truncado em fronteira UTF-8, igual ao que o fio carrega.");
            Assert.That(System.Text.Encoding.UTF8.GetByteCount(stored[0].GiftMessage!), Is.EqualTo(198), "O texto persistido cabe no campo de 199 bytes úteis do D2 0E.");
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

    /// <summary>
    /// LACUNA FASE 8/9 (teste RED): o gift deve preservar a Quantity do catálogo no item entregue,
    /// como a compra faz (Item.Durability = stack count; <c>CashShopBuyService.ResolveDeliveredDurabilities</c>).
    /// Hoje o gift grava <c>Durability = 1</c> em stackables (<c>CashShopGiftService</c>, linha ~187:
    /// <c>item.Durability = item.IsStackable() ? 1 : itemDefinition.Durability</c>), entregando 1 unidade
    /// de um pacote que promete 20 — e o cliente exibe "20" porque resolve a quantidade no catálogo local.
    /// </summary>
    [Test]
    public async Task ProcessGiftAsync_Quantity20_PreservesQuantityInStorageItem()
    {
        // Item stackable com capacidade 20 (IsStackable = !wearable && Durability > 1) e oferta
        // com Quantity 20 (padrão 7/35/51 do catálogo real).
        this._configuration.Items.First(item => item.Group == ItemGroup && item.Number == ItemNumber).Durability = 20;
        var player = await this.CreateSenderAsync(1000).ConfigureAwait(false);
        this.SetupValidCatalog(products: new[]
        {
            new CashShopProduct
            {
                ProductSeq = ProductSeq,
                PriceSeq = PriceSeq,
                PropertySeq = 7,
                PropertyName = "Quantity",
                Value = "20",
                UnitName = "EA",
                UnitType = 680,
                MustFlag = true,
                Price = Price,
                DeleteFlag = ActiveProductDeleteFlag,
                StorageGroup = 3,
                InGamePackageID = ItemCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
        });

        var result = await this.SendGiftAsync(player, ReceiverName).ConfigureAwait(false);

        Assert.That(result.Success, Is.True, $"O gift de um pacote com Quantity 20 e capacidade 20 deve ser aceito: {result.ErrorMessage}");
        var stored = await this.GetStoredItemsAsync(this._receiverAccountId).ConfigureAwait(false);
        Assert.Multiple(() =>
        {
            Assert.That(stored, Has.Count.EqualTo(1));
            Assert.That(stored[0].Item, Is.Not.Null);
            Assert.That(stored[0].Item!.Durability, Is.EqualTo(20), "O gift deve preservar a Quantity do catálogo (Item.Durability = 20), não entregar 1 unidade.");
        });
        Assert.That(await this.GetWalletBalanceAsync().ConfigureAwait(false), Is.EqualTo(1000 - Price), "O débito do gift é único, pelo preço da oferta.");
    }

    /// <summary>
    /// FASE 10 (RED): o gift deve persistir o remetente — o nome do <b>personagem</b> da sessão
    /// autenticada — na linha de storage do destinatário (campo <c>chSendUserName</c> do D2 0E).
    /// </summary>
    [Test]
    public async Task ProcessGiftAsync_PersistsSenderCharacterNameFromAuthenticatedSession()
    {
        var player = await this.CreateSenderAsync(1000).ConfigureAwait(false);
        this.SetupValidCatalog();

        var result = await this.SendGiftAsync(player, ReceiverName).ConfigureAwait(false);

        Assert.That(result.Success, Is.True, result.ErrorMessage);
        var stored = await this.GetStoredItemsAsync(this._receiverAccountId).ConfigureAwait(false);
        Assert.Multiple(() =>
        {
            Assert.That(stored, Has.Count.EqualTo(1));
            Assert.That(stored[0].GiftSender, Is.EqualTo(player.SelectedCharacter!.Name), "O remetente persistido é o personagem da sessão autenticada.");
        });
    }

    /// <summary>
    /// FASE 10 (RED): o gift deve persistir a mensagem enviada pelo cliente na linha de storage
    /// do destinatário (campo <c>chMessage</c> do D2 0E), no mesmo commit da entrega.
    /// </summary>
    [Test]
    public async Task ProcessGiftAsync_PersistsGiftMessage()
    {
        var player = await this.CreateSenderAsync(1000).ConfigureAwait(false);
        this.SetupValidCatalog();

        var result = await this.SendGiftAsync(player, ReceiverName, giftText: "Feliz Natal, amigo!").ConfigureAwait(false);

        Assert.That(result.Success, Is.True, result.ErrorMessage);
        var stored = await this.GetStoredItemsAsync(this._receiverAccountId).ConfigureAwait(false);
        Assert.Multiple(() =>
        {
            Assert.That(stored, Has.Count.EqualTo(1));
            Assert.That(stored[0].GiftMessage, Is.EqualTo("Feliz Natal, amigo!"), "A mensagem do presente é persistida integralmente (dentro do limite).");
        });
    }

    /// <summary>
    /// FASE 10: nenhum dado do pacote pode falsificar o remetente — o D2 04 não carrega campo de
    /// remetente, e um texto forjado na mensagem não altera o <c>GiftSender</c> persistido, que
    /// é sempre o personagem da sessão autenticada.
    /// </summary>
    [Test]
    public async Task ProcessGiftAsync_ArbitrarySenderCannotBeSpoofed()
    {
        var player = await this.CreateSenderAsync(1000).ConfigureAwait(false);
        this.SetupValidCatalog();

        // A mensagem tenta se passar pelo remetente; o receptor também não interfere.
        var result = await this.SendGiftAsync(player, ReceiverName, giftText: "From: Impostor").ConfigureAwait(false);

        Assert.That(result.Success, Is.True, result.ErrorMessage);
        var stored = await this.GetStoredItemsAsync(this._receiverAccountId).ConfigureAwait(false);
        Assert.Multiple(() =>
        {
            Assert.That(stored, Has.Count.EqualTo(1));
            Assert.That(stored[0].GiftSender, Is.EqualTo(player.SelectedCharacter!.Name), "O remetente vem da sessão autenticada, não do conteúdo do pacote.");
            Assert.That(stored[0].GiftSender, Is.Not.EqualTo("Impostor"));
            Assert.That(stored[0].GiftMessage, Is.EqualTo("From: Impostor"), "O texto forjado é tratado como mensagem, nunca como remetente.");
        });
    }

    /// <summary>
    /// FASE 10: mensagem vazia é aceita — o presente é entregue e a mensagem é persistida como
    /// nula (campo vazio no D2 0E), sem falhar o fluxo nem inventar texto.
    /// </summary>
    [TestCase("")]
    [TestCase("   ")]
    public async Task ProcessGiftAsync_EmptyGiftMessage_IsAcceptedAndStoredAsNull(string giftText)
    {
        var player = await this.CreateSenderAsync(1000).ConfigureAwait(false);
        this.SetupValidCatalog();

        var result = await this.SendGiftAsync(player, ReceiverName, giftText: giftText).ConfigureAwait(false);

        Assert.That(result.Success, Is.True, result.ErrorMessage);
        var stored = await this.GetStoredItemsAsync(this._receiverAccountId).ConfigureAwait(false);
        var balance = await this.GetWalletBalanceAsync().ConfigureAwait(false);
        Assert.Multiple(() =>
        {
            Assert.That(stored, Has.Count.EqualTo(1), "A mensagem vazia não impede a entrega.");
            Assert.That(stored[0].GiftMessage, Is.Null.Or.Empty, "A mensagem vazia é persistida como nula/vazia, sem texto inventado.");
            Assert.That(balance, Is.EqualTo(1000 - Price), "O débito permanece único.");
        });
    }

    /// <summary>
    /// FASE 10: presente antigo sem metadados (colunas NULL, antes da Fase 10) continua válido
    /// e listável — nenhum campo dos metadados é exigido para ler ou listar a linha.
    /// </summary>
    [Test]
    public async Task LegacyGiftRow_WithNullMetadata_RemainsValidAndListable()
    {
        var player = await this.CreateSenderAsync(1000).ConfigureAwait(false);
        this.SetupValidCatalog();

        // Linha legada criada diretamente no contexto, como as entregues antes da Fase 10:
        // sem GiftSender/GiftMessage.
        using (var context = this._contextProvider.CreateNewContext(this._configuration))
        {
            var legacy = context.CreateNew<CashShopStorageItem>();
            legacy.AccountId = this._receiverAccountId;
            legacy.StorageIndex = 5;
            legacy.ItemSeq = 9;
            legacy.IsGift = true;
            legacy.ItemType = CashShopStorageItemType.Product;
            await context.SaveChangesAsync().ConfigureAwait(false);
        }

        var stored = await this.GetStoredItemsAsync(this._receiverAccountId).ConfigureAwait(false);
        Assert.Multiple(() =>
        {
            Assert.That(stored, Has.Count.EqualTo(1));
            Assert.That(stored[0].GiftSender, Is.Null, "O presente legado não ganha remetente inventado.");
            Assert.That(stored[0].GiftMessage, Is.Null);
        });
    }

    /// <summary>
    /// FASE 10: falha de persistência não deixa nada parcial — sem gift, sem metadados, sem
    /// débito: o rollback do contexto descarta a linha com os metadados junto.
    /// </summary>
    [Test]
    public async Task ProcessGiftAsync_SaveFailure_LeavesNoPartialGiftMetadataOrDebit()
    {
        var (player, context) = await this.CreateSenderWithFailingSaveAsync(1000).ConfigureAwait(false);
        context.FailNextSave = true;

        var result = await this.SendGiftAsync(player, ReceiverName).ConfigureAwait(false);

        var balance = await this.GetWalletBalanceAsync().ConfigureAwait(false);
        var receiverItems = await this.GetStoredItemsAsync(this._receiverAccountId).ConfigureAwait(false);
        var transactions = await this.GetTransactionsAsync().ConfigureAwait(false);
        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False, "A falha de gravação é reportada como falha.");
            Assert.That(balance, Is.EqualTo(1000), "A falha de gravação não cobra o remetente.");
            Assert.That(receiverItems, Is.Empty, "A falha de gravação não entrega a linha com metadados.");
            Assert.That(transactions, Is.Empty, "A falha de gravação não deixa débito órfão.");
        });

        // Uma gravação posterior do contexto não pode ressuscitar o presente descartado.
        context.FailNextSave = false;
        await player.PersistenceContext.SaveChangesAsync().ConfigureAwait(false);
        var afterSave = await this.GetStoredItemsAsync(this._receiverAccountId).ConfigureAwait(false);
        Assert.That(afterSave, Is.Empty, "O rollback não pode deixar a linha com metadados para um save futuro persistir.");
    }
}
