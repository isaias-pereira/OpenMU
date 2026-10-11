// <copyright file="CashShopStorageListActionTests.cs" company="MUnique">
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
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.GameLogic.Views.CashShop;
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.Persistence.InMemory;
using MUnique.OpenMU.PlugIns;
using NUnit.Framework;

/// <summary>
/// Testes comportamentais do fluxo de listagem do storage da Cash Shop
/// (<c>D2 05 → D2 06 → D2 0D</c>), cobrindo o ciclo Storage → cliente:
/// contagens por conta, itens da página com os campos que o cliente resolve
/// no catálogo local (ProductSeq/PriceSeq), abas 'S'/'G' separadas e aba
/// desconhecida recusada com lista vazia.
/// </summary>
[TestFixture]
public class CashShopStorageListActionTests
{
    private InMemoryPersistenceContextProvider _contextProvider = null!;
    private GameConfiguration _configuration = null!;
    private IGameContext _gameContext = null!;
    private Mock<ICashShopStorageService> _storageService = null!;
    private RecordingCashShopViewPlugIn _viewPlugIn = null!;
    private CashShopStorageListAction _action = null!;

    /// <summary>
    /// A view plugin that records every invocation, so the tests can assert the exact
    /// <c>D2 06</c>/<c>D2 0D</c> calls the action makes for the connected client.
    /// </summary>
    private sealed class RecordingCashShopViewPlugIn : ICashShopViewPlugIn
    {
        /// <summary>Gets the recorded <c>ShowStorageCountAsync</c> calls.</summary>
        public List<(ushort TotalItemCount, ushort CurrentItemCount, ushort PageIndex, ushort TotalPage)> StorageCounts { get; } = new();

        /// <summary>Gets the recorded <c>ShowStorageItemAsync</c> calls, in order.</summary>
        public List<(int StorageIndex, int ItemSeq, int StorageGroupCode, int ProductSeq, int PriceSeq, double CashPoint, char ItemType)> StorageItems { get; } = new();

        /// <summary>Gets the recorded <c>ShowGiftStorageItemAsync</c> calls (D2 0E), in order.</summary>
        public List<(int StorageIndex, int ItemSeq, int StorageGroupCode, int ProductSeq, int PriceSeq, double CashPoint, char ItemType, string? GiftSender, string? GiftMessage)> GiftStorageItems { get; } = new();

        /// <inheritdoc />
        public ValueTask ShowPointInfoAsync(double totalCash, double cashCredit, double cashPrepaid, double totalPoint, double totalMileage) => ValueTask.CompletedTask;

        /// <inheritdoc />
        public ValueTask ShowOpenResultAsync(bool success) => ValueTask.CompletedTask;

