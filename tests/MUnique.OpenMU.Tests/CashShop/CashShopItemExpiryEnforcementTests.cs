// <copyright file="CashShopItemExpiryEnforcementTests.cs" company="MUnique">
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
using MUnique.OpenMU.DataModel.Attributes;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Configuration.ItemCrafting;
using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.PlayerActions.Items;
using MUnique.OpenMU.GameLogic.PlayerActions.ItemConsumeActions;
using MUnique.OpenMU.GameLogic.PlayerActions.Trade;
using MUnique.OpenMU.GameLogic.Views.Trade;
using MUnique.OpenMU.GameLogic.Views.NPC;
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.Persistence.InMemory;
using MUnique.OpenMU.PlugIns;
using NUnit.Framework;

/// <summary>
/// Testes do enforcement central de expiração (Fase 10, GATES F/H): um item temporário com
/// <c>ExpiresAtUtc</c> no passado não pode ser equipado, usado, negociado, transferido, mesclado
/// em stack nem usado como material de craft — e não concede atributos. Itens legados (NULL) e
/// temporários ativos não são afetados.
/// </summary>
[TestFixture]
public class CashShopItemExpiryEnforcementTests
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
    /// GATE F: um item com <c>ExpiresAtUtc</c> no passado não pode ser EQUIPADO — o move para
    /// slot equipável é recusado e o item fica onde está.
    /// </summary>
    [Test]
    public async Task MoveItemAsync_EquippingExpiredItem_IsRefused()
    {
        var player = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);
        var definition = this.CreateWearableDefinition();
        this.QualifyForCharacterClass(definition, player);
        var item = this.CreateItem(definition, 1, ExpiresAtUtc: ExpiredUtc);
        await player.Inventory!.AddItemAsync(20, item).ConfigureAwait(false);

        var action = new MoveItemAction();
        await action.MoveItemAsync(player, 20, Storages.Inventory, InventoryConstants.RightHandSlot, Storages.Inventory).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(player.Inventory.GetItem(20), Is.SameAs(item), "O item expirado fica no slot de origem.");
            Assert.That(player.Inventory.GetItem(InventoryConstants.RightHandSlot), Is.Null, "O item expirado não é equipado.");
            Assert.That(player.Inventory.EquippedItems, Does.Not.Contain(item));
        });
    }

    /// <summary>
    /// GATE F (guarda): item temporário ATIVO e item LEGADO (sem datas) continuam equipando.
    /// </summary>
    [Test]
    public async Task MoveItemAsync_EquippingActiveOrLegacyItem_Succeeds(
        [Values(null, "2999-01-01")] string? expiresAtUtc)
    {
        var player = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);
        var definition = this.CreateWearableDefinition();
        this.QualifyForCharacterClass(definition, player);
        var item = this.CreateItem(definition, 1, ExpiresAtUtc: expiresAtUtc is null ? null : DateTime.Parse(expiresAtUtc));
        await player.Inventory!.AddItemAsync(20, item).ConfigureAwait(false);

        var action = new MoveItemAction();
        await action.MoveItemAsync(player, 20, Storages.Inventory, InventoryConstants.RightHandSlot, Storages.Inventory).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(player.Inventory.GetItem(20), Is.Null, "O item sai do slot de origem.");
            Assert.That(player.Inventory.GetItem(InventoryConstants.RightHandSlot), Is.SameAs(item), "O item é equipado.");
        });
    }

    /// <summary>
    /// GATE F: um item expirado não pode ser colocado na janela de trade.
    /// </summary>
    [Test]
    public async Task MoveItemAsync_TradingExpiredItem_IsRefused()
    {
        var trader1 = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);
        var trader2 = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);
        var definition = this.CreatePlainDefinition();
        var item = this.CreateItem(definition, 1, ExpiresAtUtc: ExpiredUtc);
        await trader1.Inventory!.AddItemAsync(20, item).ConfigureAwait(false);

        await new TradeRequestAction().RequestTradeAsync(trader1, trader2).ConfigureAwait(false);
        await new TradeAcceptAction().HandleTradeAcceptAsync(trader2, true).ConfigureAwait(false);

        var action = new MoveItemAction();
        await action.MoveItemAsync(trader1, 20, Storages.Inventory, 0, Storages.Trade).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(trader1.Inventory.GetItem(20), Is.SameAs(item), "O item expirado não sai do inventário.");
            Assert.That(trader1.TemporaryStorage!.Items, Does.Not.Contain(item), "O item expirado não entra na janela de trade.");
        });
    }

    /// <summary>
    /// GATE H (guarda): um item temporário ATIVO pode ser negociado — o prazo viaja com a
    /// instância (o destinatário recebe o MESMO prazo, sem renovação).
    /// </summary>
    [Test]
    public async Task MoveItemAsync_TradingActiveTemporaryItem_IsAllowed()
    {
        var trader1 = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);
        var trader2 = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);
        var definition = this.CreatePlainDefinition();
        var item = this.CreateItem(definition, 1, ExpiresAtUtc: ActiveUntilUtc);
        await trader1.Inventory!.AddItemAsync(20, item).ConfigureAwait(false);

        await new TradeRequestAction().RequestTradeAsync(trader1, trader2).ConfigureAwait(false);
        await new TradeAcceptAction().HandleTradeAcceptAsync(trader2, true).ConfigureAwait(false);

        var action = new MoveItemAction();
        await action.MoveItemAsync(trader1, 20, Storages.Inventory, 0, Storages.Trade).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(trader1.Inventory.GetItem(20), Is.Null, "O item sai do inventário.");
            Assert.That(trader1.TemporaryStorage!.GetItem(0), Is.SameAs(item), "O item temporário ativo entra no trade.");
            Assert.That(item.ExpiresAtUtc, Is.EqualTo(ActiveUntilUtc), "O prazo NÃO é renovado nem alterado pelo trade.");
        });
    }

    /// <summary>
    /// GATE F: um item expirado NÃO concede atributos — mesmo se de alguma forma estiver
    /// equipado (carregado do banco, caminho alternativo), a fábrica de power-ups não emite nada.
    /// Itens ativos e legados continuam concedendo.
    /// </summary>
    [Test]
    public async Task ItemPowerUpFactory_ExpiredItem_GrantsNoPowerUps(
        [Values("2020-01-01", "2999-01-01", null)] string? expiresAtUtc)
    {
        var player = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);
        var definition = this.CreateWearableDefinition();
        this.QualifyForCharacterClass(definition, player);
        definition.BasePowerUpAttributes.Add(new ItemBasePowerUpDefinition
        {
            TargetAttribute = Stats.DefenseBase,
            BaseValue = 5,
            AggregateType = AggregateType.AddRaw,
        });
        var item = this.CreateItem(definition, 1, ExpiresAtUtc: expiresAtUtc is null ? null : DateTime.Parse(expiresAtUtc));
        item.ItemSlot = InventoryConstants.RightHandSlot;

        var factory = new ItemPowerUpFactory(NullLogger<ItemPowerUpFactory>.Instance);
        var powerUps = factory.GetPowerUps(item, player.Attributes!).ToList();

        var isExpired = expiresAtUtc is not null && DateTime.Parse(expiresAtUtc) <= DateTime.UtcNow;
        Assert.That(
            powerUps,
            isExpired ? Is.Empty : Is.Not.Empty,
            isExpired ? "Item expirado não pode conceder atributos." : "Item ativo/legado continua concedendo atributos.");
    }

    /// <summary>
    /// GATE F: um item expirado não pode ser USADO (consumido) — a durabilidade não muda e o
    /// item permanece. Item temporário ativo é consumido normalmente.
    /// </summary>
    [Test]
    public async Task HandleConsumeRequestAsync_ExpiredItem_IsRefusedAndStays(
        [Values("2020-01-01", "2999-01-01")] string expiresAtUtc)
    {
        var player = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);
        await player.PlayerState.TryAdvanceToAsync(PlayerState.EnteredWorld).ConfigureAwait(false);
        var definition = this.CreatePlainDefinition(consumeEffect: this.CreateConsumeEffect());
        var item = this.CreateItem(definition, 1, ExpiresAtUtc: DateTime.Parse(expiresAtUtc));
        await player.Inventory!.AddItemAsync(20, item).ConfigureAwait(false);

        var action = new ItemConsumeAction();
        await action.HandleConsumeRequestAsync(player, 20, 21, FruitUsage.Undefined).ConfigureAwait(false);

        var isExpired = DateTime.Parse(expiresAtUtc) <= DateTime.UtcNow;
        Assert.That(
            player.Inventory.GetItem(20),
            isExpired ? Is.SameAs(item) : Is.Null,
            isExpired ? "O item expirado não é consumido." : "O item ativo é consumido (durabilidade 1 → 0).");
    }

    /// <summary>
    /// GATE F: um item expirado não pode ser usado como MATERIAL de craft — a mistura falha, o
    /// material NÃO é consumido e nenhum item resultado é criado.
    /// </summary>
    [Test]
    public async Task DoMixAsync_ExpiredRequiredItem_IsRefusedWithoutConsuming()
    {
        var player = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);
        var definition = this.CreatePlainDefinition();
        var material = this.CreateItem(definition, 1, ExpiresAtUtc: ExpiredUtc);
        await player.TemporaryStorage!.AddItemAsync(0, material).ConfigureAwait(false);

        var requirement = new ItemCraftingRequiredItem
        {
            SuccessResult = MixResult.Disappear,
            FailResult = MixResult.Disappear,
            MinimumAmount = 1,
        };
        var links = new List<CraftingRequiredItemLink> { new(new List<Item> { material }, requirement) };
        var resultItem = this.CreateItem(definition, 1);
        var handler = new TestCraftingHandler(links, resultItem);

        var (result, _) = await handler.DoMixAsync(player, socketSlot: 0).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(CraftingResult.Failed), "Material expirado recusa a mistura (fail-safe).");
            Assert.That(player.TemporaryStorage.GetItem(0), Is.SameAs(material), "O material expirado NÃO é consumido pela mistura.");
        });
    }

    /// <summary>
    /// GATE H: mesclar stacks onde um dos lados é temporário é recusado — a mesclagem
    /// destruiria o prazo de um dos lados (perda de metadado).
    /// </summary>
    [Test]
    public async Task MoveItemAsync_StackMergeWithTemporaryItem_IsRefused()
    {
        var player = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);
        var definition = this.CreatePlainDefinition(durability: 10);
        var source = this.CreateItem(definition, 3, ExpiresAtUtc: ExpiredUtc);
        var target = this.CreateItem(definition, 2);
        await player.Inventory!.AddItemAsync(20, source).ConfigureAwait(false);
        await player.Inventory.AddItemAsync(21, target).ConfigureAwait(false);

        var action = new MoveItemAction();
        await action.MoveItemAsync(player, 20, Storages.Inventory, 21, Storages.Inventory).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(player.Inventory.GetItem(20), Is.SameAs(source), "O stack não é mesclado: a origem fica.");
            Assert.That(player.Inventory.GetItem(21), Is.SameAs(target), "O stack não é mesclado: o destino fica.");
            Assert.That(target.Durability, Is.EqualTo(2), "A durabilidade do destino não muda (nada é absorvido).");
        });
    }

    /// <summary>
    /// GATE H (guarda): stacks de itens PERMANENTES continuam se mesclando.
    /// </summary>
    [Test]
    public async Task MoveItemAsync_StackMergeOfPermanentItems_Succeeds()
    {
        var player = await PlayerTestHelper.CreatePlayerAsync(this._gameContext).ConfigureAwait(false);
        var definition = this.CreatePlainDefinition(durability: 10);
        var source = this.CreateItem(definition, 3);
        var target = this.CreateItem(definition, 2);
        await player.Inventory!.AddItemAsync(20, source).ConfigureAwait(false);
        await player.Inventory.AddItemAsync(21, target).ConfigureAwait(false);

        var action = new MoveItemAction();
        await action.MoveItemAsync(player, 20, Storages.Inventory, 21, Storages.Inventory).ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(player.Inventory.GetItem(20), Is.Null, "Stacks permanentes seguem se mesclando: a origem é consumida.");
            Assert.That(player.Inventory.GetItem(21), Is.SameAs(target));
            Assert.That(target.Durability, Is.EqualTo(5));
        });
    }

    private ItemDefinition CreateWearableDefinition()
    {
        // Moq com coleções explícitas: as coleções do DataModel puro são null!, e as do
        // BasicModel só aceitam instâncias do próprio assembly (CollectionAdapter). Com Moq,
        // QualifiedCharacters contém o MESMO proxy de CharacterClass do jogador (igualdade de
        // referência) e o tipo do material é o DataModel puro.
        var slots = new Mock<ItemSlotType>();
        slots.Setup(s => s.ItemSlots).Returns(new List<int> { InventoryConstants.RightHandSlot });
        var definition = new Mock<ItemDefinition>();
        definition.Setup(d => d.ItemSlot).Returns(slots.Object);
        definition.Setup(d => d.Requirements).Returns(new List<AttributeRequirement>());
        definition.Setup(d => d.QualifiedCharacters).Returns(new List<CharacterClass>());
        definition.Setup(d => d.BasePowerUpAttributes).Returns(new List<ItemBasePowerUpDefinition>());
        definition.Setup(d => d.PossibleItemSetGroups).Returns(new List<ItemSetGroup>());
        var result = definition.Object;
        result.Width = 1;
        result.Height = 1;
        result.Durability = 1;
        this._configuration.Items.Add(result);
        return result;
    }

    private ItemDefinition CreatePlainDefinition(byte durability = 1, MagicEffectDefinition? consumeEffect = null)
    {
        var definition = new Mock<ItemDefinition>();
        definition.Setup(d => d.Requirements).Returns(new List<AttributeRequirement>());
        definition.Setup(d => d.QualifiedCharacters).Returns(new List<CharacterClass>());
        if (consumeEffect is not null)
        {
            // ConsumeEffect é virtual: num proxy Moq solto o SETTER não persiste.
            definition.Setup(d => d.ConsumeEffect).Returns(consumeEffect);
        }

        var result = definition.Object;
        result.Width = 1;
        result.Height = 1;
        result.Durability = durability;
        this._configuration.Items.Add(result);
        return result;
    }

    private Item CreateItem(ItemDefinition definition, double durability, DateTime? ExpiresAtUtc = null)
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

    private MagicEffectDefinition CreateConsumeEffect()
    {
        // Tipos BasicModel: coleções inicializadas pelo ctor gerado e acessadores
        // Value/AggregateType disponíveis (o DataModel puro deixa PowerUpDefinitions = null!).
        var effect = new MUnique.OpenMU.Persistence.BasicModel.MagicEffectDefinition
        {
            SubType = 1,
            Duration = new MUnique.OpenMU.Persistence.BasicModel.PowerUpDefinitionValue { Value = 60, AggregateType = AggregateType.AddRaw },
        };
        effect.PowerUpDefinitions.Add(new MUnique.OpenMU.Persistence.BasicModel.PowerUpDefinition
        {
            TargetAttribute = Stats.DefenseBase,
            Boost = new MUnique.OpenMU.Persistence.BasicModel.PowerUpDefinitionValue { Value = 10, AggregateType = AggregateType.AddRaw },
        });
        return effect;
    }

    /// <summary>
    /// Qualifica a definição para a classe do personagem do jogador: o BasicModel aceita na
    /// coleção apenas instâncias do seu próprio tipo, e a verificação
    /// (<c>CompliesRequirements</c>) compara por Id — basta uma instância com o mesmo Id.
    /// </summary>
    private void QualifyForCharacterClass(ItemDefinition definition, Player player)
    {
        definition.QualifiedCharacters.Add(player.SelectedCharacter!.CharacterClass!);
    }

    /// <summary>
    /// Dobrável mínimo de handler de craft: devolve os links preparados pelo teste e não produz
    /// resultado — isola o gate central de <see cref="BaseItemCraftingHandler.DoMixAsync"/> sobre
    /// o consumo dos materiais.
    /// </summary>
    private sealed class TestCraftingHandler : BaseItemCraftingHandler
    {
        private readonly IList<CraftingRequiredItemLink> _links;
        private readonly Item _resultItem;

        public TestCraftingHandler(IList<CraftingRequiredItemLink> links, Item resultItem)
        {
            this._links = links;
            this._resultItem = resultItem;
        }

        public override CraftingResult? TryGetRequiredItems(Player player, out IList<CraftingRequiredItemLink> items, out byte successRate)
        {
            items = this._links;
            successRate = 100;
            return null;
        }

        protected override int GetPrice(byte successRate, IList<CraftingRequiredItemLink> requiredItems) => 0;

        protected override ValueTask<List<Item>> CreateOrModifyResultItemsAsync(IList<CraftingRequiredItemLink> requiredItems, Player player, byte socketSlot, byte successRate)
            => ValueTask.FromResult(new List<Item> { this._resultItem });
    }
}
