// <copyright file="CustomMovePlugInActivationTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests.CustomMove;

using System.Collections;
using System.Threading;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.PlayerActions.Chat;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.GameLogic.Properties;
using MUnique.OpenMU.Interfaces;
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.Persistence.InMemory;
using MUnique.OpenMU.PlugIns;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using BasicGameMapDefinition = MUnique.OpenMU.Persistence.BasicModel.GameMapDefinition;

/// <summary>
/// Regression tests for the incident in which a correctly persisted custom move alias
/// (e.g. "/bar") was answered with "The command '/bar' is not available.".
/// Root cause: the <see cref="PlugInConfiguration.IsActive"/> flag of the
/// <see cref="CustomMoveChatCommandPlugIn"/> was deactivated in the database, so the
/// <see cref="PlugInManager"/> removed the plugin from the strategy index of the
/// <see cref="IChatCommandPlugIn"/> plug in point while reading the configuration
/// at startup. In the original implementation, the plugin had registered itself
/// under the "/move" key, so the native <c>MoveChatCommandPlugIn</c> was skipped
/// during registration due to the key conflict, and no strategy remained for "/move"
/// at all. The fix: the custom move plugin now uses its own key
/// (<see cref="CustomMoveChatCommandPlugIn.CommandKey"/>), so the native move command
/// keeps working independently of the custom move plugin and its activation state.
/// <para />
/// These tests reproduce the whole chain which the game server executes at startup:
/// a <see cref="PlugInConfiguration"/> row (as persisted by the admin panel) is passed
/// to the <see cref="PlugInManager"/> constructor, which discovers all plugins and
/// applies the configuration, and a player message is then processed by the
/// <see cref="ChatMessageCommandProcessor"/>.
/// </summary>
[TestFixture]
public class CustomMovePlugInActivationTests
{
    private static readonly Guid CustomMovePlugInId = new("B7A3C9D2-E5F1-4A8B-9C7D-3E6F2A1B4C5D");
    private static readonly Guid LorenciaMapId = new("00000300-0000-0000-0000-000000000000");

    private RecordingViewPlugInContainer viewContainer = null!;
    private FakeDataSource dataSource = null!;
    private ByDataSourceReferenceHandler referenceHandler = null!;
    private BasicGameMapDefinition lorencia = null!;
    private IReadOnlyList<string> originalExcludedAssemblyPrefixes = null!;

