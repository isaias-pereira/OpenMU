// <copyright file="CashShopStorageClaimTests.cs" company="MUnique">
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
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.Persistence.InMemory;
using MUnique.OpenMU.PlugIns;
using NUnit.Framework;

/// <summary>
/// Testes do fluxo de retirada do storage da Cash Shop (<c>C1 D2 0B</c> —
/// <see cref="CashShopStorageClaimService"/>): move o item da linha para o inventário do
/// personagem e consome a linha, atomicamente.
/// </summary>
/// <remarks>
/// A propriedade obrigatória: OU o item permanece no storage, OU é entregue ao inventário —
/// nunca ambos, nunca nenhum. A autoridade é a linha persistida associada à conta autenticada;
/// o item code enviado pelo cliente nunca é autoridade. A linha é removida somente depois de
/// todas as validações.
/// </remarks>
[TestFixture]
public class CashShopStorageClaimTests
{
    private const int ItemGroup = 31;
    private const int ItemNumber = 7;
    private const int ItemCode = (ItemGroup * 512) + ItemNumber;
    private const byte StorageItemTypeProduct = (byte)'P';

    private InMemoryPersistenceContextProvider _contextProvider = null!;
    private GameConfiguration _configuration = null!;
    private IGameContext _gameContext = null!;
    private CashShopStorageClaimService _service = null!;

    /// <summary>
    /// Sets up the player context provider, the game configuration and the claim service. The
    /// claim service reads the storage rows through the player's own context, so the rows created
    /// here are visible to it.
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

