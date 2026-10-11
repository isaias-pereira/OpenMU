// <copyright file="UnsafeContextRetirementTests.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests.CashShop;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.CashShop;
using MUnique.OpenMU.GameLogic.PlayerActions.CashShop;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.GameServer;
using MUnique.OpenMU.GameServer.RemoteView;
using MUnique.OpenMU.Network.PlugIns;
using MUnique.OpenMU.Interfaces;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.Persistence.InMemory;
using MUnique.OpenMU.Pathfinding;
using MUnique.OpenMU.PlugIns;
using NUnit.Framework;

/// <summary>
/// Regressao da retirada de sessao com contexto possivelmente sujo.
/// </summary>
/// <remarks>
/// <para>
/// Estes testes usam um <see cref="GameContext"/> de verdade: o jogador e registrado com
/// <see cref="IGameContext.AddPlayerAsync"/>, que e o que assina
/// <c>GameContext.RemovePlayerAsync</c> no evento <c>PlayerDisconnected</c>. Por isso a prova de
/// que a sessao saiu do jogo e a <b>ausencia</b> do jogador nas listas, no dicionario de nome e no
/// mapa, e nao o estado <c>Finished</c> nem <c>IsConnected == false</c>, que apenas refletem a
/// maquina de estados.
/// </para>
/// <para>
/// A conexao e verificada por um <see cref="IConnection"/> falso usado por um
/// <see cref="RemotePlayer"/> real: o encerramento e provado por <c>Connected == false</c> e pelo
/// numero de chamadas a <c>DisconnectAsync</c>, e nao pelo estado do jogador.
/// </para>
/// <para>
/// O contexto in-memory e envolvido por <see cref="TrackingContext"/>, que registra descarte e
/// salvamento. A recusa de save apos descarte e simulada pelo wrapper; estes testes nao comprovam
/// esse comportamento no Entity Framework nem no PostgreSQL.
/// </para>
/// </remarks>
[TestFixture]
public class UnsafeContextRetirementTests
{
    private InMemoryPersistenceContextProvider _contextProvider = null!;
    private GameConfiguration _configuration = null!;
    private UnsafeContextRetirement _retirement = null!;

    [SetUp]
    public void SetUp()
    {
        this._contextProvider = new InMemoryPersistenceContextProvider();
        this._configuration = CreateConfiguration();
        this._retirement = new UnsafeContextRetirement(NullLogger<UnsafeContextRetirement>.Instance);
    }

    /// <summary>
    /// Uma retirada bem-sucedida remove o jogador das listas, do dicionario e do mapa, fecha a
    /// conexao e torna o contexto incapaz de salvar.
    /// </summary>
    /// <remarks>
    /// Teste unitario: nao valida transacao nem constraint de PostgreSQL.
    /// </remarks>
    [Test]
    public async Task RetireAsync_RegisteredPlayer_RemovesFromGameAndClosesConnection()
    {
        var provider = new TrackingContextProvider(this._contextProvider);
        var gameContext = CreateGameServerContext(provider, this._configuration);
        var connection = new FakeConnection { Connected = true };
        var (player, map) = await CreateRemotePlayerAsync(gameContext, connection).ConfigureAwait(false);
        var context = (TrackingContext)player.PersistenceContext;
        var originalId = player.Id;
        var originalName = player.Name;
        context.BeginTracking();

        // Preconditions: the player really is registered, so the assertions below are meaningful.
        var before = await gameContext.GetPlayersAsync().ConfigureAwait(false);
        Assert.Multiple(() =>
        {
            Assert.That(before, Does.Contain(player), "precondition: player list");
            Assert.That(gameContext.GetPlayerByCharacterName(originalName), Is.SameAs(player), "precondition: name dictionary");
            Assert.That(player.CurrentMap, Is.SameAs(map), "precondition: map");
            Assert.That(map.GetObject(originalId), Is.SameAs(player), "precondition: map object under original id");
            Assert.That(connection.Connected, Is.True, "precondition: connection open");
        });

        var retired = await this._retirement.RetireAsync(player, "regression").ConfigureAwait(false);

        var remaining = await gameContext.GetPlayersAsync().ConfigureAwait(false);
        Assert.Multiple(() =>
        {
            Assert.That(retired, Is.True, "The retirement must report a completed removal.");
            Assert.That(remaining, Does.Not.Contain(player), "The player must be removed from the player list.");
            Assert.That(gameContext.GetPlayerByCharacterName(originalName), Is.Null, "The player must be removed from the character name dictionary.");
            Assert.That(player.CurrentMap, Is.Null, "The player must be removed from the map.");
            Assert.That(map.GetObject(originalId), Is.Null, "The player must be removed from the map objects under its original id.");
            Assert.That(connection.DisconnectCallCount, Is.EqualTo(1), "The connection must really be closed.");
            Assert.That(connection.Connected, Is.False, "The connection must report itself as closed.");
            Assert.That(context.IsDisposed, Is.True, "The dirty context must have been disposed.");
        });

        // The containment: the dirty context cannot be saved anymore, and nothing was saved before.
        await VerifyContextCannotBeSaved(context, "After the retirement the context must not be savable.");
        Assert.That(context.SaveCallCount, Is.Zero, "The retirement must not have saved the context.");
    }

