// <copyright file="CustomMoveChatCommandPlugInTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests.CustomMove;

using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.GameLogic.Properties;
using MUnique.OpenMU.Interfaces;
using NUnit.Framework;

/// <summary>
/// Tests for the <see cref="CustomMoveChatCommandPlugIn"/>.
/// </summary>
[TestFixture]
public class CustomMoveChatCommandPlugInTests
{
    private const string MoveName = "bar";

    private RecordingViewPlugInContainer viewContainer = null!;
    private Player player = null!;
    private CustomMoveChatCommandPlugIn plugIn = null!;

    /// <summary>
    /// Sets up a test player and the plugin under test.
    /// </summary>
    [SetUp]
    public async Task SetUpAsync()
    {
        this.viewContainer = new RecordingViewPlugInContainer();
        this.player = await PlayerTestHelper.CreatePlayerAsync(() => this.viewContainer).ConfigureAwait(false);
        this.plugIn = new CustomMoveChatCommandPlugIn();
    }

    /// <summary>
    /// Tests that a missing configuration shows the system disabled message.
    /// </summary>
    [Test]
    public async Task NullConfigurationShowsSystemDisabled()
    {
        this.plugIn.Configuration = null;

        await this.plugIn.HandleCommandAsync(this.player, "/custommove " + MoveName).ConfigureAwait(false);

        this.AssertSingleMessage(nameof(PlayerMessage.CustomMoveSystemDisabled));
    }

    /// <summary>
    /// Tests that a disabled configuration shows the system disabled message.
    /// </summary>
    [Test]
    public async Task DisabledConfigurationShowsSystemDisabled()
    {
        this.ConfigureMoves(this.CreateMove());
        this.plugIn.Configuration!.Enabled = false;

        await this.plugIn.HandleCommandAsync(this.player, "/custommove " + MoveName).ConfigureAwait(false);

        this.AssertSingleMessage(nameof(PlayerMessage.CustomMoveSystemDisabled));
    }

    /// <summary>
    /// Tests that an unknown move name shows the not found message.
    /// </summary>
    [Test]
    public async Task UnknownMoveShowsNotFound()
    {
        this.ConfigureMoves(this.CreateMove());

        await this.plugIn.HandleCommandAsync(this.player, "/custommove unknown").ConfigureAwait(false);

        this.AssertSingleMessage(nameof(PlayerMessage.CustomMoveNotFound), "unknown");
    }

    /// <summary>
    /// Tests that a move without a configured map shows the map not configured message.
    /// </summary>
    [Test]
    public async Task MoveWithoutMapShowsMapNotConfigured()
    {
        this.ConfigureMoves(this.CreateMove());

        await this.plugIn.HandleCommandAsync(this.player, "/custommove " + MoveName).ConfigureAwait(false);

        this.AssertSingleMessage(nameof(PlayerMessage.CustomMoveMapNotConfigured));
    }

    /// <summary>
    /// Tests that a move with a minimum level requirement shows the level too low message.
    /// </summary>
    [Test]
    public async Task MoveWithLevelRequirementShowsLevelTooLow()
    {
        var move = this.CreateMove();
        move.Map = CreateMap();
        move.MinLevel = 1000;
        this.ConfigureMoves(move);

        await this.plugIn.HandleCommandAsync(this.player, "/custommove " + MoveName).ConfigureAwait(false);

        this.AssertSingleMessage(nameof(PlayerMessage.CustomMoveLevelTooLow), move.MinLevel);
    }

    /// <summary>
    /// Tests that a move which is too expensive shows the not enough money message
    /// and doesn't deduct any money.
    /// </summary>
    [Test]
    public async Task TooExpensiveMoveShowsNotEnoughMoneyAndKeepsMoney()
    {
        var move = this.CreateMove();
        move.Map = CreateMap();
        move.Cost = 1000;
        this.ConfigureMoves(move);
        this.player.Money = 100;

        await this.plugIn.HandleCommandAsync(this.player, "/custommove " + MoveName).ConfigureAwait(false);

        this.AssertSingleMessage(nameof(PlayerMessage.CustomMoveNotEnoughMoney), move.Cost);
        Assert.That(this.player.Money, Is.EqualTo(100));
    }

