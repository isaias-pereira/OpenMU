// <copyright file="CashShopExpiryOnlineIntegrationTests.cs" company="MUnique">
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
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.DataModel;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.CashShop;
using MUnique.OpenMU.GameLogic.PlayerActions.CashShop;
using MUnique.OpenMU.GameLogic.PlayerActions.Items;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.GameLogic.Views.Inventory;
using MUnique.OpenMU.Interfaces;
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.Persistence.InMemory;
using MUnique.OpenMU.PlugIns;
using NUnit.Framework;

/// <summary>
/// Integração da expiração com o ciclo REAL do <see cref="GameContext"/> (GATE J2/J3):
/// o timer de recuperação periódico (mesmo caminho da regeneração de produção) executa a
/// varredura de expiração em jogadores conectados; logout/login reconcilia o estado persistido.
/// </summary>
/// <remarks>
/// <para>
/// O relógio global NÃO é alterado: os prazos curtos vêm da duração contratada da linha de
/// storage (<c>DurationSeconds = 1</c> segundo) resolvida no claim real — o mesmo fluxo de
/// produção com uma oferta de duração mínima.
/// </para>
/// <para>
/// Periodicidade efetiva: o timer usa <c>GameConfiguration.RecoveryInterval</c> — 3000 ms na
/// configuração de produção (Season 6) e 400 ms nestes testes. O atraso máximo entre
/// <c>ExpiresAtUtc</c> e a atualização visual do cliente é de um ciclo (~3 s em produção),
/// enquanto a inelegibilidade lógica (equipar/usar/negociar) é imediata, no próprio enforce.
/// </para>
/// </remarks>
[TestFixture]
public class CashShopExpiryOnlineIntegrationTests
{
    private static readonly DateTime ReferenceUtc = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private InMemoryPersistenceContextProvider _contextProvider = null!;
    private GameConfiguration _configuration = null!;
    private IGameContext _gameContext = null!;
    private CashShopStorageClaimService _claimService = null!;
    private readonly List<object> _clientNotifications = new();

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
        configurationMock.Setup(c => c.Attributes).Returns(new List<AttributeDefinition>());
        configurationMock.Setup(c => c.GlobalAttributeCombinations).Returns(new List<MUnique.OpenMU.AttributeSystem.AttributeRelationship>());
        configurationMock.Setup(c => c.GlobalBaseAttributeValues).Returns(new List<ConstValueAttribute> { new(1, Stats.MoneyAmountRate) });
        this._configuration = configurationMock.Object;