    /// <summary>
    /// Excludes the test assembly from the plugin discovery, so that only the real
    /// plugins of the game assemblies are registered. Without this, test helper
    /// subclasses of plugins (for example the <c>RecordingCustomMovePlugIn</c> of the
    /// <see cref="ChatMessageCommandProcessorTests"/>, which inherits the
    /// <c>PlugInAttribute</c> and has its own <c>GuidAttribute</c>) would be discovered
    /// as plugins and take over the custom move strategy key. The game server never
    /// loads the test assembly, so this reproduces its discovery result.
    /// The original setting is restored afterwards; the test suite runs sequentially
    /// (no parallelizable fixtures), so the static state doesn't leak.
    /// </summary>
    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        this.originalExcludedAssemblyPrefixes = PlugInManager.ExcludedAssemblyPrefixes;
        PlugInManager.ExcludedAssemblyPrefixes = this.originalExcludedAssemblyPrefixes.Concat(new[] { "MUnique.OpenMU.Tests" }).ToArray();
    }

    /// <summary>
    /// Restores the original plugin discovery exclusions.
    /// </summary>
    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        PlugInManager.ExcludedAssemblyPrefixes = this.originalExcludedAssemblyPrefixes;
    }

    /// <summary>
    /// Sets up the reference data source which resolves the map reference of the
    /// persisted configuration, as the <see cref="ByDataSourceReferenceHandler"/> of
    /// the admin panel and the game server do.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        this.viewContainer = new RecordingViewPlugInContainer();
        this.dataSource = new FakeDataSource();
        this.lorencia = new BasicGameMapDefinition
        {
            Id = LorenciaMapId,
            Name = "Lorencia",
            // All-zero terrain data marks every coordinate as walkable (0 = walkable),
            // so the configured target (124/131) passes the accessibility check.
            TerrainData = new byte[ushort.MaxValue + 3],
        };
        this.dataSource.Add(this.lorencia);
        this.referenceHandler = new ByDataSourceReferenceHandler(this.dataSource);
    }

    /// <summary>
    /// Tests that a plug in configuration row with <c>IsActive = false</c> (the state
    /// which caused the incident) removes the custom move plugin from the strategy
    /// index of the chat command plug in point, while the native "/move" command
    /// stays effective independently of the custom move plugin.
    /// </summary>
    [Test]
    public void DeactivatedConfigurationRemovesPlugInFromStrategyIndex()
    {
        var manager = this.CreateManager(this.CreateConfigurationRow(isActive: false));

        Assert.That(manager.IsPlugInActive(CustomMovePlugInId), Is.False);
        Assert.That(manager.GetStrategy<IChatCommandPlugIn>(CustomMoveChatCommandPlugIn.CommandKey), Is.Null);
        Assert.That(manager.GetStrategy<IChatCommandPlugIn>("/move"), Is.InstanceOf<MoveChatCommandPlugIn>(), "the native move command must not depend on the custom move plugin");
    }

    /// <summary>
    /// Tests that a deactivated custom move plugin answers the configured alias with
    /// the "command not available" message instead of executing the persisted move.
    /// This is the exact observed incident.
    /// </summary>
    [Test]
    public async Task DeactivatedPlugInAnswersConfiguredAliasWithInvalidCommandAsync()
    {
        var manager = this.CreateManager(this.CreateConfigurationRow(isActive: false));
        var player = await this.CreatePlayerOnManagerAsync(manager).ConfigureAwait(false);
        var initialX = player.SelectedCharacter!.PositionX;
        var initialY = player.SelectedCharacter!.PositionY;

        var processor = new ChatMessageCommandProcessor();
        await processor.ProcessMessageAsync(player, ("/bar", "Tester")).ConfigureAwait(false);

        Assert.That(this.viewContainer.MessagePlugIn.Messages, Has.Count.EqualTo(1));
        var (message, messageType) = this.viewContainer.MessagePlugIn.Messages[0];
        Assert.That(messageType, Is.EqualTo(MessageType.BlueNormal));
        Assert.That(message, Is.EqualTo(player.GetLocalizedMessage(nameof(PlayerMessage.InvalidChatCommand), "/bar")));
        Assert.That(player.SelectedCharacter!.PositionX, Is.EqualTo(initialX), "the player must not be warped");
        Assert.That(player.SelectedCharacter!.PositionY, Is.EqualTo(initialY), "the player must not be warped");
        Assert.That(player.SelectedCharacter!.CurrentMap, Is.Not.SameAs(this.lorencia));
    }

    /// <summary>
    /// Tests that an active configuration row lets the processor resolve the alias
    /// and execute the real custom move plugin, which warps the player to the
    /// configured coordinates (Lorencia, 124/131) of the persisted configuration.
    /// </summary>
    [Test]
    public async Task ActivatedPlugInExecutesAliasAndWarpsToConfiguredCoordinatesAsync()
    {
        var manager = this.CreateManager(this.CreateConfigurationRow(isActive: true));
        var player = await this.CreatePlayerOnManagerAsync(manager).ConfigureAwait(false);

        var processor = new ChatMessageCommandProcessor();
        await processor.ProcessMessageAsync(player, ("/bar", "Tester")).ConfigureAwait(false);

        Assert.That(manager.GetStrategy<IChatCommandPlugIn>(CustomMoveChatCommandPlugIn.CommandKey), Is.InstanceOf<CustomMoveChatCommandPlugIn>());
        Assert.That(manager.GetStrategy<IChatCommandPlugIn>("/move"), Is.InstanceOf<MoveChatCommandPlugIn>(), "the native move command must not be shadowed by the custom move plugin");
        Assert.That(this.viewContainer.MessagePlugIn.Messages, Has.Count.EqualTo(1));
        var (message, messageType) = this.viewContainer.MessagePlugIn.Messages[0];
        Assert.That(messageType, Is.EqualTo(MessageType.BlueNormal));
        Assert.That(message, Is.EqualTo(player.GetLocalizedMessage(nameof(PlayerMessage.CustomMoveSuccess), "bar", "Lorencia")));
        Assert.That(player.SelectedCharacter!.PositionX, Is.EqualTo((byte)124));
        Assert.That(player.SelectedCharacter!.PositionY, Is.EqualTo((byte)131));
        Assert.That(player.SelectedCharacter!.CurrentMap, Is.SameAs(this.lorencia));
    }

    /// <summary>
    /// Tests that activating the plugin after a startup with a deactivated configuration
    /// row restores the alias, without re-registering or re-creating anything. This is
    /// the path which the admin panel's Plugins page takes through the
    /// <c>ConfigurationChangeHandler</c> when the user clicks "Activate".
    /// </summary>
    [Test]
    public async Task ActivationAfterStartupDeactivationRestoresTheAliasAsync()
    {
        var manager = this.CreateManager(this.CreateConfigurationRow(isActive: false));
        var player = await this.CreatePlayerOnManagerAsync(manager).ConfigureAwait(false);
        var processor = new ChatMessageCommandProcessor();

        await processor.ProcessMessageAsync(player, ("/bar", "Tester")).ConfigureAwait(false);
        Assert.That(this.viewContainer.MessagePlugIn.Messages, Has.Count.EqualTo(1));
        Assert.That(this.viewContainer.MessagePlugIn.Messages[0].Message, Is.EqualTo(player.GetLocalizedMessage(nameof(PlayerMessage.InvalidChatCommand), "/bar")));

        manager.ActivatePlugIn(CustomMovePlugInId);
        Assert.That(manager.IsPlugInActive(CustomMovePlugInId), Is.True);
        Assert.That(manager.GetStrategy<IChatCommandPlugIn>(CustomMoveChatCommandPlugIn.CommandKey), Is.InstanceOf<CustomMoveChatCommandPlugIn>());
        this.viewContainer.MessagePlugIn.Messages.Clear();

        await processor.ProcessMessageAsync(player, ("/bar", "Tester")).ConfigureAwait(false);
        Assert.That(this.viewContainer.MessagePlugIn.Messages, Has.Count.EqualTo(1));
        Assert.That(this.viewContainer.MessagePlugIn.Messages[0].Message, Is.EqualTo(player.GetLocalizedMessage(nameof(PlayerMessage.CustomMoveSuccess), "bar", "Lorencia")));
        Assert.That(player.SelectedCharacter!.PositionX, Is.EqualTo((byte)124));
        Assert.That(player.SelectedCharacter!.PositionY, Is.EqualTo((byte)131));
    }

    /// <summary>
    /// Tests that deactivating the plugin at runtime removes the alias again, so that
    /// the configured move stops working immediately.
    /// </summary>
    [Test]
    public async Task DeactivationRemovesTheAliasAgainAsync()
    {
        var manager = this.CreateManager(this.CreateConfigurationRow(isActive: true));
        var player = await this.CreatePlayerOnManagerAsync(manager).ConfigureAwait(false);
        var processor = new ChatMessageCommandProcessor();

        manager.DeactivatePlugIn(CustomMovePlugInId);
        Assert.That(manager.GetStrategy<IChatCommandPlugIn>(CustomMoveChatCommandPlugIn.CommandKey), Is.Null);
        Assert.That(manager.GetStrategy<IChatCommandPlugIn>("/move"), Is.InstanceOf<MoveChatCommandPlugIn>());

        await processor.ProcessMessageAsync(player, ("/bar", "Tester")).ConfigureAwait(false);
        Assert.That(this.viewContainer.MessagePlugIn.Messages, Has.Count.EqualTo(1));
        Assert.That(this.viewContainer.MessagePlugIn.Messages[0].Message, Is.EqualTo(player.GetLocalizedMessage(nameof(PlayerMessage.InvalidChatCommand), "/bar")));
    }

    /// <summary>
    /// Tests that a configuration update, as it is saved by the admin panel and
    /// propagated through <see cref="PlugInManager.ConfigurePlugIn"/>, reaches the
    /// running plugin instance, so that newly added moves work without a restart.
    /// </summary>
    [Test]
    public async Task ConfigurationUpdatePropagatesToActivePlugInAsync()
    {
        var manager = this.CreateManager(this.CreateConfigurationRow(isActive: true));
        var player = await this.CreatePlayerOnManagerAsync(manager).ConfigureAwait(false);
        var processor = new ChatMessageCommandProcessor();

        var updatedConfiguration = this.CreateBarConfiguration();
        updatedConfiguration.Moves.Add(new CustomMoveEntry(Guid.NewGuid())
        {
            Name = "baz",
            Command = "baz", // without leading slash; EffectiveCommand normalizes it to "/baz"
            Map = this.lorencia,
            X = 140,
            Y = 140,
            Cost = 0,
            MinLevel = 0,
        });
        var updatedRow = new PlugInConfiguration { TypeId = CustomMovePlugInId, IsActive = true };
        updatedRow.SetConfiguration(updatedConfiguration, this.referenceHandler);
        manager.ConfigurePlugIn(CustomMovePlugInId, updatedRow);

        await processor.ProcessMessageAsync(player, ("/baz", "Tester")).ConfigureAwait(false);
        Assert.That(this.viewContainer.MessagePlugIn.Messages, Has.Count.EqualTo(1));
        Assert.That(this.viewContainer.MessagePlugIn.Messages[0].Message, Is.EqualTo(player.GetLocalizedMessage(nameof(PlayerMessage.CustomMoveSuccess), "baz", "Lorencia")));
        Assert.That(player.SelectedCharacter!.PositionX, Is.EqualTo((byte)140));
        Assert.That(player.SelectedCharacter!.PositionY, Is.EqualTo((byte)140));

        // The previously configured move still works after the update.
        this.viewContainer.MessagePlugIn.Messages.Clear();
        await processor.ProcessMessageAsync(player, ("/bar", "Tester")).ConfigureAwait(false);
        Assert.That(this.viewContainer.MessagePlugIn.Messages, Has.Count.EqualTo(1));
        Assert.That(this.viewContainer.MessagePlugIn.Messages[0].Message, Is.EqualTo(player.GetLocalizedMessage(nameof(PlayerMessage.CustomMoveSuccess), "bar", "Lorencia")));
    }

    private CustomMoveConfiguration CreateBarConfiguration()
    {
        return new CustomMoveConfiguration
        {
            Enabled = true,
            Moves = new List<CustomMoveEntry>
            {
                new(Guid.NewGuid())
                {
                    Name = "bar",
                    Command = "/bar",
                    Map = this.lorencia,
                    X = 124,
                    Y = 131,
                    Cost = 0,
                    MinLevel = 0,
                },
            },
        };
    }

    private PlugInConfiguration CreateConfigurationRow(bool isActive)
    {
        var configuration = new PlugInConfiguration
        {
            TypeId = CustomMovePlugInId,
            IsActive = isActive,
        };
        configuration.SetConfiguration(this.CreateBarConfiguration(), this.referenceHandler);
        return configuration;
    }

    private PlugInManager CreateManager(PlugInConfiguration configurationRow)
    {
        // The same construction path as at the server startup (Program.PlugInConfigurationsFactory):
        // the manager discovers all plugins of the loaded assemblies and then applies
        // the persisted plug in configurations.
        return new PlugInManager(new List<PlugInConfiguration> { configurationRow }, new NullLoggerFactory(), null, this.referenceHandler);
    }

    private async Task<Player> CreatePlayerOnManagerAsync(PlugInManager plugInManager)
    {
        var gameConfig = new Mock<GameConfiguration>();
        gameConfig.SetupAllProperties();
        gameConfig.Setup(c => c.Maps).Returns(new List<GameMapDefinition>());
        gameConfig.Setup(c => c.Items).Returns(new List<ItemDefinition>());
        gameConfig.Setup(c => c.Skills).Returns(new List<Skill>());
        gameConfig.Setup(c => c.PlugInConfigurations).Returns(new List<PlugInConfiguration>());
        gameConfig.Setup(c => c.CharacterClasses).Returns(new List<CharacterClass>());
        gameConfig.Setup(c => c.GlobalAttributeCombinations).Returns(new List<AttributeRelationship>());
        gameConfig.Setup(c => c.GlobalBaseAttributeValues).Returns(new List<ConstValueAttribute>
        {
            new(1, Stats.MoneyAmountRate),
        });
        var map = new Mock<GameMapDefinition>();
        map.SetupAllProperties();
        map.Setup(m => m.DropItemGroups).Returns(new List<DropItemGroup>());
        map.Setup(m => m.MonsterSpawns).Returns(new List<MonsterSpawnArea>());
        map.Object.TerrainData = new byte[ushort.MaxValue + 3];
        gameConfig.Object.Maps.Add(map.Object);

        var mapInitializer = new MapInitializer(gameConfig.Object, new NullLogger<MapInitializer>(), NullDropGenerator.Instance, null);
        var gameContext = new GameContext(gameConfig.Object, new InMemoryPersistenceContextProvider(), mapInitializer, new NullLoggerFactory(), plugInManager, NullDropGenerator.Instance, new ConfigurationChangeMediator());
        mapInitializer.PlugInManager = gameContext.PlugInManager;
        mapInitializer.PathFinderPool = gameContext.PathFinderPool;

        return await PlayerTestHelper.CreatePlayerAsync(gameContext, () => this.viewContainer).ConfigureAwait(false);
    }

    /// <summary>
    /// A minimal <see cref="IDataSource{TOwner}"/> which resolves objects by their id
    /// from an in-memory dictionary, as the real data source of the admin panel does.
    /// </summary>
    private sealed class FakeDataSource : IDataSource<GameConfiguration>
    {
        private readonly Dictionary<Guid, MUnique.OpenMU.Persistence.IIdentifiable> objects = new();

        /// <summary>
        /// Adds an object to the data source, so that references to it can be resolved.
        /// </summary>
        /// <param name="identifiable">The object.</param>
        public void Add(MUnique.OpenMU.Persistence.IIdentifiable identifiable) => this.objects.Add(identifiable.Id, identifiable);

        /// <inheritdoc />
        public MUnique.OpenMU.Persistence.IIdentifiable? Get(Guid id) => this.objects.TryGetValue(id, out var result) ? result : null;

        /// <inheritdoc />
        public ValueTask<IContext> GetContextAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <inheritdoc />
        public ValueTask<GameConfiguration> GetOwnerAsync(Guid ownerId = default, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        /// <inheritdoc />
        ValueTask<object> IDataSource.GetOwnerAsync(Guid ownerId, CancellationToken cancellationToken) => throw new NotSupportedException();

        /// <inheritdoc />
        public bool IsSupporting(Type type) => true;

        /// <inheritdoc />
        public ValueTask DiscardChangesAsync() => throw new NotSupportedException();

        /// <inheritdoc />
        public ValueTask ForceDiscardChangesAsync() => throw new NotSupportedException();

        /// <inheritdoc />
        public IEnumerable<T> GetAll<T>() => throw new NotSupportedException();

        /// <inheritdoc />
        public IEnumerable GetAll(Type type) => throw new NotSupportedException();

        /// <inheritdoc />
        public void Dispose()
        {
            // Nothing to dispose.
        }
    }
}