    /// <summary>
    /// Tests that a successful move warps the player to the configured coordinates
    /// and deducts the configured cost.
    /// </summary>
    [Test]
    public async Task SuccessfulMoveWarpsPlayerAndDeductsMoney()
    {
        var move = this.CreateMove();
        move.Map = CreateMap();
        move.X = 100;
        move.Y = 50;
        move.Cost = 100;
        this.ConfigureMoves(move);
        this.player.Money = 200;

        await this.plugIn.HandleCommandAsync(this.player, "/custommove " + MoveName).ConfigureAwait(false);

        this.AssertSingleMessage(nameof(PlayerMessage.CustomMoveSuccess), move.Name, move.Map.Name);
        Assert.That(this.player.Money, Is.EqualTo(100));
        Assert.That(this.player.SelectedCharacter!.PositionX, Is.EqualTo((byte)100));
        Assert.That(this.player.SelectedCharacter!.PositionY, Is.EqualTo((byte)50));
        Assert.That(this.player.SelectedCharacter!.CurrentMap, Is.SameAs(move.Map));
    }

    /// <summary>
    /// Tests that the move name lookup is case-insensitive.
    /// </summary>
    [Test]
    public async Task SuccessfulMoveIsCaseInsensitive()
    {
        var move = this.CreateMove();
        move.Map = CreateMap();
        this.ConfigureMoves(move);

        await this.plugIn.HandleCommandAsync(this.player, "/custommove BAR").ConfigureAwait(false);

        this.AssertSingleMessage(nameof(PlayerMessage.CustomMoveSuccess), move.Name, move.Map.Name);
    }

    /// <summary>
    /// Tests that a free move doesn't require any money.
    /// </summary>
    [Test]
    public async Task FreeMoveDoesNotRequireMoney()
    {
        var move = this.CreateMove();
        move.Map = CreateMap();
        this.ConfigureMoves(move);
        this.player.Money = 0;

        await this.plugIn.HandleCommandAsync(this.player, "/custommove " + MoveName).ConfigureAwait(false);

        this.AssertSingleMessage(nameof(PlayerMessage.CustomMoveSuccess), move.Name, move.Map.Name);
        Assert.That(this.player.Money, Is.EqualTo(0));
    }

    /// <summary>
    /// Tests that a move whose map has unmet entry requirements is rejected with the
    /// map requirement message and doesn't charge money, consistent with the native warp behavior.
    /// </summary>
    [Test]
    public async Task MoveWithUnmetMapRequirementShowsRequirementMessageAndKeepsMoney()
    {
        var map = new RestrictedMap();
        map.SetMapRequirements(new AttributeRequirement { Attribute = Stats.Level, MinimumValue = 1000 });
        var move = this.CreateMove();
        move.Map = map;
        this.ConfigureMoves(move);
        this.player.Money = 100;

        await this.plugIn.HandleCommandAsync(this.player, "/custommove " + MoveName).ConfigureAwait(false);

        this.AssertSingleMessage(
            nameof(PlayerMessage.MissingMapRequirement),
            map.MapRequirements.First().Attribute?.Description);
        Assert.That(this.player.Money, Is.EqualTo(100));
    }

    /// <summary>
    /// Tests that a move to an unwalkable coordinate (wall) is rejected before charging
    /// money, so that failures don't cause wrongful charges.
    /// </summary>
    [Test]
    public async Task MoveToUnwalkableCoordinateShowsInvalidCoordinatesAndKeepsMoney()
    {
        var move = this.CreateMove();
        move.Map = CreateMapWithWallAt(100, 50);
        move.X = 100;
        move.Y = 50;
        move.Cost = 100;
        this.ConfigureMoves(move);
        this.player.Money = 500;

        await this.plugIn.HandleCommandAsync(this.player, "/custommove " + MoveName).ConfigureAwait(false);

        this.AssertSingleMessage(nameof(PlayerMessage.CustomMoveInvalidCoordinates));
        Assert.That(this.player.Money, Is.EqualTo(500));
    }