        this._gameContext = CreateGameContext(this._contextProvider, this._configuration);
        this._service = new CashShopStorageClaimService(NullLogger<CashShopStorageClaimService>.Instance, new UnsafeContextRetirement(NullLogger<UnsafeContextRetirement>.Instance));
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
    /// Uma retirada válida entrega o item referenciado pela linha ao inventário do personagem
    /// (com a Quantity preservada em Durability) e consome a linha no mesmo commit.
    /// </summary>
    [Test]
    public async Task ClaimAsync_ValidRow_MovesItemToInventoryAndConsumesRow()
    {
        var accountId = Guid.NewGuid();
        var player = await this.CreatePlayerWithAccountAsync(accountId).ConfigureAwait(false);
        var row = await this.CreateRowWithItemAsync(accountId, storageIndex: 0, itemSeq: 0, durability: 20).ConfigureAwait(false);

        var result = await this._service.ClaimAsync(player, storageIndex: 0, itemSeq: 0, StorageItemTypeProduct).ConfigureAwait(false);
        var stored = await this.GetStoredItemsAsync(accountId).ConfigureAwait(false);
        var inventoryItems = player.Inventory!.Items.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.ErrorCode, Is.EqualTo(StorageClaimErrorCode.Success));
            Assert.That(stored, Is.Empty, "A linha do storage é consumida no mesmo commit.");
            Assert.That(inventoryItems, Has.Count.EqualTo(1), "O item é entregue ao inventário.");
            Assert.That(inventoryItems[0].Definition?.Group, Is.EqualTo(ItemGroup));
            Assert.That(inventoryItems[0].Definition?.Number, Is.EqualTo(ItemNumber));
            Assert.That(inventoryItems[0].Durability, Is.EqualTo(20), "A Quantity persistida em Durability é preservada na retirada.");
        });
        Assert.That(row, Is.Not.Null);
    }

    /// <summary>
    /// Inventário cheio: nada é entregue e a linha permanece no storage — nenhuma perda.
    /// </summary>
    [Test]
    public async Task ClaimAsync_FullInventory_RowStaysAndNothingIsDelivered()
    {
        var accountId = Guid.NewGuid();
        var player = await this.CreatePlayerWithAccountAsync(accountId).ConfigureAwait(false);
        var row = await this.CreateRowWithItemAsync(accountId, storageIndex: 0, itemSeq: 0, durability: 20, width: 9, height: 9).ConfigureAwait(false);

        var result = await this._service.ClaimAsync(player, storageIndex: 0, itemSeq: 0, StorageItemTypeProduct).ConfigureAwait(false);
        var stored = await this.GetStoredItemsAsync(accountId).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False, "Um item que não cabe no inventário não pode ser entregue.");
            Assert.That(result.ErrorCode, Is.EqualTo(StorageClaimErrorCode.InventoryFull), "O código de inventário cheio é o 21 do ReceiveIGS_UseStorageItem.");
            Assert.That(stored, Has.Count.EqualTo(1), "A linha permanece no storage.");
            Assert.That(player.Inventory!.Items, Is.Empty, "Nenhum item é entregue.");
        });
        Assert.That(row, Is.Not.Null);
    }

    /// <summary>
    /// Índice inexistente: recusa sem efeito — nada é removido, nada é entregue.
    /// </summary>
    [Test]
    public async Task ClaimAsync_UnknownIndex_RefusedWithoutEffect()
    {
        var accountId = Guid.NewGuid();
        var player = await this.CreatePlayerWithAccountAsync(accountId).ConfigureAwait(false);
        await this.CreateRowWithItemAsync(accountId, storageIndex: 0, itemSeq: 0, durability: 20).ConfigureAwait(false);

        var result = await this._service.ClaimAsync(player, storageIndex: 5, itemSeq: 5, StorageItemTypeProduct).ConfigureAwait(false);
        var stored = await this.GetStoredItemsAsync(accountId).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(StorageClaimErrorCode.StorageNotFound));
            Assert.That(stored, Has.Count.EqualTo(1), "A linha existente não é tocada.");
            Assert.That(player.Inventory!.Items, Is.Empty);
        });
    }

    /// <summary>
    /// Um StorageIndex de outra conta não é encontrado para o jogador autenticado: a recusa não
    /// revela a existência do item e não altera o registro da outra conta.
    /// </summary>
    [Test]
    public async Task ClaimAsync_IndexOfAnotherAccount_IsRefusedWithoutSideEffects()
    {
        var otherAccountId = Guid.NewGuid();
        var player = await this.CreatePlayerWithAccountAsync(Guid.NewGuid()).ConfigureAwait(false);
        var otherRow = await this.CreateRowWithItemAsync(otherAccountId, storageIndex: 0, itemSeq: 0, durability: 20).ConfigureAwait(false);

        var result = await this._service.ClaimAsync(player, storageIndex: 0, itemSeq: 0, StorageItemTypeProduct).ConfigureAwait(false);
        var otherStored = await this.GetStoredItemsAsync(otherAccountId).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False, "A linha de outra conta não pode ser entregue.");
            Assert.That(result.ErrorCode, Is.EqualTo(StorageClaimErrorCode.StorageNotFound), "A recusa é 'não encontrado', sem revelar a existência do item.");
            Assert.That(otherStored, Has.Count.EqualTo(1), "O registro da outra conta permanece intacto.");
            Assert.That(otherRow.Item, Is.Not.Null, "O item da outra conta permanece no storage dela.");
            Assert.That(player.Inventory!.Items, Is.Empty);
        });
    }

    /// <summary>
    /// Requisição repetida do mesmo par (duplo clique): apenas o primeiro claim entrega; o
    /// segundo não encontra mais a linha — 1 linha = no máximo 1 item entregue.
    /// </summary>
    [Test]
    public async Task ClaimAsync_Twice_OnlyOneClaimSucceeds()
    {
        var accountId = Guid.NewGuid();
        var player = await this.CreatePlayerWithAccountAsync(accountId).ConfigureAwait(false);
        await this.CreateRowWithItemAsync(accountId, storageIndex: 0, itemSeq: 0, durability: 20).ConfigureAwait(false);

        var first = await this._service.ClaimAsync(player, storageIndex: 0, itemSeq: 0, StorageItemTypeProduct).ConfigureAwait(false);
        var second = await this._service.ClaimAsync(player, storageIndex: 0, itemSeq: 0, StorageItemTypeProduct).ConfigureAwait(false);
        var inventoryItems = player.Inventory!.Items.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(first.Success, Is.True, "O primeiro claim entrega o item.");
            Assert.That(second.Success, Is.False, "O segundo claim não encontra a linha: ela foi consumida no primeiro commit.");
            Assert.That(second.ErrorCode, Is.EqualTo(StorageClaimErrorCode.StorageNotFound));
            Assert.That(inventoryItems, Has.Count.EqualTo(1), "No máximo um item é entregue.");
        });
    }

    /// <summary>
    /// Item sem ItemDefinition (a forma da incompatibilidade 6702 aceita): a linha permanece,
    /// nenhum item é entregue e nenhuma exceção escapa — a remoção nunca vem antes da validação.
    /// </summary>
    [Test]
    public async Task ClaimAsync_ItemWithoutDefinition_RowStaysAndNoItemIsCreated()
    {
        var accountId = Guid.NewGuid();
        var player = await this.CreatePlayerWithAccountAsync(accountId).ConfigureAwait(false);
        await this.CreateRowAsync(accountId, storageIndex: 0, itemSeq: 0, withItem: true, withDefinition: false).ConfigureAwait(false);

        var result = await this._service.ClaimAsync(player, storageIndex: 0, itemSeq: 0, StorageItemTypeProduct).ConfigureAwait(false);
        var stored = await this.GetStoredItemsAsync(accountId).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False, "Um item sem definição não pode ser entregue.");
            Assert.That(result.ErrorCode, Is.EqualTo(StorageClaimErrorCode.CannotUse));
            Assert.That(stored, Has.Count.EqualTo(1), "A linha permanece no storage — a remoção nunca vem antes da validação.");
            Assert.That(player.Inventory!.Items, Is.Empty, "Nenhum item é entregue.");
        });
    }

    /// <summary>
    /// Linha sem item entregue (compra antiga): a linha permanece e nada é entregue.
    /// </summary>
    [Test]
    public async Task ClaimAsync_RowWithoutItem_RowStays()
    {
        var accountId = Guid.NewGuid();
        var player = await this.CreatePlayerWithAccountAsync(accountId).ConfigureAwait(false);
        await this.CreateRowAsync(accountId, storageIndex: 0, itemSeq: 0, withItem: false, withDefinition: false).ConfigureAwait(false);

        var result = await this._service.ClaimAsync(player, storageIndex: 0, itemSeq: 0, StorageItemTypeProduct).ConfigureAwait(false);
        var stored = await this.GetStoredItemsAsync(accountId).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo(StorageClaimErrorCode.CannotUse));
            Assert.That(stored, Has.Count.EqualTo(1), "A linha permanece no storage.");
            Assert.That(player.Inventory!.Items, Is.Empty);
        });
    }

    /// <summary>
    /// Um presente retirado pelo destinatário preserva a Quantity (Durability) e não sofre
    /// segundo débito: a retirada não consulta WCoin.
    /// </summary>
    [Test]
    public async Task ClaimAsync_GiftRow_PreservesQuantityAndDoesNotDebit()
    {
        var receiverAccountId = Guid.NewGuid();
        var senderAccountId = Guid.NewGuid();
        var player = await this.CreatePlayerWithAccountAsync(receiverAccountId).ConfigureAwait(false);
        await this.CreateWalletAsync(senderAccountId, 1000).ConfigureAwait(false);
        // A linha de presente como o gift a cria: Item com Durability = Quantity (20).
        await this.CreateRowWithItemAsync(receiverAccountId, storageIndex: 0, itemSeq: 0, durability: 20, isGift: true).ConfigureAwait(false);

        var result = await this._service.ClaimAsync(player, storageIndex: 0, itemSeq: 0, StorageItemTypeProduct).ConfigureAwait(false);
        var senderBalance = await this.GetWalletBalanceAsync(senderAccountId).ConfigureAwait(false);
        var stored = await this.GetStoredItemsAsync(receiverAccountId).ConfigureAwait(false);
        var inventoryItems = player.Inventory!.Items.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(inventoryItems, Has.Count.EqualTo(1));
            Assert.That(inventoryItems[0].Durability, Is.EqualTo(20), "A Quantity do presente é preservada na retirada.");
            Assert.That(senderBalance, Is.EqualTo(1000), "A retirada não sofre segundo débito.");
            Assert.That(stored, Is.Empty, "A linha do presente é consumida.");
        });
    }

    /// <summary>
    /// Cria um jogador cuja conta autenticada é a informada, para que as linhas desta conta
    /// resolvam como suas.
    /// </summary>
    private async ValueTask<Player> CreatePlayerWithAccountAsync(Guid accountId)
    {
        var player = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);
        player.Account!.Id = accountId;
        return player;
    }

    /// <summary>
    /// Cria uma linha de storage com o item entregue, como as fazem a compra e o gift: o item é
    /// criado no contexto, recebe Definition e Durability (a Quantity), e a linha o referencia.
    /// </summary>
    private async ValueTask<CashShopStorageItem> CreateRowWithItemAsync(
        Guid accountId,
        int storageIndex,
        int itemSeq,
        double durability,
        bool isGift = false,
        byte width = 1,
        byte height = 1)
    {
        this._configuration.Items.Add(new ItemDefinition { Group = ItemGroup, Number = ItemNumber, Name = "Test Item", Width = width, Height = height });
        return await this.CreateRowAsync(accountId, storageIndex, itemSeq, withItem: true, withDefinition: true, isGift: isGift, durability: durability).ConfigureAwait(false);
    }

    /// <summary>
    /// Cria uma linha de storage persistida no contexto compartilhado.
    /// </summary>
    private async ValueTask<CashShopStorageItem> CreateRowAsync(
        Guid accountId,
        int storageIndex,
        int itemSeq,
        bool withItem,
        bool withDefinition,
        bool isGift = false,
        double durability = 0)
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
        row.IsGift = isGift;
        if (withItem)
        {
            var item = context.CreateNew<Item>();
            item.Definition = withDefinition
                ? this._configuration.Items.FirstOrDefault(candidate => candidate.Group == ItemGroup && candidate.Number == ItemNumber)
                : null;
            item.Durability = durability;
            row.Item = item;
        }

        await context.SaveChangesAsync().ConfigureAwait(false);
        return row;
    }

    /// <summary>
    /// Lê as linhas de storage de uma conta do contexto compartilhado.
    /// </summary>
    private async ValueTask<IReadOnlyList<CashShopStorageItem>> GetStoredItemsAsync(Guid accountId)
    {
        using var context = this._contextProvider.CreateNewContext(this._configuration);
        var items = await context.GetAsync<CashShopStorageItem>().ConfigureAwait(false);
        return items.Where(item => item.AccountId == accountId).ToList();
    }

    /// <summary>
    /// Cria uma carteira WCoin para a conta informada.
    /// </summary>
    private async ValueTask CreateWalletAsync(Guid accountId, long balance)
    {
        using var context = this._contextProvider.CreateNewContext(this._configuration);
        var wallet = context.CreateNew<WCoinWallet>();
        wallet.Id = accountId;
        wallet.Balance = balance;
        await context.SaveChangesAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Lê o saldo da carteira WCoin de uma conta.
    /// </summary>
    private async ValueTask<long> GetWalletBalanceAsync(Guid accountId)
    {
        using var context = this._contextProvider.CreateNewContext(this._configuration);
        var wallet = await context.GetByIdAsync<WCoinWallet>(accountId).ConfigureAwait(false);
        return wallet?.Balance ?? 0;
    }
}
