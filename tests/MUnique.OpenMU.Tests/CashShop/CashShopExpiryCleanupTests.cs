// <copyright file="CashShopExpiryCleanupTests.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests.CashShop;

using System;
using System.Collections.Generic;
using System.Linq;
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
using MUnique.OpenMU.GameLogic.PlayerActions.Items;
using MUnique.OpenMU.Persistence.InMemory;
using MUnique.OpenMU.PlugIns;
using NUnit.Framework;

/// <summary>
/// Testes da limpeza de expiração (Fase 10, GATE G): itens temporários expirados são removidos
/// do inventário (incluídos os equipados, com recálculo de atributos) e do baú, com persistência
/// e reconciliação offline idempotente. Itens ativos e legados não são afetados.
/// </summary>
[TestFixture]
public class CashShopExpiryCleanupTests
{
    private static readonly DateTime ExpiredUtc = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ActiveUntilUtc = new(2999, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private GameConfiguration _configuration = null!;
    private IGameContext _gameContext = null!;

    [SetUp]
    public void SetUp()
    {
        var configurationMock = new Mock<GameConfiguration>();
        configurationMock.SetupAllProperties();
        configurationMock.Setup(c => c.Items).Returns(new List<ItemDefinition>());
        configurationMock.Setup(c => c.Maps).Returns(new List<GameMapDefinition>());
        configurationMock.Setup(c => c.Skills).Returns(new List<Skill>());
        configurationMock.Setup(c => c.PlugInConfigurations).Returns(new List<PlugInConfiguration>());
        configurationMock.Setup(c => c.CharacterClasses).Returns(new List<CharacterClass>());
        configurationMock.Setup(c => c.Attributes).Returns(new List<AttributeDefinition>());
        configurationMock.Setup(c => c.JewelMixes).Returns(new List<JewelMix>());
        configurationMock.Setup(c => c.GlobalAttributeCombinations).Returns(new List<MUnique.OpenMU.AttributeSystem.AttributeRelationship>());
        configurationMock.Setup(c => c.GlobalBaseAttributeValues).Returns(new List<ConstValueAttribute> { new(1, Stats.MoneyAmountRate) });
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
            new InMemoryPersistenceContextProvider(),
            mapInitializer,
            new NullLoggerFactory(),
            new PlugInManager(null, new NullLoggerFactory(), null, null),
            NullDropGenerator.Instance,
            new ConfigurationChangeMediator());
        mapInitializer.PlugInManager = this._gameContext.PlugInManager;
        mapInitializer.PathFinderPool = this._gameContext.PathFinderPool;
    }

    /// <summary>
    /// GATE G: o item expirado EQUIPADO é removido pela varredura — o desequipamento automático
    /// descarta os power-ups dele (recálculo de atributos) e o item sai da persistência.
    /// </summary>
    [Test]
    public async Task SweepExpiredItemsAsync_ExpiredEquippedItem_IsRemovedAndAttributesRecalculated()
    {
        var player = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);
        var definition = this.CreateDefinition(powerUpValue: 5);
        var item = this.CreateItem(definition, durability: 1, ExpiresAtUtc: ActiveUntilUtc);
        await player.Inventory!.AddItemAsync(InventoryConstants.RightHandSlot, item).ConfigureAwait(false);
        Assert.That(player.Attributes!.ItemPowerUps.ContainsKey(item), Is.True, "Sanidade: o item ATIVO equipado concede power-up.");
        item.ExpiresAtUtc = ExpiredUtc; // o momento da expiração com o item equipado.