        /// <inheritdoc />
        public ValueTask ShowStorageCountAsync(ushort totalItemCount, ushort currentPageItemCount, ushort pageIndex, ushort totalPage)
        {
            this.StorageCounts.Add((totalItemCount, currentPageItemCount, pageIndex, totalPage));
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask ShowStorageItemAsync(int storageIndex, int itemSeq, int storageGroupCode, int productSeq, int priceSeq, double cashPoint, char itemType)
        {
            this.StorageItems.Add((storageIndex, itemSeq, storageGroupCode, productSeq, priceSeq, cashPoint, itemType));
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask ShowGiftStorageItemAsync(int storageIndex, int itemSeq, int storageGroupCode, int productSeq, int priceSeq, double cashPoint, char itemType, string? giftSender, string? giftMessage)
        {
            this.GiftStorageItems.Add((storageIndex, itemSeq, storageGroupCode, productSeq, priceSeq, cashPoint, itemType, giftSender, giftMessage));
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask ShowScriptVersionAsync(ushort saleZone, ushort year, ushort yearIdentify) => ValueTask.CompletedTask;

        /// <inheritdoc />
        public ValueTask ShowBannerVersionAsync(ushort bannerZone, ushort year, ushort yearIdentify) => ValueTask.CompletedTask;

        /// <inheritdoc />
        public ValueTask ShowEventItemCountAsync(ushort itemCount) => ValueTask.CompletedTask;

        /// <inheritdoc />
        public ValueTask ShowBuyResultAsync(bool success, int errorCode, string? deliveredItem) => ValueTask.CompletedTask;

        /// <inheritdoc />
        public ValueTask ShowStorageItemUseResultAsync(StorageClaimErrorCode errorCode) => ValueTask.CompletedTask;
    }

    /// <summary>
    /// A view plugin container that only serves the recording cash shop view.
    /// </summary>
    private sealed class SingleViewPlugInContainer : ICustomPlugInContainer<IViewPlugIn>
    {
        private readonly ICashShopViewPlugIn _viewPlugIn;

        /// <summary>
        /// Initializes a new instance of the <see cref="SingleViewPlugInContainer"/> class.
        /// </summary>
        /// <param name="viewPlugIn">The only plug in this container serves.</param>
        public SingleViewPlugInContainer(ICashShopViewPlugIn viewPlugIn) => this._viewPlugIn = viewPlugIn;

        /// <inheritdoc />
        public T? GetPlugIn<T>()
            where T : class, IViewPlugIn => this._viewPlugIn as T;
    }

    /// <summary>
    /// Sets up the player, the storage service and the action. The player shares the context
    /// provider, so the storage rows created here are visible to the listing.
    /// </summary>
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

        var map = new Mock<GameMapDefinition>();
        map.SetupAllProperties();
        map.Setup(m => m.DropItemGroups).Returns(new List<DropItemGroup>());
        map.Setup(m => m.MonsterSpawns).Returns(new List<MonsterSpawnArea>());
        map.Object.TerrainData = new byte[ushort.MaxValue + 3];
        this._configuration.Maps.Add(map.Object);
        this._configuration.RecoveryInterval = int.MaxValue;

        var mapInitializer = new MapInitializer(this._configuration, new NullLogger<MapInitializer>(), NullDropGenerator.Instance, null);
        this._gameContext = new GameContext(
            this._configuration,
            this._contextProvider,
            mapInitializer,
            new NullLoggerFactory(),
            new PlugInManager(null, new NullLoggerFactory(), null, null),
            NullDropGenerator.Instance,
            new ConfigurationChangeMediator());
        mapInitializer.PlugInManager = this._gameContext.PlugInManager;
        mapInitializer.PathFinderPool = this._gameContext.PathFinderPool;

        this._storageService = new Mock<ICashShopStorageService>();
        this._storageService.Setup(s => s.Capacity).Returns(30);
        this._storageService.Setup(s => s.GetItemCountAsync(It.IsAny<Guid>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        this._storageService.Setup(s => s.GetGiftCountAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
        this._storageService.Setup(s => s.GetPageAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), false, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<CashShopStorageItem>());
        this._storageService.Setup(s => s.GetGiftPageAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<CashShopStorageItem>());

        this._viewPlugIn = new RecordingCashShopViewPlugIn();
        this._action = new CashShopStorageListAction(this._storageService.Object);
    }

    /// <summary>
    /// Creates a player whose account owns the given id, so the storage rows of the test
    /// resolve as the account's own rows.
    /// </summary>
    private async ValueTask<Player> CreatePlayerWithAccountAsync(Guid accountId)
    {
        var player = await PlayerTestHelper.CreatePlayerAsync(
            this._gameContext,
            () => new SingleViewPlugInContainer(this._viewPlugIn)).ConfigureAwait(false);
        player.Account!.Id = accountId;
        return player;
    }

    /// <summary>
    /// A listagem da aba 'S' envia D2 06 com a contagem da conta e uma linha D2 0D por item,
    /// com os campos que o cliente resolve no catálogo local (ProductSeq/PriceSeq) — e nunca
    /// linhas de outra conta.
    /// </summary>
    [Test]
    public async Task RequestListAsync_PurchaseTab_SendsCountAndAccountScopedItems()
    {
        var accountId = Guid.NewGuid();
        var otherAccountId = Guid.NewGuid();
        var own = await this.CreateStorageRowAsync(accountId, storageIndex: 0, isGift: false).ConfigureAwait(false);
        await this.CreateStorageRowAsync(otherAccountId, storageIndex: 0, isGift: false).ConfigureAwait(false);
        this._storageService.Setup(s => s.GetItemCountAsync(accountId, false, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        this._storageService.Setup(s => s.GetPageAsync(accountId, 0, 30, false, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { own });
        var player = await this.CreatePlayerWithAccountAsync(accountId).ConfigureAwait(false);

        await this._action.RequestListAsync(player, pageIndex: 1, CashShopStorageListAction.DefaultStorageType).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(this._viewPlugIn.StorageCounts, Has.Count.EqualTo(1), "The client receives exactly one D2 06 for the request.");
            Assert.That(this._viewPlugIn.StorageCounts[0].TotalItemCount, Is.EqualTo(1), "The count only covers the requesting account's rows.");
            Assert.That(this._viewPlugIn.StorageCounts[0].CurrentItemCount, Is.EqualTo(1));
            Assert.That(this._viewPlugIn.StorageCounts[0].PageIndex, Is.EqualTo(1));
            Assert.That(this._viewPlugIn.StorageCounts[0].TotalPage, Is.EqualTo(1));
            Assert.That(this._viewPlugIn.StorageItems, Has.Count.EqualTo(1), "One D2 0D per item of the page; other accounts' rows are not sent.");
            Assert.That(this._viewPlugIn.StorageItems[0].StorageIndex, Is.EqualTo(own.StorageIndex));
            Assert.That(this._viewPlugIn.StorageItems[0].ItemSeq, Is.EqualTo(own.ItemSeq));
            Assert.That(this._viewPlugIn.StorageItems[0].StorageGroupCode, Is.EqualTo(own.StorageGroup));
            Assert.That(this._viewPlugIn.StorageItems[0].ProductSeq, Is.EqualTo(own.ProductSeq), "The client resolves the item, quantity and period from ProductSeq/PriceSeq in its local catalog.");
            Assert.That(this._viewPlugIn.StorageItems[0].PriceSeq, Is.EqualTo(own.PriceSeq));
            Assert.That(this._viewPlugIn.StorageItems[0].CashPoint, Is.EqualTo(own.CashPoint));
            Assert.That(this._viewPlugIn.StorageItems[0].ItemType, Is.EqualTo('P'));
        });
    }

    /// <summary>
    /// A aba 'G' (caixa de presentes) lista somente as linhas marcadas como presente,
    /// com numeração independente da aba de compras. A partir da Fase 10 as linhas saem
    /// pelo D2 0E (com remetente/mensagem), não mais pelo D2 0D.
    /// </summary>
    [Test]
    public async Task RequestListAsync_GiftTab_ListsOnlyGiftRows()
    {
        var accountId = Guid.NewGuid();
        var gift = await this.CreateStorageRowAsync(accountId, storageIndex: 0, isGift: true).ConfigureAwait(false);
        await this.CreateStorageRowAsync(accountId, storageIndex: 0, isGift: false).ConfigureAwait(false);
        this._storageService.Setup(s => s.GetGiftCountAsync(accountId, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        this._storageService.Setup(s => s.GetGiftPageAsync(accountId, 0, 30, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { gift });
        var player = await this.CreatePlayerWithAccountAsync(accountId).ConfigureAwait(false);

        await this._action.RequestListAsync(player, pageIndex: 1, CashShopStorageListAction.GiftStorageType).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(this._viewPlugIn.StorageCounts, Has.Count.EqualTo(1));
            Assert.That(this._viewPlugIn.StorageCounts[0].TotalItemCount, Is.EqualTo(1), "The gift tab counts only gift rows.");
            Assert.That(this._viewPlugIn.GiftStorageItems, Has.Count.EqualTo(1), "A partir da Fase 10 a aba de presentes entrega as linhas pelo D2 0E.");
            Assert.That(this._viewPlugIn.GiftStorageItems[0].StorageIndex, Is.EqualTo(gift.StorageIndex));
            Assert.That(this._viewPlugIn.GiftStorageItems[0].ItemType, Is.EqualTo('P'), "A gift is rendered as a product ('P'); chItemType only accepts 'P'/'C'.");
            Assert.That(this._viewPlugIn.StorageItems, Is.Empty, "A aba de presentes não envia linhas D2 0D.");
        });
    }

    /// <summary>
    /// FASE 10 (RED): a aba 'G' entrega as linhas pelo D2 0E — com o remetente e a mensagem da
    /// linha — e nunca mistura D2 0D na mesma resposta; os demais campos replicam os do D2 0D.
    /// </summary>
    [Test]
    public async Task RequestListAsync_GiftTab_SendsGiftStorageItemsWithMetadataViaD2_0E()
    {
        var accountId = Guid.NewGuid();
        var gift = await this.CreateStorageRowAsync(accountId, storageIndex: 0, isGift: true).ConfigureAwait(false);
        gift.GiftSender = "Sender";
        gift.GiftMessage = "Feliz Natal!";
        this._storageService.Setup(s => s.GetGiftCountAsync(accountId, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        this._storageService.Setup(s => s.GetGiftPageAsync(accountId, 0, 30, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { gift });
        var player = await this.CreatePlayerWithAccountAsync(accountId).ConfigureAwait(false);

        await this._action.RequestListAsync(player, pageIndex: 1, CashShopStorageListAction.GiftStorageType).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(this._viewPlugIn.GiftStorageItems, Has.Count.EqualTo(1), "A aba de presentes entrega cada linha pelo D2 0E.");
            Assert.That(this._viewPlugIn.StorageItems, Is.Empty, "A aba de presentes não pode misturar linhas D2 0D.");
            Assert.That(this._viewPlugIn.GiftStorageItems[0].StorageIndex, Is.EqualTo(gift.StorageIndex));
            Assert.That(this._viewPlugIn.GiftStorageItems[0].ItemSeq, Is.EqualTo(gift.ItemSeq));
            Assert.That(this._viewPlugIn.GiftStorageItems[0].StorageGroupCode, Is.EqualTo(gift.StorageGroup));
            Assert.That(this._viewPlugIn.GiftStorageItems[0].ProductSeq, Is.EqualTo(gift.ProductSeq), "O cliente resolve o item no catálogo local como no D2 0D.");
            Assert.That(this._viewPlugIn.GiftStorageItems[0].PriceSeq, Is.EqualTo(gift.PriceSeq));
            Assert.That(this._viewPlugIn.GiftStorageItems[0].CashPoint, Is.EqualTo(gift.CashPoint));
            Assert.That(this._viewPlugIn.GiftStorageItems[0].ItemType, Is.EqualTo('P'));
            Assert.That(this._viewPlugIn.GiftStorageItems[0].GiftSender, Is.EqualTo("Sender"), "O D2 0E carrega o remetente persistido.");
            Assert.That(this._viewPlugIn.GiftStorageItems[0].GiftMessage, Is.EqualTo("Feliz Natal!"), "O D2 0E carrega a mensagem persistida.");
        });
    }

    /// <summary>
    /// FASE 10: a aba de compras ('S') continua entregando apenas D2 0D — nenhuma linha D2 0E,
    /// mesmo que uma linha de compra carregue metadados de presente (não podem existir, mas o
    /// contrato é da aba, não da linha).
    /// </summary>
    [Test]
    public async Task RequestListAsync_PurchaseTab_NeverSendsGiftStorageItems()
    {
        var accountId = Guid.NewGuid();
        var own = await this.CreateStorageRowAsync(accountId, storageIndex: 0, isGift: false).ConfigureAwait(false);
        this._storageService.Setup(s => s.GetItemCountAsync(accountId, false, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        this._storageService.Setup(s => s.GetPageAsync(accountId, 0, 30, false, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { own });
        var player = await this.CreatePlayerWithAccountAsync(accountId).ConfigureAwait(false);

        await this._action.RequestListAsync(player, pageIndex: 1, CashShopStorageListAction.DefaultStorageType).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(this._viewPlugIn.StorageItems, Has.Count.EqualTo(1), "A aba de compras continua entregando D2 0D.");
            Assert.That(this._viewPlugIn.GiftStorageItems, Is.Empty, "A aba de compras nunca envia D2 0E.");
        });
    }

    /// <summary>
    /// FASE 10: a paginação da aba de presentes é preservada — D2 06 com as contagens e
    /// D2 0E apenas para a página pedida; página fora do range responde só com as contagens.
    /// </summary>
    [Test]
    public async Task RequestListAsync_GiftTab_PaginationAndCountPreserved()
    {
        var accountId = Guid.NewGuid();
        var first = await this.CreateStorageRowAsync(accountId, storageIndex: 0, isGift: true).ConfigureAwait(false);
        this._storageService.Setup(s => s.GetGiftCountAsync(accountId, It.IsAny<CancellationToken>())).ReturnsAsync(31);
        this._storageService.Setup(s => s.GetGiftPageAsync(accountId, 0, 30, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { first });
        this._storageService.Setup(s => s.GetGiftPageAsync(accountId, 30, 30, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<CashShopStorageItem>());
        var player = await this.CreatePlayerWithAccountAsync(accountId).ConfigureAwait(false);

        await this._action.RequestListAsync(player, pageIndex: 1, CashShopStorageListAction.GiftStorageType).ConfigureAwait(false);
        await this._action.RequestListAsync(player, pageIndex: 2, CashShopStorageListAction.GiftStorageType).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(this._viewPlugIn.StorageCounts, Has.Count.EqualTo(2), "Cada página pedida recebe um D2 06.");
            Assert.That(this._viewPlugIn.StorageCounts[0].TotalItemCount, Is.EqualTo(31), "A contagem total cobre as 31 linhas de presente.");
            Assert.That(this._viewPlugIn.StorageCounts[0].CurrentItemCount, Is.EqualTo(1));
            Assert.That(this._viewPlugIn.StorageCounts[0].TotalPage, Is.EqualTo(2), "31 linhas em páginas de 30 = 2 páginas.");
            Assert.That(this._viewPlugIn.StorageCounts[1].CurrentItemCount, Is.EqualTo(0), "A segunda página está vazia neste cenário.");
            Assert.That(this._viewPlugIn.GiftStorageItems, Has.Count.EqualTo(1), "Somente a primeira página entrega linhas D2 0E.");
        });
    }

    /// <summary>
    /// FASE 10: a listagem de presentes é account-scoped — a ação consulta o serviço com a
    /// conta autenticada, e linhas de outras contas nunca chegam ao cliente.
    /// </summary>
    [Test]
    public async Task RequestListAsync_GiftTab_QueriesTheAuthenticatedAccountOnly()
    {
        var accountId = Guid.NewGuid();
        var otherAccountId = Guid.NewGuid();
        var own = await this.CreateStorageRowAsync(accountId, storageIndex: 0, isGift: true).ConfigureAwait(false);
        await this.CreateStorageRowAsync(otherAccountId, storageIndex: 0, isGift: true).ConfigureAwait(false);
        this._storageService.Setup(s => s.GetGiftCountAsync(accountId, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        this._storageService.Setup(s => s.GetGiftPageAsync(accountId, 0, 30, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { own });
        var player = await this.CreatePlayerWithAccountAsync(accountId).ConfigureAwait(false);

        await this._action.RequestListAsync(player, pageIndex: 1, CashShopStorageListAction.GiftStorageType).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            this._storageService.Verify(s => s.GetGiftCountAsync(accountId, It.IsAny<CancellationToken>()), Times.Once, "A contagem consulta a conta autenticada.");
            this._storageService.Verify(s => s.GetGiftPageAsync(accountId, 0, 30, It.IsAny<CancellationToken>()), Times.Once, "A página consulta a conta autenticada.");
            this._storageService.Verify(s => s.GetGiftPageAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
            Assert.That(this._viewPlugIn.GiftStorageItems, Has.Count.EqualTo(1), "Apenas as linhas da própria conta são entregues.");
            Assert.That(this._viewPlugIn.GiftStorageItems[0].StorageIndex, Is.EqualTo(own.StorageIndex));
        });
    }

    /// <summary>
    /// FASE 10: um presente antigo sem metadados persistidos (colunas NULL) é listado pelo
    /// D2 0E com remetente/mensagem nulos — nenhum remetente é inventado.
    /// </summary>
    [Test]
    public async Task RequestListAsync_GiftTab_LegacyRowWithoutMetadata_ListsWithNullMetadata()
    {
        var accountId = Guid.NewGuid();
        var legacy = await this.CreateStorageRowAsync(accountId, storageIndex: 0, isGift: true).ConfigureAwait(false);
        Assert.That(legacy.GiftSender, Is.Null, "Linhas criadas antes da Fase 10 não têm remetente.");
        Assert.That(legacy.GiftMessage, Is.Null);
        this._storageService.Setup(s => s.GetGiftCountAsync(accountId, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        this._storageService.Setup(s => s.GetGiftPageAsync(accountId, 0, 30, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { legacy });
        var player = await this.CreatePlayerWithAccountAsync(accountId).ConfigureAwait(false);

        await this._action.RequestListAsync(player, pageIndex: 1, CashShopStorageListAction.GiftStorageType).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(this._viewPlugIn.GiftStorageItems, Has.Count.EqualTo(1), "O presente legado continua listado.");
            Assert.That(this._viewPlugIn.GiftStorageItems[0].GiftSender, Is.Null, "Nenhum remetente é inventado para o presente legado.");
            Assert.That(this._viewPlugIn.GiftStorageItems[0].GiftMessage, Is.Null);
        });
    }

    /// <summary>
    /// Uma aba desconhecida é recusada com a lista vazia, para que uma requisição forjada
    /// não reciba linhas de outra aba por engano.
    /// </summary>
    [Test]
    public async Task RequestListAsync_UnknownStorageType_AnswersWithEmptyList()
    {
        var accountId = Guid.NewGuid();
        await this.CreateStorageRowAsync(accountId, storageIndex: 0, isGift: false).ConfigureAwait(false);
        this._storageService.Setup(s => s.GetItemCountAsync(accountId, false, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var player = await this.CreatePlayerWithAccountAsync(accountId).ConfigureAwait(false);

        await this._action.RequestListAsync(player, pageIndex: 1, (byte)'X').ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(this._viewPlugIn.StorageCounts, Has.Count.EqualTo(1));
            Assert.That(this._viewPlugIn.StorageCounts[0].TotalItemCount, Is.EqualTo(0), "An unknown storage code answers with an empty list.");
            Assert.That(this._viewPlugIn.StorageCounts[0].TotalPage, Is.EqualTo(0));
            Assert.That(this._viewPlugIn.StorageItems, Is.Empty, "No storage rows are sent for an unknown storage code.");
        });
    }

    /// <summary>
    /// Páginas fora do range respondem apenas com as contagens, deixando a lista vazia —
    /// o cliente pede a última página mesmo com o storage encolhido.
    /// </summary>
    [Test]
    public async Task RequestListAsync_PageBeyondRange_AnswersCountsOnly()
    {
        var accountId = Guid.NewGuid();
        await this.CreateStorageRowAsync(accountId, storageIndex: 0, isGift: false).ConfigureAwait(false);
        this._storageService.Setup(s => s.GetItemCountAsync(accountId, false, It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var player = await this.CreatePlayerWithAccountAsync(accountId).ConfigureAwait(false);

        await this._action.RequestListAsync(player, pageIndex: 5, CashShopStorageListAction.DefaultStorageType).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(this._viewPlugIn.StorageCounts, Has.Count.EqualTo(1));
            Assert.That(this._viewPlugIn.StorageCounts[0].TotalItemCount, Is.EqualTo(1));
            Assert.That(this._viewPlugIn.StorageCounts[0].CurrentItemCount, Is.EqualTo(0), "A page beyond the range carries no items.");
            Assert.That(this._viewPlugIn.StorageCounts[0].PageIndex, Is.EqualTo(5));
            Assert.That(this._viewPlugIn.StorageCounts[0].TotalPage, Is.EqualTo(1));
            Assert.That(this._viewPlugIn.StorageItems, Is.Empty);
        });
    }

    /// <summary>
    /// Cria uma linha de storage persistida no contexto compartilhado, como as faz a reserva
    /// da compra/gift.
    /// </summary>
    private async ValueTask<CashShopStorageItem> CreateStorageRowAsync(Guid accountId, int storageIndex, bool isGift)
    {
        using var context = this._contextProvider.CreateNewContext(this._configuration);
        var row = context.CreateNew<CashShopStorageItem>();
        row.AccountId = accountId;
        row.StorageIndex = storageIndex;
        row.IsGift = isGift;
        row.ItemSeq = storageIndex;
        row.StorageGroup = 673;
        row.ProductSeq = 58;
        row.PriceSeq = 95;
        row.CashPoint = 100;
        row.ItemType = CashShopStorageItemType.Product;
        await context.SaveChangesAsync().ConfigureAwait(false);
        return row;
    }
}
