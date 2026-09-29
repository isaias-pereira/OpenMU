// <copyright file="ChatMessageCommandProcessorTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests.CustomMove;

using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlayerActions.Chat;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.GameLogic.Properties;
using MUnique.OpenMU.Interfaces;
using NUnit.Framework;

/// <summary>
/// Tests for the <see cref="ChatMessageCommandProcessor"/>, especially the
/// resolution of configured custom move aliases (e.g. "/bar") to the /move command.
/// </summary>
[TestFixture]
public class ChatMessageCommandProcessorTests
{
    private const string MoveName = "lorencia_bar";
    private const string Alias = "bar";

    private RecordingViewPlugInContainer viewContainer = null!;
    private Player player = null!;
    private RecordingCustomMovePlugIn movePlugIn = null!;
    private ChatMessageCommandProcessor processor = null!;

    /// <summary>
    /// Sets up a test player with a registered recording /move plugin.
    /// </summary>
    [SetUp]
    public async Task SetUpAsync()
    {
        this.viewContainer = new RecordingViewPlugInContainer();
        this.player = await PlayerTestHelper.CreatePlayerAsync(() => this.viewContainer).ConfigureAwait(false);
        this.movePlugIn = new RecordingCustomMovePlugIn
        {
            Configuration = new CustomMoveConfiguration { Moves = new List<CustomMoveEntry>() },
        };
        this.player.GameContext.PlugInManager.RegisterPlugInAtPlugInPoint<IChatCommandPlugIn>(this.movePlugIn);
        this.processor = new ChatMessageCommandProcessor();
    }

    /// <summary>
    /// Tests that a configured alias (e.g. "/bar") is rewritten to a /move command
    /// which uses the name of the move.
    /// Regression test: the rewritten command must never use the configured alias,
    /// because the /move plugin resolves moves by their name.
    /// </summary>
    [Test]
    public async Task CustomAliasIsRewrittenToMoveName()
    {
        this.AddConfiguredMove(MoveName, Alias);

        await this.processor.ProcessMessageAsync(this.player, ("/bar", "Tester")).ConfigureAwait(false);

        Assert.That(this.movePlugIn.ReceivedCommands, Is.EqualTo(new[] { "/move lorencia_bar" }));
    }

    /// <summary>
    /// Tests that additional arguments after the alias are kept in the rewritten command.
    /// </summary>
    [Test]
    public async Task CustomAliasKeepsAdditionalArguments()
    {
        this.AddConfiguredMove(MoveName, Alias);

        await this.processor.ProcessMessageAsync(this.player, ("/bar arg1 arg2", "Tester")).ConfigureAwait(false);

        Assert.That(this.movePlugIn.ReceivedCommands, Is.EqualTo(new[] { "/move lorencia_bar arg1 arg2" }));
    }

    /// <summary>
    /// Tests that a move without a configured command is reachable by using its name as command.
    /// </summary>
    [Test]
    public async Task MoveWithoutCommandAliasIsResolvedByName()
    {
        this.AddConfiguredMove("bar", null);

        await this.processor.ProcessMessageAsync(this.player, ("/bar", "Tester")).ConfigureAwait(false);

        Assert.That(this.movePlugIn.ReceivedCommands, Is.EqualTo(new[] { "/move bar" }));
    }

    /// <summary>
    /// Tests that a directly entered /move command is passed to the /move plugin unchanged.
    /// </summary>
    [Test]
    public async Task DirectMoveCommandIsPassedThroughUnchanged()
    {
        this.AddConfiguredMove(MoveName, Alias);

        await this.processor.ProcessMessageAsync(this.player, ("/move lorencia_bar", "Tester")).ConfigureAwait(false);

        Assert.That(this.movePlugIn.ReceivedCommands, Is.EqualTo(new[] { "/move lorencia_bar" }));
    }

    /// <summary>
    /// Tests that an unknown command shows the invalid command message, without
    /// calling the /move plugin.
    /// </summary>
    [Test]
    public async Task UnknownCommandShowsInvalidCommandMessage()
    {
        this.AddConfiguredMove(MoveName, Alias);

        await this.processor.ProcessMessageAsync(this.player, ("/unknown", "Tester")).ConfigureAwait(false);

        Assert.That(this.movePlugIn.ReceivedCommands, Is.Empty);
        Assert.That(this.viewContainer.MessagePlugIn.Messages, Has.Count.EqualTo(1));
        var (message, messageType) = this.viewContainer.MessagePlugIn.Messages[0];
        Assert.That(messageType, Is.EqualTo(MessageType.BlueNormal));
        Assert.That(message, Is.EqualTo(this.player.GetLocalizedMessage(nameof(PlayerMessage.InvalidChatCommand), "/unknown")));
    }

    private void AddConfiguredMove(string name, string? command)
    {
        this.movePlugIn.Configuration!.Moves.Add(new CustomMoveEntry(Guid.NewGuid()) { Name = name, Command = command });
    }

    /// <summary>
    /// A <see cref="CustomMoveChatCommandPlugIn"/> which records the commands it receives,
    /// instead of executing them.
    /// </summary>
    [Guid("3F9C2A6B-8E4D-4C1F-B5A8-7D2E9F0C4B21")]
    private sealed class RecordingCustomMovePlugIn : CustomMoveChatCommandPlugIn
    {
        /// <summary>
        /// Gets the list of commands which were received by this plugin.
        /// </summary>
        public List<string> ReceivedCommands { get; } = new();

        /// <inheritdoc />
        public override ValueTask HandleCommandAsync(Player player, string command)
        {
            this.ReceivedCommands.Add(command);
            return ValueTask.CompletedTask;
        }
    }
}