        var removed = await ExpiredTemporaryItemService.SweepExpiredItemsAsync(player).ConfigureAwait(false);
        var stored = await this.GetPersistedItemsAsync(player).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.EqualTo(1));
            Assert.That(player.Inventory.GetItem(InventoryConstants.RightHandSlot), Is.Null, "O item expirado sai do slot equipado.");
            Assert.That(player.Inventory.Items, Is.Empty);
            Assert.That(player.Attributes!.ItemPowerUps.ContainsKey(item), Is.False, "Os power-ups do item expirado são descartados (recálculo).");
            Assert.That(stored.Any(i => i.ExpiresAtUtc == ExpiredUtc), Is.False, "O item expirado é removido da persistência.");
        });
    }

    /// <summary>
    /// GATE G: item temporário ATIVO e item LEGADO não são tocados pela varredura.
    /// </summary>
    [Test]
    public async Task SweepExpiredItemsAsync_ActiveAndLegacyItems_Unaffected()
    {
        var player = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);
        var definition = this.CreateDefinition(powerUpValue: null);
        var active = this.CreateItem(definition, durability: 1, ExpiresAtUtc: ActiveUntilUtc);
        var legacy = this.CreateItem(definition, durability: 1, ExpiresAtUtc: null);
        await player.Inventory!.AddItemAsync(20, active).ConfigureAwait(false);
        await player.Inventory.AddItemAsync(21, legacy).ConfigureAwait(false);

        var removed = await ExpiredTemporaryItemService.SweepExpiredItemsAsync(player).ConfigureAwait(false);
        var stored = await this.GetPersistedItemsAsync(player).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.EqualTo(0));
            Assert.That(player.Inventory.GetItem(20), Is.SameAs(active), "O item temporário ativo permanece.");
            Assert.That(player.Inventory.GetItem(21), Is.SameAs(legacy), "O item legado permanece.");
            Assert.That(stored, Has.Count.EqualTo(2), "Nada é removido da persistência.");
        });
    }

    /// <summary>
    /// GATE G: o item expirado no BAÚ também é removido pela varredura.
    /// </summary>
    [Test]
    public async Task SweepExpiredItemsAsync_VaultExpiredItem_IsRemoved()
    {
        var player = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);
        var vaultStorage = new Mock<ItemStorage>();
        vaultStorage.Setup(i => i.Items).Returns(new List<Item>());
        player.Vault = new Storage(InventoryConstants.WarehouseSize, vaultStorage.Object);
        var definition = this.CreateDefinition(powerUpValue: null);
        var expired = this.CreateItem(definition, durability: 1, ExpiresAtUtc: ExpiredUtc);
        var active = this.CreateItem(definition, durability: 1, ExpiresAtUtc: ActiveUntilUtc);
        await player.Vault!.AddItemAsync(0, expired).ConfigureAwait(false);
        await player.Vault.AddItemAsync(1, active).ConfigureAwait(false);

        var removed = await ExpiredTemporaryItemService.SweepExpiredItemsAsync(player).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.EqualTo(1));
            Assert.That(player.Vault.GetItem(0), Is.Null, "O item expirado sai do baú.");
            Assert.That(player.Vault.GetItem(1), Is.SameAs(active), "O item ativo permanece no baú.");
        });
    }

    /// <summary>
    /// GATE G: a varredura é idempotente — uma segunda passada não remove nada.
    /// </summary>
    [Test]
    public async Task SweepExpiredItemsAsync_IsIdempotent()
    {
        var player = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);
        var definition = this.CreateDefinition(powerUpValue: null);
        var expired = this.CreateItem(definition, durability: 1, ExpiresAtUtc: ExpiredUtc);
        await player.Inventory!.AddItemAsync(20, expired).ConfigureAwait(false);

        var first = await ExpiredTemporaryItemService.SweepExpiredItemsAsync(player).ConfigureAwait(false);
        var second = await ExpiredTemporaryItemService.SweepExpiredItemsAsync(player).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(1));
            Assert.That(second, Is.EqualTo(0), "A segunda varredura não encontra mais nada.");
            Assert.That(player.Inventory.Items, Is.Empty);
        });
    }

    /// <summary>
    /// GATE G (reconciliação offline): um item expirado persistido no inventário do personagem é
    /// removido no re-log — a entrada no mundo reconcilia o estado antes de o cliente ver a
    /// lista de inventário. Idempotente: o re-log seguinte não encontra nada.
    /// </summary>
    [Test]
    public async Task RelogWithPersistedExpiredItem_IsReconciledOnLoad()
    {
        var player = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);
        var selectedCharacter = player.SelectedCharacter!;
        var definition = this.CreateDefinition(powerUpValue: null);

        // Item expirado criado NO contexto de persistência do jogador e gravado, para que o
        // re-log o carregue do armazenamento persistido.
        var expired = player.PersistenceContext.CreateNew<Item>();
        expired.Definition = definition;
        expired.Durability = 1;
        expired.ExpiresAtUtc = ExpiredUtc;
        await player.Inventory!.AddItemAsync(20, expired).ConfigureAwait(false);
        await player.PersistenceContext.SaveChangesAsync().ConfigureAwait(false);
        Assert.That(player.Inventory.GetItem(20), Is.Not.Null, "Sanidade: o item expirado está no inventário antes do re-log.");

        await player.RemoveFromGameAsync().ConfigureAwait(false);
        await player.SetSelectedCharacterAsync(selectedCharacter).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(player.Inventory!.GetItem(20), Is.Null, "O item expirado é reconciliado na entrada no mundo.");
            Assert.That(player.Inventory.Items, Is.Empty);
        });
    }

    private ItemDefinition CreateDefinition(float? powerUpValue)
    {
        var definition = new Mock<ItemDefinition>();
        definition.Setup(d => d.Requirements).Returns(new List<AttributeRequirement>());
        definition.Setup(d => d.QualifiedCharacters).Returns(new List<CharacterClass>());
        definition.Setup(d => d.BasePowerUpAttributes).Returns(new List<ItemBasePowerUpDefinition>());
        // Sem isto, o handler de equipamento do InventoryStorage faz NRE em PossibleItemSetGroups
        // — exceção descartada silenciosamente pela invocação multi-cast (só a última ValueTask é
        // esperada) — e os power-ups nunca chegam a ser registrados.
        definition.Setup(d => d.PossibleItemSetGroups).Returns(new List<ItemSetGroup>());
        var result = definition.Object;
        result.Width = 1;
        result.Height = 1;
        result.Durability = 1;
        if (powerUpValue is { } value)
        {
            result.BasePowerUpAttributes.Add(new ItemBasePowerUpDefinition
            {
                TargetAttribute = Stats.DefenseBase,
                BaseValue = value,
                AggregateType = AggregateType.AddRaw,
            });
        }

        this._configuration.Items.Add(result);
        return result;
    }

    private Item CreateItem(ItemDefinition definition, double durability, DateTime? ExpiresAtUtc)
    {
        var item = new Mock<Item>();
        item.SetupAllProperties();
        item.Setup(i => i.ItemOptions).Returns(new List<ItemOptionLink>());
        item.Setup(i => i.ItemSetGroups).Returns(new List<ItemOfItemSet>());
        item.Object.Definition = definition;
        item.Object.Durability = durability;
        item.Object.ExpiresAtUtc = ExpiresAtUtc;
        return item.Object;
    }

    private async ValueTask<IReadOnlyList<Item>> GetPersistedItemsAsync(Player player)
    {
        var storage = player.SelectedCharacter!.Inventory;
        if (storage is null)
        {
            return Array.Empty<Item>();
        }

        return storage.Items.ToList();
    }
}