        var map = new Mock<GameMapDefinition>();
        map.SetupAllProperties();
        map.Setup(m => m.DropItemGroups).Returns(new List<DropItemGroup>());
        map.Setup(m => m.MonsterSpawns).Returns(new List<MonsterSpawnArea>());
        map.Object.TerrainData = new byte[ushort.MaxValue + 3];
        this._configuration.Maps.Add(map.Object);
    }

    /// <summary>
    /// Cria o GameContext com o intervalo de recuperação informado (o mesmo timer de produção).
    /// </summary>
    private void CreateGameContext(int recoveryIntervalMs)
    {
        this._configuration.RecoveryInterval = recoveryIntervalMs;
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
    }

    /// <summary>
    /// J2 (ciclo online real): o item equipado com duração de 1 segundo é removido pelo timer de
    /// recuperação do GameContext — remoção do inventário e da persistência, descarte dos
    /// power-ups e notificação de remoção ao cliente (IItemRemovedPlugIn). Sem movimentação nem
    /// ações do jogador: apenas o ciclo periódico.
    /// </summary>
    [Test]
    public async Task RecoveryCycle_RemovesExpiredEquippedItemAndNotifiesClient()
    {
        this.CreateGameContext(recoveryIntervalMs: 400);
        var itemRemovedMock = new Mock<IItemRemovedPlugIn>();
        var itemRemoved = itemRemovedMock.Object;
        var player = await PlayerTestHelper.CreatePlayerAsync(
            this._gameContext,
            () => new SingleViewPlugInContainer(itemRemoved)).ConfigureAwait(false);
        var accountId = Guid.NewGuid();
        player.Account!.Id = accountId;
        // Registro REAL no GameContext: é o que o GameServer faz quando o jogador entra no
        // mundo — sem isto, o ciclo de recuperação não itera o jogador.
        await this._gameContext.AddPlayerAsync(player).ConfigureAwait(false);

        // Item temporário com duração de 1 segundo, entregue pelo claim real e EQUIPADO.
        var definition = this.CreateWearableDefinition(player);
        await this.CreateRowWithItemAsync(accountId, storageIndex: 0, itemSeq: 0, durationSeconds: 1, definition).ConfigureAwait(false);
        var claim = await this._claimService.ClaimAsync(player, storageIndex: 0, itemSeq: 0, (byte)'P').ConfigureAwait(false);
        Assert.That(claim.Success, Is.True, claim.ErrorMessage);
        var item = player.Inventory!.Items.Single();
        var inventorySlot = item.ItemSlot;

        var move = new MoveItemAction();
        await move.MoveItemAsync(player, inventorySlot, Storages.Inventory, InventoryConstants.RightHandSlot, Storages.Inventory).ConfigureAwait(false);
        Assert.That(player.Inventory.GetItem(InventoryConstants.RightHandSlot), Is.SameAs(item), "Sanidade: item ativo equipado.");
        Assert.That(player.Attributes!.ItemPowerUps.ContainsKey(item), Is.True, "Sanidade: item ativo concede power-up.");
        Assert.That(item.ExpiresAtUtc, Is.Not.Null);
        var expiresAt = item.ExpiresAtUtc!.Value;

        // Nenhuma ação do jogador: apenas o ciclo periódico do GameContext.
        var waitUntil = expiresAt.AddMilliseconds(1800);
        var remaining = waitUntil - DateTime.UtcNow;
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining).ConfigureAwait(false);
        }

        Assert.Multiple(() =>
        {
            Assert.That(player.Inventory.GetItem(InventoryConstants.RightHandSlot), Is.Null, "O item expirado foi removido do slot pelo ciclo.");
            Assert.That(player.Inventory.Items, Is.Empty, "O item expirado foi removido do inventário.");
            Assert.That(player.SelectedCharacter!.Inventory!.Items.Any(i => i.GetId() == item.GetId()), Is.False, "O item foi removido da persistência.");
            Assert.That(player.Attributes!.ItemPowerUps.ContainsKey(item), Is.False, "Os power-ups do item expirado foram descartados (recálculo de atributos).");
        });
        itemRemovedMock.Verify(p => p.RemoveItemAsync(It.IsAny<byte>()), Times.AtLeastOnce, "O cliente deve ser notificado da remoção (IItemRemovedPlugIn).");
    }

    /// <summary>
    /// J2 (complemento): enquanto o prazo não vence, o ciclo periódico NÃO remove o item.
    /// </summary>
    [Test]
    public async Task RecoveryCycle_KeepsActiveTemporaryItem()
    {
        this.CreateGameContext(recoveryIntervalMs: 400);
        var player = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);
        var accountId = Guid.NewGuid();
        player.Account!.Id = accountId;
        await this._gameContext.AddPlayerAsync(player).ConfigureAwait(false);
        var definition = this.CreatePlainDefinition(player);
        await this.CreateRowWithItemAsync(accountId, storageIndex: 0, itemSeq: 0, durationSeconds: 30, definition).ConfigureAwait(false);

        var claim = await this._claimService.ClaimAsync(player, storageIndex: 0, itemSeq: 0, (byte)'P').ConfigureAwait(false);
        Assert.That(claim.Success, Is.True, claim.ErrorMessage);

        await Task.Delay(millisecondsDelay: 1200).ConfigureAwait(false);

        var inventoryItems = player.Inventory!.Items.ToList();
        Assert.Multiple(() =>
        {
            Assert.That(inventoryItems, Has.Count.EqualTo(1), "Item ativo não é tocado pelo ciclo.");
            Assert.That(inventoryItems.Single().ExpiresAtUtc, Is.Not.Null);
        });
    }

    /// <summary>
    /// J3-A + invariante 3/7: o item temporário VÁLIDO sobrevive ao logout/login com o MESMO GUID
    /// e os MESMOS timestamps — o reinício não renova o prazo nem reativa a contagem.
    /// </summary>
    [Test]
    public async Task Relog_ValidTemporaryItem_KeepsGuidAndExpiry()
    {
        this.CreateGameContext(recoveryIntervalMs: int.MaxValue);
        var player = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);
        var accountId = Guid.NewGuid();
        player.Account!.Id = accountId;
        var definition = this.CreatePlainDefinition(player);
        await this.CreateRowWithItemAsync(accountId, storageIndex: 0, itemSeq: 0, durationSeconds: 86400, definition).ConfigureAwait(false);

        var claim = await this._claimService.ClaimAsync(player, storageIndex: 0, itemSeq: 0, (byte)'P').ConfigureAwait(false);
        Assert.That(claim.Success, Is.True, claim.ErrorMessage);
        var originalId = player.Inventory!.Items.Single().GetId();
        var originalActivated = player.Inventory.Items.Single().ActivatedAtUtc;
        var originalExpires = player.Inventory.Items.Single().ExpiresAtUtc;
        Assert.That(originalActivated, Is.Not.Null);
        Assert.That(originalExpires, Is.Not.Null);

        var selectedCharacter = player.SelectedCharacter!;
        await player.RemoveFromGameAsync().ConfigureAwait(false);
        await player.SetSelectedCharacterAsync(selectedCharacter).ConfigureAwait(false);

        var afterRelog = player.Inventory!.Items.SingleOrDefault();
        Assert.Multiple(() =>
        {
            Assert.That(afterRelog, Is.Not.Null, "O item temporário válido sobrevive ao logout/login.");
            Assert.That(afterRelog!.GetId(), Is.EqualTo(originalId), "O GUID original é preservado.");
            Assert.That(afterRelog!.ActivatedAtUtc, Is.EqualTo(originalActivated), "A ativação não é recalculada no reinício.");
            Assert.That(afterRelog!.ExpiresAtUtc, Is.EqualTo(originalExpires), "O prazo não é renovado no reinício (invariante 7).");
        });
    }

    /// <summary>
    /// J3-C: item EQUIPADO quando o personagem desconectou e venceu offline — a reconciliação da
    /// entrada no mundo remove o item e nenhum power-up fantasma fica registrado.
    /// </summary>
    [Test]
    public async Task Relog_EquippedItemExpiredWhileDisconnected_IsRemovedWithoutGhostPowerUps()
    {
        this.CreateGameContext(recoveryIntervalMs: int.MaxValue);
        var player = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);
        var accountId = Guid.NewGuid();
        player.Account!.Id = accountId;
        var definition = this.CreateWearableDefinition(player);
        await this.CreateRowWithItemAsync(accountId, storageIndex: 0, itemSeq: 0, durationSeconds: 86400, definition).ConfigureAwait(false);

        var claim = await this._claimService.ClaimAsync(player, storageIndex: 0, itemSeq: 0, (byte)'P').ConfigureAwait(false);
        Assert.That(claim.Success, Is.True, claim.ErrorMessage);
        var item = player.Inventory!.Items.Single();
        var move = new MoveItemAction();
        await move.MoveItemAsync(player, item.ItemSlot, Storages.Inventory, InventoryConstants.RightHandSlot, Storages.Inventory).ConfigureAwait(false);
        Assert.That(player.Attributes!.ItemPowerUps.ContainsKey(item), Is.True, "Sanidade: item equipado ativo.");

        var selectedCharacter = player.SelectedCharacter!;
        await player.RemoveFromGameAsync().ConfigureAwait(false);

        // O vencimento acontece enquanto o personagem está desconectado: a instância persistida
        // no provider InMemory é a mesma referência, então basta antecipar o prazo.
        item.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
        await player.SetSelectedCharacterAsync(selectedCharacter).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(player.Inventory!.Items, Is.Empty, "O item vencido offline é reconciliado na entrada no mundo.");
            Assert.That(player.Attributes!.ItemPowerUps.ContainsKey(item), Is.False, "Nenhum power-up fantasma permanece.");
        });
    }

    /// <summary>
    /// J3-E: item PERMANENTE explícito (duração contratada 0) sobrevive ao logout/login sem
    /// receber datas.
    /// </summary>
    [Test]
    public async Task Relog_PermanentItem_SurvivesWithoutTimestamps()
    {
        this.CreateGameContext(recoveryIntervalMs: int.MaxValue);
        var player = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);
        var accountId = Guid.NewGuid();
        player.Account!.Id = accountId;
        var definition = this.CreatePlainDefinition(player);
        await this.CreateRowWithItemAsync(accountId, storageIndex: 0, itemSeq: 0, durationSeconds: 0, definition).ConfigureAwait(false);

        var claim = await this._claimService.ClaimAsync(player, storageIndex: 0, itemSeq: 0, (byte)'P').ConfigureAwait(false);
        Assert.That(claim.Success, Is.True, claim.ErrorMessage);
        var originalId = player.Inventory!.Items.Single().GetId();
        var selectedCharacter = player.SelectedCharacter!;
        await player.RemoveFromGameAsync().ConfigureAwait(false);
        await player.SetSelectedCharacterAsync(selectedCharacter).ConfigureAwait(false);

        var afterRelog = player.Inventory!.Items.SingleOrDefault();
        Assert.Multiple(() =>
        {
            Assert.That(afterRelog, Is.Not.Null, "O item permanente sobrevive.");
            Assert.That(afterRelog!.GetId(), Is.EqualTo(originalId));
            Assert.That(afterRelog!.ActivatedAtUtc, Is.Null, "Permanente não recebe datas.");
            Assert.That(afterRelog!.ExpiresAtUtc, Is.Null);
        });
    }

    /// <summary>
    /// J3-G (invariante 4 — fail-safe): item com ativação ÓRFÃ (ActivatedAtUtc sem
    /// ExpiresAtUtc) é metadado inconsistente — a varredura o remove em vez de deixá-lo virar
    /// permanente silenciosamente.
    /// </summary>
    [Test]
    public async Task Sweep_ItemWithOrphanActivation_IsRemovedFailSafe()
    {
        this.CreateGameContext(recoveryIntervalMs: int.MaxValue);
        var player = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);
        var definition = this.CreatePlainDefinition(player);
        var item = this.CreatePersistedItem(player, definition);
        item.ActivatedAtUtc = ReferenceUtc; // ativação sem prazo: inconsistente
        item.ExpiresAtUtc = null;
        await player.Inventory!.AddItemAsync(20, item).ConfigureAwait(false);
        await player.PersistenceContext.SaveChangesAsync().ConfigureAwait(false);

        var removed = await ExpiredTemporaryItemService.SweepExpiredItemsAsync(player).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.EqualTo(1), "Metadado inconsistente é removido (fail-safe).");
            Assert.That(player.Inventory.Items, Is.Empty);
            Assert.That(player.SelectedCharacter!.Inventory!.Items.Any(i => i.GetId() == item.GetId()), Is.False, "Removido da persistência.");
        });
    }

    /// <summary>
    /// J3-G (complemento): item com prazo VÁLIDO mas sem ativação gravada é tratado pelo prazo
    /// (a verdade é o vencimento absoluto) — ativo sobrevive; o recálculo da ativação nunca
    /// acontece na varredura (não há reinício de contagem).
    /// </summary>
    [Test]
    public async Task Sweep_ItemWithExpiryButNoActivation_IsHandledByExpiryNotByActivation()
    {
        this.CreateGameContext(recoveryIntervalMs: int.MaxValue);
        var player = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);
        var definition = this.CreatePlainDefinition(player);
        var active = this.CreatePersistedItem(player, definition);
        active.ActivatedAtUtc = null;
        active.ExpiresAtUtc = DateTime.UtcNow.AddHours(1);
        await player.Inventory!.AddItemAsync(20, active).ConfigureAwait(false);
        await player.PersistenceContext.SaveChangesAsync().ConfigureAwait(false);

        var removed = await ExpiredTemporaryItemService.SweepExpiredItemsAsync(player).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.EqualTo(0), "Prazo futuro manda: item válido sobrevive.");
            Assert.That(player.Inventory.GetItem(20), Is.SameAs(active));
            Assert.That(active.ActivatedAtUtc, Is.Null, "A varredura nunca inventa ativação (nenhum reinício de contagem).");
        });
    }

    private ItemDefinition CreateWearableDefinition(Player player)
    {
        var slots = new Mock<ItemSlotType>();
        slots.Setup(s => s.ItemSlots).Returns(new List<int> { InventoryConstants.RightHandSlot });
        var definition = new Mock<ItemDefinition>();
        definition.Setup(d => d.ItemSlot).Returns(slots.Object);
        definition.Setup(d => d.Requirements).Returns(new List<AttributeRequirement>());
        definition.Setup(d => d.QualifiedCharacters).Returns(new List<CharacterClass> { player.SelectedCharacter!.CharacterClass! });
        definition.Setup(d => d.BasePowerUpAttributes).Returns(new List<ItemBasePowerUpDefinition>
        {
            new() { TargetAttribute = Stats.DefenseBase, BaseValue = 5, AggregateType = AggregateType.AddRaw },
        });
        definition.Setup(d => d.PossibleItemSetGroups).Returns(new List<ItemSetGroup>());
        var result = definition.Object;
        result.Width = 1;
        result.Height = 1;
        result.Durability = 1;
        this._configuration.Items.Add(result);
        return result;
    }

    private ItemDefinition CreatePlainDefinition(Player player)
    {
        var definition = new Mock<ItemDefinition>();
        definition.Setup(d => d.Requirements).Returns(new List<AttributeRequirement>());
        definition.Setup(d => d.QualifiedCharacters).Returns(new List<CharacterClass> { player.SelectedCharacter!.CharacterClass! });
        definition.Setup(d => d.BasePowerUpAttributes).Returns(new List<ItemBasePowerUpDefinition>());
        definition.Setup(d => d.PossibleItemSetGroups).Returns(new List<ItemSetGroup>());
        var result = definition.Object;
        result.Width = 1;
        result.Height = 1;
        result.Durability = 1;
        this._configuration.Items.Add(result);
        return result;
    }

    private Item CreatePersistedItem(Player player, ItemDefinition definition)
    {
        var item = player.PersistenceContext.CreateNew<Item>();
        item.Definition = definition;
        item.Durability = 1;
        return item;
    }

    private async ValueTask CreateRowWithItemAsync(Guid accountId, int storageIndex, int itemSeq, int durationSeconds, ItemDefinition definition)
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
        item.Definition = definition;
        item.Durability = 1;
        row.Item = item;
        await context.SaveChangesAsync().ConfigureAwait(false);
        this._claimService = new CashShopStorageClaimService(
            NullLogger<CashShopStorageClaimService>.Instance,
            new UnsafeContextRetirement(NullLogger<UnsafeContextRetirement>.Instance),
            () => this._contextProvider.CreateNewContext());
    }
}

/// <summary>
/// Container de views que devolve SEMPRE a instância registrada — usado para capturar as
/// notificações que o servidor enviaria ao cliente (IItemRemovedPlugIn no GATE J2).
/// </summary>
public sealed class SingleViewPlugInContainer : ICustomPlugInContainer<IViewPlugIn>
{
    private readonly IViewPlugIn viewPlugIn;

    /// <summary>
    /// Initializes a new instance of the <see cref="SingleViewPlugInContainer"/> class.
    /// </summary>
    /// <param name="viewPlugIn">A única instância devolvida para qualquer tipo de view.</param>
    public SingleViewPlugInContainer(IViewPlugIn viewPlugIn)
    {
        this.viewPlugIn = viewPlugIn;
    }

    /// <inheritdoc />
    public T? GetPlugIn<T>()
        where T : class, IViewPlugIn
    {
        return this.viewPlugIn as T;
    }
}
