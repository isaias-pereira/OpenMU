// <copyright file="CashShopItemExpiryTests.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
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
using MUnique.OpenMU.Interfaces;
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.Persistence.InMemory;
using MUnique.OpenMU.PlugIns;
using NUnit.Framework;

/// <summary>
/// Testes do contrato de expiração real de itens temporários da Cash Shop (Fase 10,
/// <c>docs/cashshop/item-expiry-contract.md</c>).
/// </summary>
/// <remarks>
/// <para>
/// Contrato sob teste:
/// </para>
/// <list type="bullet">
/// <item><description><b>Persistência da duração (GATE D)</b>: a duração contratada
/// (resolvida do catálogo) é gravada em <see cref="CashShopStorageItem.DurationSeconds"/> no
/// momento da compra/gift — nunca vem do cliente e nunca é reconsultada no claim. Permanente
/// novo = 0; NULL = legado.</description></item>
/// <item><description><b>Ativação no claim (GATE E)</b>: o claim grava
/// <see cref="Item.ActivatedAtUtc"/>/<see cref="Item.ExpiresAtUtc"/> no MESMO commit da entrega,
/// exatamente uma vez, a partir do relógio UTC (injetável nos testes); duração ≤ 0 não ativa;
/// duração negativa (metadado corrompido) recusa o claim (fail-safe); falha de save não persiste
/// ativação parcial e o re-claim ativa do zero, sem prorrogação.</description></item>
/// </list>
/// </remarks>
[TestFixture]
public class CashShopItemExpiryTests
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
    private const int GiftableGiftFlag = 184;
    private const string ReceiverName = "Friend";
    private const byte StorageItemTypeProduct = (byte)'P';

    /// <summary>
    /// O "agora" fixo do relógio UTC injetado no claim service: com ele, os timestamps de
    /// ativação são determinísticos nos testes.
    /// </summary>
    private static readonly DateTime FixedUtc = new(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private InMemoryPersistenceContextProvider _contextProvider = null!;
    private GameConfiguration _configuration = null!;
    private Mock<ICashShopCatalogService> _catalog = null!;
    private Mock<ICashShopStorageService> _storageService = null!;
    private CashShopBuyService _buyService = null!;
    private CashShopGiftService _giftService = null!;
    private CashShopStorageClaimService _claimService = null!;
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
        this._storageService.Setup(s => s.GetItemCountAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        this._storageService.Setup(s => s.GetNextStorageIndexAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        this.SetupStorageReservation(0, StorageIndexFull: false);

        var resolver = new CashShopSelectionResolver(this._catalog.Object, NullLogger<CashShopSelectionResolver>.Instance);
        var retirement = new UnsafeContextRetirement(NullLogger<UnsafeContextRetirement>.Instance);
        this._buyService = new CashShopBuyService(resolver, this._storageService.Object, NullLogger<CashShopBuyService>.Instance, retirement);
        this._giftService = new CashShopGiftService(resolver, this._storageService.Object, NullLogger<CashShopGiftService>.Instance, retirement);
        this._claimService = new CashShopStorageClaimService(
            NullLogger<CashShopStorageClaimService>.Instance,
            retirement,
            () => this._contextProvider.CreateNewContext(),
            () => FixedUtc);
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

    /// <summary>
    /// GATE D: a compra de uma oferta de 1 dia (PropertySeq 10, UnitType 386, 86400) grava a
    /// duração contratada na linha do storage. A duração vem do resolver (catálogo), nunca do
    /// cliente — o D2 03 não carrega campo de duração. A duração contratada NÃO ativa o item:
    /// a contagem só começa no claim.
    /// </summary>
    [Test]
    public async Task Buy_OneDayOffer_PersistsContractedDurationSeconds()
    {
        var player = await this.CreateBuyerAsync(1000).ConfigureAwait(false);
        this.SetupCatalog(durationValue: "86400", price: Price);

        var result = await this._buyService.ProcessPurchaseAsync(
            player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false).ConfigureAwait(false);

        Assert.That(result.Success, Is.True, result.ErrorMessage);
        var stored = await this.GetStoredItemsAsync(this._senderAccountId).ConfigureAwait(false);
        Assert.Multiple(() =>
        {
            Assert.That(stored, Has.Count.EqualTo(1));
            Assert.That(stored[0].DurationSeconds, Is.EqualTo(86400), "A duração contratada (1 dia) deve ficar congelada na linha do storage.");
            Assert.That(stored[0].Item, Is.Not.Null);
            Assert.That(stored[0].Item!.ActivatedAtUtc, Is.Null, "A compra não pode ativar a duração.");
            Assert.That(stored[0].Item!.ExpiresAtUtc, Is.Null, "A compra não pode gravar prazo de expiração.");
        });
    }

    /// <summary>
    /// GATE D: oferta sem linha de duração é permanente — a linha NOVA leva 0 explícito
    /// (NULL é reservado para linhas legadas anteriores à Fase 10).
    /// </summary>
    [Test]
    public async Task Buy_PermanentOffer_PersistsZeroDurationSeconds()
    {
        var player = await this.CreateBuyerAsync(1000).ConfigureAwait(false);
        this.SetupCatalog(durationValue: null, price: Price);

        var result = await this._buyService.ProcessPurchaseAsync(
            player, PackageProductSeq, CategoryProductDisplaySeq, PriceSeq, ItemCode, WCoinPCashType, false).ConfigureAwait(false);

        Assert.That(result.Success, Is.True, result.ErrorMessage);
        var stored = await this.GetStoredItemsAsync(this._senderAccountId).ConfigureAwait(false);
        Assert.Multiple(() =>
        {
            Assert.That(stored, Has.Count.EqualTo(1));
            Assert.That(stored[0].DurationSeconds, Is.EqualTo(0), "Permanente novo é 0 explícito; NULL fica para o legado.");
        });
    }

    /// <summary>
    /// GATE D: o presente carrega a mesma duração contratada para a linha do DESTINATÁRIO,
    /// resolvida do catálogo no momento do envio, sem ativação antecipada.
    /// </summary>
    [Test]
    public async Task Gift_OneDayOffer_PersistsDurationOnReceiverRow()
    {
        var player = await this.CreateSenderAsync(1000).ConfigureAwait(false);
        this.SetupCatalog(durationValue: "86400", price: Price);

        var result = await this.SendGiftAsync(player).ConfigureAwait(false);

        Assert.That(result.Success, Is.True, result.ErrorMessage);
        var stored = await this.GetStoredItemsAsync(this._receiverAccountId).ConfigureAwait(false);
        Assert.Multiple(() =>
        {
            Assert.That(stored, Has.Count.EqualTo(1));
            Assert.That(stored[0].IsGift, Is.True);
            Assert.That(stored[0].DurationSeconds, Is.EqualTo(86400), "O presente congela a duração contratada na linha do destinatário.");
            Assert.That(stored[0].Item, Is.Not.Null);
            Assert.That(stored[0].Item!.ActivatedAtUtc, Is.Null, "O presente não ativa a duração.");
            Assert.That(stored[0].Item!.ExpiresAtUtc, Is.Null);
        });
    }

    /// <summary>
    /// GATE E: o claim grava a ativação no MESMO commit da entrega: ActivatedAtUtc = agora do
    /// relógio UTC (injetado), ExpiresAtUtc = ativação + duração contratada. O prazo vem da linha
    /// de storage — o catálogo NÃO é reconsultado no claim.
    /// </summary>
    [Test]
    public async Task Claim_ContractedDuration_ActivatesWithInjectedClock()
    {
        var accountId = Guid.NewGuid();
        var player = await this.CreatePlayerWithAccountAsync(accountId).ConfigureAwait(false);
        await this.CreateRowWithItemAsync(accountId, storageIndex: 0, itemSeq: 0, durability: 1, durationSeconds: 86400).ConfigureAwait(false);

        var result = await this._claimService.ClaimAsync(player, storageIndex: 0, itemSeq: 0, StorageItemTypeProduct).ConfigureAwait(false);
        var stored = await this.GetStoredItemsAsync(accountId).ConfigureAwait(false);
        var inventoryItems = player.Inventory!.Items.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(stored, Is.Empty, "A linha é consumida no commit da entrega.");
            Assert.That(inventoryItems, Has.Count.EqualTo(1));
            Assert.That(inventoryItems[0].ActivatedAtUtc, Is.EqualTo(FixedUtc), "A ativação usa o relógio UTC injetado.");
            Assert.That(inventoryItems[0].ExpiresAtUtc, Is.EqualTo(FixedUtc.AddSeconds(86400)), "ExpiresAtUtc = ativação + duração contratada.");
        });
    }

    /// <summary>
    /// GATE E: linha sem duração contratada (compra legada anterior à Fase 10) entrega item
    /// PERMANENTE — nenhum timestamp é inventado retroativamente.
    /// </summary>
    [Test]
    public async Task Claim_RowWithoutDuration_DeliversPermanentItemWithoutTimestamps()
    {
        var accountId = Guid.NewGuid();
        var player = await this.CreatePlayerWithAccountAsync(accountId).ConfigureAwait(false);
        await this.CreateRowWithItemAsync(accountId, storageIndex: 0, itemSeq: 0, durability: 1, durationSeconds: null).ConfigureAwait(false);

        var result = await this._claimService.ClaimAsync(player, storageIndex: 0, itemSeq: 0, StorageItemTypeProduct).ConfigureAwait(false);
        var inventoryItems = player.Inventory!.Items.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(inventoryItems, Has.Count.EqualTo(1));
            Assert.That(inventoryItems[0].ActivatedAtUtc, Is.Null, "Legado não recebe datas retroativas.");
            Assert.That(inventoryItems[0].ExpiresAtUtc, Is.Null);
        });
    }

    /// <summary>
    /// GATE E: duração contratada 0 (permanente explícito) também não grava timestamps.
    /// </summary>
    [Test]
    public async Task Claim_ZeroDuration_DeliversPermanentItemWithoutTimestamps()
    {
        var accountId = Guid.NewGuid();
        var player = await this.CreatePlayerWithAccountAsync(accountId).ConfigureAwait(false);
        await this.CreateRowWithItemAsync(accountId, storageIndex: 0, itemSeq: 0, durability: 1, durationSeconds: 0).ConfigureAwait(false);

        var result = await this._claimService.ClaimAsync(player, storageIndex: 0, itemSeq: 0, StorageItemTypeProduct).ConfigureAwait(false);
        var inventoryItems = player.Inventory!.Items.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(inventoryItems, Has.Count.EqualTo(1));
            Assert.That(inventoryItems[0].ActivatedAtUtc, Is.Null);
            Assert.That(inventoryItems[0].ExpiresAtUtc, Is.Null);
        });
    }

    /// <summary>
    /// GATE E: duração negativa é metadado corrompido — o claim recusa (fail-safe) e a linha
    /// permanece no storage. Nada é entregue com prazo inválido.
    /// </summary>
    [Test]
    public async Task Claim_NegativeDuration_IsRefusedAndRowStays()
    {
        var accountId = Guid.NewGuid();
        var player = await this.CreatePlayerWithAccountAsync(accountId).ConfigureAwait(false);
        await this.CreateRowWithItemAsync(accountId, storageIndex: 0, itemSeq: 0, durability: 1, durationSeconds: -1).ConfigureAwait(false);

        var result = await this._claimService.ClaimAsync(player, storageIndex: 0, itemSeq: 0, StorageItemTypeProduct).ConfigureAwait(false);
        var stored = await this.GetStoredItemsAsync(accountId).ConfigureAwait(false);
        var inventoryItems = player.Inventory!.Items.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False, "Metadado de duração corrompido deve recusar o claim (fail-safe).");
            Assert.That(stored, Has.Count.EqualTo(1), "A linha permanece no storage.");
            Assert.That(inventoryItems, Is.Empty, "Nada é entregue com prazo inválido.");
        });
    }

    /// <summary>
    /// GATE E: falha de save reporta falha e não entrega nada ao inventário.
    /// </summary>
    /// <remarks>
    /// A atomicidade completa (linha permanece após save falho) é propriedade da transação única
    /// do EF em produção e não é simulável com o provider InMemory, cujo <c>DeleteAsync</c> remove
    /// a linha EAGERLY (fora do save) — o cenário de re-claim após falha exige o provider EF
    /// transacional e é coberto pela estrutura de um único SaveChangesAsync + rollback defensivo.
    /// </remarks>
    [Test]
    public async Task Claim_SaveFailure_ReportsFailureAndDeliversNothing()
    {
        var accountId = Guid.NewGuid();
        var failingProvider = new CashShopBuyServiceTests.FailingContextProvider(this._contextProvider, null);
        this._gameContext = CreateGameContext(failingProvider, this._configuration);
        var player = await this.CreatePlayerWithAccountAsync(accountId).ConfigureAwait(false);
        await this.CreateRowWithItemAsync(accountId, storageIndex: 0, itemSeq: 0, durability: 1, durationSeconds: 86400).ConfigureAwait(false);
        var failingContext = failingProvider.PlayerContext!;
        failingContext.ReturnFalse = true;

        var result = await this._claimService.ClaimAsync(player, storageIndex: 0, itemSeq: 0, StorageItemTypeProduct).ConfigureAwait(false);
        var inventoryItems = player.Inventory!.Items.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False, "Save falho não pode reportar sucesso.");
            Assert.That(inventoryItems, Is.Empty, "Nenhum item é entregue sem commit.");
        });
    }

    private void SetupCatalog(string? durationValue, int price)
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
        this._catalog
            .Setup(c => c.GetPackageBySeqAsync(CategoryProductDisplaySeq, PackageProductSeq, It.IsAny<GameConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(package);

        var products = new List<CashShopProduct>();
        if (durationValue is null)
        {
            // Sem linha 10: oferta permanente (o resolver agrega 0 segundos).
            products.Add(this.CreateCatalogRow(ProductSeq, PriceSeq, 1, "IP Quantity", "1", "EA", 680, price));
        }
        else
        {
            products.Add(this.CreateCatalogRow(ProductSeq, PriceSeq, 10, "Duration", durationValue, "Sec.", 386, price));
        }

        this._catalog
            .Setup(c => c.GetPackageProductsByPriceSeqAsync(It.IsAny<int[]>(), It.IsAny<int>(), It.IsAny<GameConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((
                int[] productSequences,
                int requestedPriceSeq,
                GameConfiguration _,
                CancellationToken _) =>
                products
                    .Where(product => product.PriceSeq == requestedPriceSeq && productSequences.Contains(product.ProductSeq))
                    .ToList());
    }

    private CashShopProduct CreateCatalogRow(int productSeq, int priceSeq, int propertySeq, string propertyName, string value, string unitName, int unitType, int price)
    {
        return new CashShopProduct
        {
            ProductSeq = productSeq,
            PriceSeq = priceSeq,
            InGamePackageID = ItemCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PropertySeq = propertySeq,
            ProductName = "Test Product",
            PropertyName = propertyName,
            Value = value,
            UnitName = unitName,
            UnitType = unitType,
            MustFlag = true,
            ViewOrder = 1,
            ProductType = 140,
            Price = price,
            PropertyType = 142,
            StorageGroup = 673,
            ShareFlag = true,
            DeleteFlag = ActiveProductDeleteFlag,
        };
    }

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

                var storageItem = context.CreateNew<CashShopStorageItem>();
                storageItem.AccountId = accountId;
                storageItem.StorageIndex = storageIndex;
                storageItem.IsGift = forGift;
                return ValueTask.FromResult(new CashShopStorageReservation(storageItem, storageIndex));
            });
    }

    private async ValueTask<Player> CreateBuyerAsync(long balance)
    {
        var player = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);
        this._senderAccountId = Guid.NewGuid();
        player.Account!.Id = this._senderAccountId;

        using (var context = this._contextProvider.CreateNewContext(this._configuration))
        {
            var wallet = context.CreateNew<WCoinWallet>();
            wallet.Id = this._senderAccountId;
            wallet.Balance = balance;
            await context.SaveChangesAsync().ConfigureAwait(false);
        }

        return player;
    }

    /// <summary>
    /// Cria o remetente com carteira e o destinatário em outra conta (personagem
    /// <see cref="ReceiverName"/>), como os testes de gift fazem.
    /// </summary>
    private async ValueTask<Player> CreateSenderAsync(long balance)
    {
        var player = await this.CreateBuyerAsync(balance).ConfigureAwait(false);
        player.SelectedCharacter!.Name = "Sender";

        this._receiverAccountId = Guid.NewGuid();
        using (var context = this._contextProvider.CreateNewContext(this._configuration))
        {
            var receiverAccount = context.CreateNew<Account>();
            receiverAccount.Id = this._receiverAccountId;
            var receiverCharacter = context.CreateNew<Character>();
            receiverCharacter.Id = Guid.NewGuid();
            receiverCharacter.Name = ReceiverName;
            receiverAccount.Characters.Add(receiverCharacter);
            await context.SaveChangesAsync().ConfigureAwait(false);
        }

        return player;
    }

    private async ValueTask<PurchaseResult> SendGiftAsync(Player player)
    {
        return await this._giftService.ProcessGiftAsync(
            player,
            PackageProductSeq,
            CategoryProductDisplaySeq,
            PriceSeq,
            ItemCode,
            WCoinPCashType,
            false,
            ReceiverName,
            "For you",
            CancellationToken.None).ConfigureAwait(false);
    }

    private async ValueTask<Player> CreatePlayerWithAccountAsync(Guid accountId)
    {
        var player = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);
        player.Account!.Id = accountId;
        return player;
    }

    private async ValueTask<CashShopStorageItem> CreateRowWithItemAsync(
        Guid accountId,
        int storageIndex,
        int itemSeq,
        double durability,
        int? durationSeconds)
    {
        using var context = this._contextProvider.CreateNewContext(this._configuration);
        var row = context.CreateNew<CashShopStorageItem>();
        row.AccountId = accountId;
        row.StorageIndex = storageIndex;
        row.ItemSeq = itemSeq;
        row.StorageGroup = 673;
        row.ProductSeq = 58;
        row.PriceSeq = 95;
        row.CashPoint = 100;
        row.ItemType = CashShopStorageItemType.Product;
        row.DurationSeconds = durationSeconds;
        var item = context.CreateNew<Item>();
        item.Definition = this._configuration.Items.First(candidate => candidate.Group == ItemGroup && candidate.Number == ItemNumber);
        item.Durability = durability;
        row.Item = item;
        await context.SaveChangesAsync().ConfigureAwait(false);
        return row;
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
}