    /// <summary>
    /// Tests that a move to a walkable coordinate with explicit terrain data succeeds.
    /// </summary>
    [Test]
    public async Task MoveToWalkableCoordinateWithTerrainDataSucceeds()
    {
        var move = this.CreateMove();
        move.Map = CreateMapWithWallAt(100, 50);
        move.X = 101;
        move.Y = 51;
        this.ConfigureMoves(move);
        this.player.Money = 0;

        await this.plugIn.HandleCommandAsync(this.player, "/custommove " + MoveName).ConfigureAwait(false);

        this.AssertSingleMessage(nameof(PlayerMessage.CustomMoveSuccess), move.Name, move.Map.Name);
        Assert.That(this.player.SelectedCharacter!.PositionX, Is.EqualTo((byte)101));
        Assert.That(this.player.SelectedCharacter!.PositionY, Is.EqualTo((byte)51));
    }

    /// <summary>
    /// Tests that a negative cost is treated as a free move and never grants money.
    /// </summary>
    [Test]
    public async Task NegativeCostIsTreatedAsFree()
    {
        var move = this.CreateMove();
        move.Map = CreateMap();
        move.Cost = -50;
        this.ConfigureMoves(move);
        this.player.Money = 0;

        await this.plugIn.HandleCommandAsync(this.player, "/custommove " + MoveName).ConfigureAwait(false);

        this.AssertSingleMessage(nameof(PlayerMessage.CustomMoveSuccess), move.Name, move.Map.Name);
        Assert.That(this.player.Money, Is.EqualTo(0));
    }

    private CustomMoveEntry CreateMove()
    {
        return new CustomMoveEntry(Guid.NewGuid()) { Name = MoveName };
    }

    private static GameMapDefinition CreateMap()
    {
        return new GameMapDefinition { Name = "Lorencia" };
    }

    /// <summary>
    /// Creates a map whose terrain marks the specified coordinate as not walkable.
    /// Terrain data format: 3 header bytes, followed by a 256x256 grid where
    /// 0 = walkable, 1 = safezone (walkable) and everything else = not walkable.
    /// </summary>
    private static GameMapDefinition CreateMapWithWallAt(byte x, byte y)
    {
        var terrainData = new byte[3 + (256 * 256)];
        terrainData[3 + (y * 256) + x] = 2;
        return new GameMapDefinition { Name = "Lorencia", TerrainData = terrainData };
    }

    private void ConfigureMoves(params CustomMoveEntry[] moves)
    {
        this.plugIn.Configuration = new CustomMoveConfiguration { Moves = moves.ToList() };
    }

    private void AssertSingleMessage(string messageKey, params object?[] arguments)
    {
        Assert.That(this.viewContainer.MessagePlugIn.Messages, Has.Count.EqualTo(1));
        var (message, messageType) = this.viewContainer.MessagePlugIn.Messages[0];
        Assert.That(messageType, Is.EqualTo(MessageType.BlueNormal));
        Assert.That(message, Is.EqualTo(this.player.GetLocalizedMessage(messageKey, arguments)));
    }

    /// <summary>
    /// A map definition which allows setting the protected
    /// <see cref="GameMapDefinition.MapRequirements"/> collection for tests.
    /// </summary>
    private sealed class RestrictedMap : GameMapDefinition
    {
        /// <summary>
        /// Sets the map requirements.
        /// </summary>
        /// <param name="requirements">The requirements.</param>
        public void SetMapRequirements(params AttributeRequirement[] requirements)
        {
            this.MapRequirements = requirements.ToList();
        }
    }
}