    /// <summary>
    /// A retirada nao pode salvar o contexto em momento algum, nem durante a limpeza do mundo.
    /// </summary>
    /// <remarks>
    /// Teste unitario; nao valida transacao PostgreSQL.
    /// </remarks>
    [Test]
    public async Task RetireAsync_NeverSavesTheDirtyContext()
    {
        var provider = new TrackingContextProvider(this._contextProvider);
        var gameContext = CreateGameContext(provider, this._configuration);
        var player = await CreatePlayerAsync(gameContext).ConfigureAwait(false);
        await gameContext.AddPlayerAsync(player).ConfigureAwait(false);
        provider.PlayerContext!.BeginTracking();

        await this._retirement.RetireAsync(player, "regression").ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(provider.PlayerContext.SaveCallCount, Is.Zero,
                "The retirement must not call SaveChangesAsync at all.");
            Assert.That(provider.PlayerContext.IsDisposed, Is.True, "The retirement must dispose the context.");
        });
    }

    /// <summary>
    /// Uma transicao recusada nao pode ser tratada como retirada concluida, mas o contexto sujo
    /// ainda assim tem de ser invalidado.
    /// </summary>
    /// <remarks>
    /// Teste unitario; nao valida transacao PostgreSQL.
    /// </remarks>
    [Test]
    public async Task RetireAsync_RefusedTransition_ReportsFailureButStillInvalidatesContext()
    {
        var provider = new TrackingContextProvider(this._contextProvider);
        var gameContext = CreateGameContext(provider, this._configuration);
        var player = await CreatePlayerAsync(gameContext).ConfigureAwait(false);
        var context = provider.PlayerContext!;

        // Finished is the terminal state: it has no outgoing transitions, so the state machine
        // refuses any further advance, which is exactly a refused retirement.
        Assert.That(
            await player.PlayerState.TryAdvanceToAsync(PlayerState.Disconnected).ConfigureAwait(false),
            Is.True,
            "precondition: the player must reach Disconnected");
        Assert.That(
            await player.PlayerState.TryAdvanceToAsync(PlayerState.Finished).ConfigureAwait(false),
            Is.True,
            "precondition: the player must reach the terminal state");
        var contextBefore = player.PersistenceContext;
        context.BeginTracking();

        var retired = await this._retirement.RetireAsync(player, "regression").ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(retired, Is.False, "A refused transition must not be reported as a completed retirement.");
            Assert.That(player.PersistenceContext, Is.SameAs(contextBefore), "The same context must be the one being invalidated.");
            Assert.That(context.IsDisposed, Is.True, "A refused transition must still dispose the context.");
        });

        // Even on refusal the dirty state must not be savable.
        await VerifyContextCannotBeSaved(context, "A refused transition must still invalidate the context.");
    }

    /// <summary>
    /// Uma excecao durante a limpeza nao pode ser engolida: a retirada e reportada como falha,
    /// com o contexto contido.
    /// </summary>
    /// <remarks>
    /// Teste unitario; nao valida transacao PostgreSQL.
    /// </remarks>
    [Test]
    public void RetireAsync_ExceptionDuringCleanup_ReportsFailure()
    {
        var gameContext = CreateGameContext(this._contextProvider, this._configuration);
        var player = new ThrowingCleanupPlayer(gameContext);
        player.InitializeAsync();

        var retired = this._retirement.RetireAsync(player, "regression").GetAwaiter().GetResult();

        Assert.Multiple(() =>
        {
            Assert.That(retired, Is.False, "An exception during the cleanup must not be reported as a completed retirement.");
            Assert.That(player.InvalidateCallCount, Is.EqualTo(1), "The dirty context must still be invalidated after a cleanup failure.");
        });
    }

    [Test]
    public async Task RetireAsync_RemoteCleanupThrows_ClosesConnectionAndRunsDisconnectedHandlers()
    {
        var provider = new TrackingContextProvider(this._contextProvider);
        var gameContext = CreateGameServerContext(provider, this._configuration);
        var connection = new FakeConnection { Connected = true };
        var (player, map) = await CreateRemotePlayerAsync(gameContext, connection).ConfigureAwait(false);
        var context = (TrackingContext)player.PersistenceContext;
        var name = player.Name;
        var originalId = player.Id;
        Assert.Multiple(() =>
        {
            Assert.That(gameContext.GetPlayerByCharacterName(name), Is.SameAs(player), "precondition: name dictionary");
            Assert.That(map.GetObject(originalId), Is.SameAs(player), "precondition: map object under original id");
        });
        var handlerCalls = 0;
        var cleanupCalls = 0;
        player.PlayerLeftWorld += _ =>
        {
            cleanupCalls++;
            throw new InvalidOperationException("Simulated world cleanup failure.");
        };
        player.PlayerDisconnected += _ =>
        {
            handlerCalls++;
            return default;
        };
        context.BeginTracking();

        var retired = await this._retirement.RetireAsync(player, "regression").ConfigureAwait(false);
        var remaining = await gameContext.GetPlayersAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(cleanupCalls, Is.EqualTo(1), "The real world cleanup must reach the injected failure.");
            Assert.That(retired, Is.False);
            Assert.That(connection.DisconnectCallCount, Is.EqualTo(1));
            Assert.That(connection.Connected, Is.False);
            Assert.That(handlerCalls, Is.EqualTo(1), "Cleanup failure must not skip presence/disposal handlers.");
            Assert.That(remaining, Does.Not.Contain(player));
            Assert.That(gameContext.GetPlayerByCharacterName(name), Is.Null);
            Assert.That(map.GetObject(originalId), Is.Null);
            Assert.That(context.IsDisposed, Is.True);
            Assert.That(context.SaveCallCount, Is.Zero);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RetireAsync_AsynchronousHandler_AwaitsCompletionAndObservesFailure(bool throwAfterAwait)
    {
        var provider = new TrackingContextProvider(this._contextProvider);
        var gameContext = CreateGameContext(provider, this._configuration);
        var player = await CreatePlayerAsync(gameContext).ConfigureAwait(false);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = false;
        var lastHandlerCalls = 0;
        player.PlayerDisconnected += async _ =>
        {
            entered.TrySetResult(true);
            await release.Task.ConfigureAwait(false);
            completed = true;
            if (throwAfterAwait)
            {
                throw new InvalidOperationException("Simulated asynchronous handler failure.");
            }
        };
        await gameContext.AddPlayerAsync(player).ConfigureAwait(false);
        player.PlayerDisconnected += _ =>
        {
            lastHandlerCalls++;
            return default;
        };
        provider.PlayerContext!.BeginTracking();

        var retirement = this._retirement.RetireAsync(player, "regression").AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(retirement.IsCompleted, Is.False, "Retirement must await earlier multicast handlers as well as the last one.");
        }
        finally
        {
            release.TrySetResult(true);
            await retirement.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }

        var remaining = await gameContext.GetPlayersAsync().ConfigureAwait(false);
        Assert.Multiple(() =>
        {
            Assert.That(retirement.Result, Is.EqualTo(!throwAfterAwait));
            Assert.That(completed, Is.True);
            Assert.That(lastHandlerCalls, Is.EqualTo(1));
            Assert.That(remaining, Does.Not.Contain(player), "A failing handler must not prevent later GameContext removal.");
            Assert.That(provider.PlayerContext.IsDisposed, Is.True);
            Assert.That(provider.PlayerContext.SaveCallCount, Is.Zero);
        });
    }

    [Test]
    public async Task RetireAsync_SynchronousHandlerThrows_StillRemovesPlayerAndInvokesLaterHandler()
    {
        var provider = new TrackingContextProvider(this._contextProvider);
        var gameContext = CreateGameContext(provider, this._configuration);
        var player = await CreatePlayerAsync(gameContext).ConfigureAwait(false);
        var lastHandlerCalls = 0;
        player.PlayerDisconnected += _ => throw new InvalidOperationException("Simulated synchronous handler failure.");
        await gameContext.AddPlayerAsync(player).ConfigureAwait(false);
        player.PlayerDisconnected += _ =>
        {
            lastHandlerCalls++;
            return default;
        };
        provider.PlayerContext!.BeginTracking();

        var retired = await this._retirement.RetireAsync(player, "regression").ConfigureAwait(false);
        var remaining = await gameContext.GetPlayersAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(retired, Is.False);
            Assert.That(lastHandlerCalls, Is.EqualTo(1));
            Assert.That(remaining, Does.Not.Contain(player));
            Assert.That(provider.PlayerContext.IsDisposed, Is.True);
            Assert.That(provider.PlayerContext.SaveCallCount, Is.Zero);
        });
    }

    [Test]
    public async Task RetireAsync_ContextDisposalThrows_ReportsFailureAndStillClosesConnection()
    {
        var provider = new TrackingContextProvider(this._contextProvider);
        var gameContext = CreateGameServerContext(provider, this._configuration);
        var connection = new FakeConnection { Connected = true };
        var (player, _) = await CreateRemotePlayerAsync(gameContext, connection).ConfigureAwait(false);
        var context = (TrackingContext)player.PersistenceContext;
        context.BeginTracking();
        context.ThrowOnDispose = true;

        var retired = await this._retirement.RetireAsync(player, "regression").ConfigureAwait(false);
        var remaining = await gameContext.GetPlayersAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(retired, Is.False, "Failed containment must never be reported as successful retirement.");
            Assert.That(context.DisposeCallCount, Is.GreaterThanOrEqualTo(1));
            Assert.That(context.IsDisposed, Is.False);
            Assert.That(context.SaveCallCount, Is.Zero);
            Assert.That(connection.DisconnectCallCount, Is.EqualTo(1));
            Assert.That(connection.Connected, Is.False);
            Assert.That(remaining, Does.Not.Contain(player));
        });
    }

    [Test]
    public async Task RetireAsync_FinalInvalidationThrows_OverridesOtherwiseSuccessfulRetirement()
    {
        var gameContext = CreateGameContext(this._contextProvider, this._configuration);
        var player = new ThrowingInvalidationPlayer(gameContext);
        Assert.That(await player.PlayerState.TryAdvanceToAsync(PlayerState.LoginScreen).ConfigureAwait(false), Is.True);
        await gameContext.AddPlayerAsync(player).ConfigureAwait(false);

        var retired = await this._retirement.RetireAsync(player, "regression").ConfigureAwait(false);
        var remaining = await gameContext.GetPlayersAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(remaining, Does.Not.Contain(player), "The disconnect pipeline completed before final containment failed.");
            Assert.That(player.InvalidateCallCount, Is.EqualTo(1));
            Assert.That(retired, Is.False, "A successful disconnect cannot hide the final invalidation failure.");
        });
    }

    [Test]
    public async Task DisconnectAsync_AsynchronousHandler_AwaitsAllHandlersAndSavesProgress()
    {
        var provider = new TrackingContextProvider(this._contextProvider);
        var gameContext = CreateGameContext(provider, this._configuration);
        var player = await CreatePlayerAsync(gameContext).ConfigureAwait(false);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstHandlerCompleted = false;
        var lastHandlerCalls = 0;
        player.PlayerDisconnected += async _ =>
        {
            entered.TrySetResult(true);
            await release.Task.ConfigureAwait(false);
            firstHandlerCompleted = true;
        };
        await gameContext.AddPlayerAsync(player).ConfigureAwait(false);
        player.PlayerDisconnected += _ =>
        {
            lastHandlerCalls++;
            return default;
        };
        provider.PlayerContext!.BeginTracking();

        var disconnect = player.DisconnectAsync().AsTask();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(disconnect.IsCompleted, Is.False, "Normal disconnect must also await earlier multicast handlers.");
        }
        finally
        {
            release.TrySetResult(true);
            await disconnect.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        }

        var remaining = await gameContext.GetPlayersAsync().ConfigureAwait(false);
        Assert.Multiple(() =>
        {
            Assert.That(firstHandlerCompleted, Is.True);
            Assert.That(lastHandlerCalls, Is.EqualTo(1));
            Assert.That(remaining, Does.Not.Contain(player));
            Assert.That(provider.PlayerContext.SaveCallCount, Is.EqualTo(1), "Ordinary disconnect must retain its save behavior.");
        });
    }

    [Test]
    public async Task RetireAsync_ConnectionDisconnectThrows_DisposesConnectionAndRunsRemainingHandlers()
    {
        var provider = new TrackingContextProvider(this._contextProvider);
        var gameContext = CreateGameServerContext(provider, this._configuration);
        var connection = new FakeConnection { Connected = true, ThrowOnDisconnect = true };
        var (player, _) = await CreateRemotePlayerAsync(gameContext, connection).ConfigureAwait(false);
        var context = (TrackingContext)player.PersistenceContext;
        var handlerCalls = 0;
        player.PlayerDisconnected += _ =>
        {
            handlerCalls++;
            return default;
        };
        context.BeginTracking();

        var retired = await this._retirement.RetireAsync(player, "regression").ConfigureAwait(false);
        var remaining = await gameContext.GetPlayersAsync().ConfigureAwait(false);

        Assert.Multiple(() =>
        {
            Assert.That(retired, Is.False, "Connection failure cannot be reported as a successful retirement.");
            Assert.That(connection.DisconnectCallCount, Is.EqualTo(1));
            Assert.That(connection.DisposeCallCount, Is.EqualTo(1), "Dispose must still be attempted after DisconnectAsync throws.");
            Assert.That(handlerCalls, Is.EqualTo(1));
            Assert.That(remaining, Does.Not.Contain(player));
            Assert.That(context.IsDisposed, Is.True);
            Assert.That(context.SaveCallCount, Is.Zero);
        });
    }

    private static GameConfiguration CreateConfiguration()
    {
        var configurationMock = new Mock<GameConfiguration>();
        configurationMock.SetupAllProperties();
        configurationMock.Setup(c => c.Items).Returns(new List<ItemDefinition>());
        configurationMock.Setup(c => c.Maps).Returns(new List<GameMapDefinition>());
        configurationMock.Setup(c => c.Skills).Returns(new List<Skill>());
        configurationMock.Setup(c => c.PlugInConfigurations).Returns(new List<PlugInConfiguration>());
        configurationMock.Setup(c => c.CharacterClasses).Returns(new List<CharacterClass>());
        configurationMock.Setup(c => c.GlobalAttributeCombinations).Returns(new List<AttributeRelationship>());
        configurationMock.Setup(c => c.GlobalBaseAttributeValues).Returns(new List<ConstValueAttribute>
        {
            new(1, Stats.MoneyAmountRate),
        });
        var map = new Mock<GameMapDefinition>();
        map.SetupAllProperties();
        map.Setup(m => m.DropItemGroups).Returns(new List<DropItemGroup>());
        map.Setup(m => m.MonsterSpawns).Returns(new List<MonsterSpawnArea>());
        map.Object.TerrainData = new byte[ushort.MaxValue + 3];
        configurationMock.Object.Maps.Add(map.Object);
        configurationMock.Object.RecoveryInterval = int.MaxValue;
        return configurationMock.Object;
    }

    private static IGameContext CreateGameContext(IPersistenceContextProvider provider, GameConfiguration configuration)
    {
        var mapInitializer = new MapInitializer(configuration, new NullLogger<MapInitializer>(), NullDropGenerator.Instance, null);
        var context = new GameContext(
            configuration,
            provider,
            mapInitializer,
            new NullLoggerFactory(),
            CreatePlugInManager(),
            NullDropGenerator.Instance,
            new ConfigurationChangeMediator());
        mapInitializer.PlugInManager = context.PlugInManager;
        mapInitializer.PathFinderPool = context.PathFinderPool;
        return context;
    }

    /// <summary>
    /// Creates a real <see cref="GameServerContext"/>, so the player is registered in the very
    /// same player list, name dictionary and maps that the assertions inspect.
    /// </summary>
    private static GameServerContext CreateGameServerContext(IPersistenceContextProvider provider, GameConfiguration configuration)
    {
        var mapInitializer = new MapInitializer(configuration, new NullLogger<MapInitializer>(), NullDropGenerator.Instance, null);
        var serverDefinition = new Mock<GameServerDefinition>();
        serverDefinition.SetupAllProperties();
        serverDefinition.Object.GameConfiguration = configuration;
        serverDefinition.Object.ServerConfiguration = new GameServerConfiguration();
        var context = new GameServerContext(
            serverDefinition.Object,
            new Mock<IGuildServer>().Object,
            new Mock<IEventPublisher>().Object,
            new Mock<ILoginServer>().Object,
            new Mock<IFriendServer>().Object,
            provider,
            mapInitializer,
            new NullLoggerFactory(),
            CreatePlugInManager(),
            NullDropGenerator.Instance,
            new ConfigurationChangeMediator());
        mapInitializer.PlugInManager = context.PlugInManager;
        mapInitializer.PathFinderPool = context.PathFinderPool;
        return context;
    }

    /// <summary>
    /// Creates a plug-in manager with the plug-ins discovered and the cash shop services the
    /// message handler plug-ins depend on, so the real <see cref="RemotePlayer"/> can be built and
    /// the view plug-in container can resolve plug-ins such as the respawn one.
    /// </summary>
    private static PlugInManager CreatePlugInManager()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(new NullLoggerFactory());

        // NullLoggerFactory alone does not provide the ILogger / ILogger<T> instances the services
        // take as constructor arguments, so those are mapped to null loggers.
        services.AddSingleton<ILogger>(NullLogger.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddSingleton<IPersistenceContextProvider>(new InMemoryPersistenceContextProvider());

        // The real catalog service is not the subject of this test and drags in a memory cache, so
        // it is replaced by a mock. What matters here is only that the plug-in graph can be built.
        services.AddSingleton<ICashShopCatalogService>(_ => new Mock<ICashShopCatalogService>().Object);
        services.AddSingleton<ICashShopStorageService>(provider => new CashShopStorageService(
            () => provider.GetRequiredService<IPersistenceContextProvider>().CreateNewContext()));
        services.AddSingleton<UnsafeContextRetirement>();
        services.AddSingleton<ICashShopSelectionResolver, CashShopSelectionResolver>();
        services.AddSingleton<ICashShopBuyService, CashShopBuyService>();
        services.AddSingleton<ICashShopGiftService, CashShopGiftService>();
        services.AddSingleton<ICashShopStorageClaimService>(provider => new CashShopStorageClaimService(
            NullLogger<CashShopStorageClaimService>.Instance,
            provider.GetRequiredService<UnsafeContextRetirement>(),
            () => provider.GetRequiredService<IPersistenceContextProvider>().CreateNewContext()));
        services.AddSingleton(sp => (CashShopStorageClaimService)sp.GetRequiredService<ICashShopStorageClaimService>());
        services.AddSingleton<CashShopBuyAction>();
        services.AddSingleton<CashShopGiftAction>();
        services.AddSingleton<CashShopStorageListAction>();
        services.AddSingleton<CashShopEventItemListAction>();
        var serviceProvider = services.BuildServiceProvider();

        var manager = new PlugInManager(null, new NullLoggerFactory(), serviceProvider, null);
        manager.DiscoverAndRegisterPlugIns();
        return manager;
    }

    /// <summary>
    /// Asserts that the given context can no longer persist anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This deliberately does not assume a specific exception type. A real
    /// <c>EntityFrameworkContextBase</c> throws <see cref="ObjectDisposedException"/> once disposed,
    /// but <c>InMemoryContext.Dispose</c> only drops its event and keeps working, so asserting
    /// <c>ObjectDisposedException</c> here would be asserting an in-memory detail rather than the
    /// behaviour under test. What matters is that the save does not succeed.
    /// </para>
    /// <para>
    /// This helper is used with <see cref="TrackingContext"/>. Its refusal to save after disposal
    /// is simulated; the production Entity Framework context requires separate integration coverage.
    /// </para>
    /// </remarks>
    private static async ValueTask VerifyContextCannotBeSaved(IContext context, string message)
    {
        try
        {
            var saved = await context.SaveChangesAsync().ConfigureAwait(false);
            Assert.Fail($"{message} The save reported success ({saved}).");
        }
        catch (AssertionException)
        {
            throw;
        }
        catch (Exception)
        {
            // Expected: the disposed context refused the save.
        }
    }

    /// <summary>
    /// Creates a player through the shared helper, which sets up a valid character class.
    /// </summary>
    private static ValueTask<Player> CreatePlayerAsync(IGameContext gameContext) =>
        PlayerTestHelper.CreatePlayerAsync(gameContext);

    /// <summary>
    /// Creates a player through the shared helper, which sets up a valid character class, and
    /// returns it together with the map it was placed on.
    /// </summary>
    private static async ValueTask<(RemotePlayer Player, GameMap Map)> CreateRemotePlayerAsync(IGameServerContext gameContext, IConnection connection)
    {
        // 106 is the minimum client version of the respawn plug-in that actually assigns
        // CurrentMap; below it the player would stay map-less and the map assertions would be
        // meaningless. The version is set before the plug-in container is created, because the
        // container decides which plug-ins are suitable for the client version.
        var player = new RemotePlayer(gameContext, connection, new ClientVersion(106, 0, ClientLanguage.English));
        var reference = await PlayerTestHelper.CreatePlayerAsync(gameContext).ConfigureAwait(false);
        player.Account = reference.Account;
        await player.PlayerState.TryAdvanceToAsync(PlayerState.LoginScreen).ConfigureAwait(false);
        await player.PlayerState.TryAdvanceToAsync(PlayerState.Authenticated).ConfigureAwait(false);
        await player.PlayerState.TryAdvanceToAsync(PlayerState.CharacterSelection).ConfigureAwait(false);

        // Registration has to happen before the character is selected: AddPlayerAsync is what
        // subscribes GameContext.PlayerEnteredWorldAsync, which is the handler that puts the player
        // into the character name dictionary. Registering afterwards would leave the dictionary
        // empty and the removal assertion meaningless.
        await gameContext.AddPlayerAsync(player).ConfigureAwait(false);
        await player.SetSelectedCharacterAsync(reference.SelectedCharacter!).ConfigureAwait(false);

        var map = (await gameContext.GetMapAsync(0).ConfigureAwait(false))!;

        // CurrentMap is only assigned once the client acknowledged the map change, so the warp
        // alone leaves the player map-less. RespawnAtAsync is the path that actually places it,
        // which is what makes the map assertions meaningful.
        player.IsAlive = true;
        await player.RespawnAtAsync(new ExitGate
        {
            Map = map.Definition,
            X1 = 128,
            X2 = 128,
            Y1 = 128,
            Y2 = 128,
            Direction = Direction.South,
        }).ConfigureAwait(false);
        return (player, map);
    }

    /// <summary>
    /// Uma conexao cujo encerramento e observavel, ao contrario de <c>IsConnected</c>, que deriva do
    /// estado do jogador e passaria mesmo com o socket aberto.
    /// </summary>
    private sealed class FakeConnection : IConnection
    {
        public event AsyncEventHandler<System.Buffers.ReadOnlySequence<byte>>? PacketReceived;

        public event AsyncEventHandler? Disconnected;

        public bool Connected { get; set; }

        public int DisconnectCallCount { get; private set; }

        public bool ThrowOnDisconnect { get; set; }

        public int DisposeCallCount { get; private set; }

        public System.Net.EndPoint? EndPoint => null;

        public System.Net.EndPoint? LocalEndPoint => null;

        // The respawn plug-in writes a packet through the connection, so a real pipe writer is
        // needed here instead of a throwing stub.
        public System.IO.Pipelines.PipeWriter Output { get; } = System.IO.Pipelines.PipeWriter.Create(new System.IO.MemoryStream());

        public Nito.AsyncEx.AsyncLock OutputLock => new();

        public Task BeginReceiveAsync() => Task.CompletedTask;

        public ValueTask DisconnectAsync()
        {
            this.DisconnectCallCount++;
            if (this.ThrowOnDisconnect)
            {
                throw new InvalidOperationException("Simulated connection disconnect failure.");
            }

            this.Connected = false;
            return default;
        }

        public void Dispose()
        {
            this.DisposeCallCount++;
            this.PacketReceived = null;
            this.Disconnected = null;
        }
    }

    /// <summary>
    /// Um jogador cuja limpeza interna lanca excecao, representando uma falha ao remover o jogador
    /// do mundo.
    /// </summary>
    private sealed class ThrowingCleanupPlayer : Player
    {
        public ThrowingCleanupPlayer(IGameContext gameContext)
            : base(gameContext)
        {
        }

        public int InvalidateCallCount { get; private set; }

        public void InitializeAsync() => this.PlayerState.TryAdvanceToAsync(GameLogic.PlayerState.LoginScreen).AsTask().GetAwaiter().GetResult();

        protected override ICustomPlugInContainer<IViewPlugIn> CreateViewPlugInContainer() => new MockViewPlugInContainer();

        protected override ValueTask InternalDisconnectWithoutSavingAsync() =>
            throw new InvalidOperationException("Simulated cleanup failure.");

        public override void InvalidatePersistenceContext()
        {
            this.InvalidateCallCount++;
        }
    }

    private sealed class TestPlayer : Player
    {
        public TestPlayer(IGameContext gameContext)
            : base(gameContext)
        {
        }

        protected override ICustomPlugInContainer<IViewPlugIn> CreateViewPlugInContainer() => new MockViewPlugInContainer();
    }

    private sealed class ThrowingInvalidationPlayer : Player
    {
        public ThrowingInvalidationPlayer(IGameContext gameContext)
            : base(gameContext)
        {
        }

        public int InvalidateCallCount { get; private set; }

        protected override ICustomPlugInContainer<IViewPlugIn> CreateViewPlugInContainer() => new MockViewPlugInContainer();

        // Isolate the final invalidation performed by the retirement service after a successful
        // disconnect. A derived player's hook is not required to call the base implementation.
        protected override ValueTask InternalDisconnectWithoutSavingAsync() => default;

        public override void InvalidatePersistenceContext()
        {
            this.InvalidateCallCount++;
            throw new InvalidOperationException("Simulated final containment failure.");
        }
    }

    /// <summary>
    /// Entrega ao jogador um <see cref="TrackingContext"/> que conta salvamentos e registra o
    /// descarte, para provar que a retirada nao salvou e invalidou o contexto.
    /// </summary>
    private sealed class TrackingContextProvider : IPersistenceContextProvider
    {
        private readonly IPersistenceContextProvider _inner;

        public TrackingContextProvider(IPersistenceContextProvider inner)
        {
            this._inner = inner;
        }

        /// <summary>
        /// Gets the wrapped context that was handed to the player.
        /// </summary>
        public TrackingContext? PlayerContext { get; private set; }

        public IRepositoryProvider RepositoryProvider => this._inner.RepositoryProvider;

        public IContext CreateNewContext() => this._inner.CreateNewContext();

        public IContext CreateNewContext(GameConfiguration gameConfiguration) => this._inner.CreateNewContext(gameConfiguration);

        public IConfigurationContext CreateNewConfigurationContext() => this._inner.CreateNewConfigurationContext();

        public IContext CreateNewTradeContext() => this._inner.CreateNewTradeContext();

        public IPlayerContext CreateNewPlayerContext(GameConfiguration gameConfiguration)
        {
            var wrapped = new TrackingContext(this._inner.CreateNewPlayerContext(gameConfiguration));
            this.PlayerContext = wrapped;
            return wrapped;
        }

        public IFriendServerContext CreateNewFriendServerContext() => this._inner.CreateNewFriendServerContext();

        public IGuildServerContext CreateNewGuildContext() => this._inner.CreateNewGuildContext();

        public IContext CreateNewTypedContext(Type editType, bool useCache, GameConfiguration? gameConfiguration = null) =>
            this._inner.CreateNewTypedContext(editType, useCache, gameConfiguration);
    }

    /// <summary>
    /// Um <see cref="IContext"/> que delega ao contexto real e conta as chamadas de
    /// <see cref="SaveChangesAsync"/>. Depois de descartado, ele falha como um contexto real.
    /// </summary>
    private sealed class TrackingContext : IPlayerContext
    {
        private readonly IPlayerContext _inner;

        public TrackingContext(IPlayerContext inner)
        {
            this._inner = inner;
        }

        /// <summary>
        /// Gets a value indicating whether the context was disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        public bool ThrowOnDispose { get; set; }

        public int DisposeCallCount { get; private set; }

        /// <summary>
        /// Gets the number of saves counted since <see cref="BeginTracking"/> was called.
        /// </summary>
        public int SaveCallCount { get; private set; }

        public void BeginTracking() => this.SaveCallCount = 0;

        public bool HasChanges => this._inner.HasChanges;

        public IDisposable SuspendChangeNotifications() => this._inner.SuspendChangeNotifications();

        public bool Detach(object item) => this._inner.Detach(item);

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

        public ValueTask<bool> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            this.ThrowIfDisposed();
            this.SaveCallCount++;
            return this._inner.SaveChangesAsync(cancellationToken);
        }

        public void Dispose()
        {
            this.DisposeCallCount++;
            if (this.ThrowOnDispose)
            {
                throw new InvalidOperationException("Simulated context disposal failure.");
            }

            this.IsDisposed = true;
            this._inner.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (this.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(TrackingContext));
            }
        }

        public ValueTask<LetterBody?> GetLetterBodyByHeaderIdAsync(Guid headerId, CancellationToken cancellationToken = default) =>
            this._inner.GetLetterBodyByHeaderIdAsync(headerId, cancellationToken);

        public ValueTask<bool> CanSaveLetterAsync(LetterHeader letterHeader, CancellationToken cancellationToken = default) =>
            this._inner.CanSaveLetterAsync(letterHeader, cancellationToken);

        public ValueTask<AccountState?> AuthenticateAsync(string loginName, string password, CancellationToken cancellationToken = default) =>
            this._inner.AuthenticateAsync(loginName, password, cancellationToken);

        public ValueTask<Account?> GetAccountByLoginNameAsync(string loginName, string password, CancellationToken cancellationToken = default) =>
            this._inner.GetAccountByLoginNameAsync(loginName, password, cancellationToken);

        public ValueTask<Account?> GetAccountByLoginNameAsync(string loginName, CancellationToken cancellationToken = default) =>
            this._inner.GetAccountByLoginNameAsync(loginName, cancellationToken);

        public ValueTask<IEnumerable<Account>> GetAccountsOrderedByLoginNameAsync(int skip, int count, CancellationToken cancellationToken = default) =>
            this._inner.GetAccountsOrderedByLoginNameAsync(skip, count, cancellationToken);

        public ValueTask<Account?> GetAccountByCharacterNameAsync(string characterName, CancellationToken cancellationToken = default) =>
            this._inner.GetAccountByCharacterNameAsync(characterName, cancellationToken);
    }
}
