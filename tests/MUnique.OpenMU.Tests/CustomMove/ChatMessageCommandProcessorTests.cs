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
/// resolution of configured custom move aliases (e.g. "/bar") to the custom move
/// command, and the coexistence with the native "/move" command.
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
    /// Sets up a test player with a registered recording custom move plugin.
    /// </summary>
    [SetUp]
    public async Task SetUpAsync()
    {
        this.viewContainer = new RecordingViewPlugInContainer();
        this.player = await PlayerTestHelper.CreatePlayerAsync(() => this.viewContainer).ConfigureAwait(false);
        this.movePlugIn = new RecordingCustomMovePlugIn
        {
            Configuration = new CustomMoveConfiguration { Enabled = true, Moves = new List<CustomMoveEntry>() },
        };
        this.player.GameContext.PlugInManager.RegisterPlugInAtPlugInPoint<IChatCommandPlugIn>(this.movePlugIn);
        this.processor = new ChatMessageCommandProcessor();
    }

    /// <summary>
    /// Tests that a configured alias (e.g. "/bar") is rewritten to a custom move command
    /// which uses the name of the move.
    /// Regression test: the rewritten command must never use the configured alias,
    /// because the custom move plugin resolves moves by their name.
    /// </summary>
    [Test]
    public async Task CustomAliasIsRewrittenToMoveName()
    {
        this.AddConfiguredMove(MoveName, Alias);

        await this.processor.ProcessMessageAsync(this.player, ("/bar", "Tester")).ConfigureAwait(false);

        Assert.That(this.movePlugIn.ReceivedCommands, Is.EqualTo(new[] { "/custommove lorencia_bar" }));
    }

    /// <summary>
    /// Tests that additional arguments after the alias are kept in the rewritten command.
    /// </summary>
    [Test]
    public async Task CustomAliasKeepsAdditionalArguments()
    {
        this.AddConfiguredMove(MoveName, Alias);

        await this.processor.ProcessMessageAsync(this.player, ("/bar arg1 arg2", "Tester")).ConfigureAwait(false);

        Assert.That(this.movePlugIn.ReceivedCommands, Is.EqualTo(new[] { "/custommove lorencia_bar arg1 arg2" }));
    }

    /// <summary>
    /// Tests that a move without a configured command is reachable by using its name as command.
    /// </summary>
    [Test]
    public async Task MoveWithoutCommandAliasIsResolvedByName()
    {
        this.AddConfiguredMove("bar", null);

        await this.processor.ProcessMessageAsync(this.player, ("/bar", "Tester")).ConfigureAwait(false);

        Assert.That(this.movePlugIn.ReceivedCommands, Is.EqualTo(new[] { "/custommove bar" }));
    }

    /// <summary>
    /// Tests that a directly entered custom move command is passed to the
    /// custom move plugin unchanged.
    /// </summary>
    [Test]
    public async Task DirectCustomMoveCommandIsPassedThroughUnchanged()
    {
        this.AddConfiguredMove(MoveName, Alias);

        await this.processor.ProcessMessageAsync(this.player, ("/custommove lorencia_bar", "Tester")).ConfigureAwait(false);

        Assert.That(this.movePlugIn.ReceivedCommands, Is.EqualTo(new[] { "/custommove lorencia_bar" }));
    }

    /// <summary>
    /// Tests that the native "/move" command is not shadowed by the custom move
    /// plugin: "/move" is dispatched to the plugin which registered the "/move" key,
    /// while the custom aliases are still resolved as fallback.
    /// Regression test for the incident where the custom move plugin registered
    /// itself under "/move" and thereby made the native move command ineffective.
    /// </summary>
    [Test]
    public async Task NativeMoveCommandIsNotShadowedByCustomMove()
    {
        this.AddConfiguredMove(MoveName, Alias);
        var nativeMovePlugIn = new RecordingNativeMoveCommandPlugIn();
        this.player.GameContext.PlugInManager.RegisterPlugInAtPlugInPoint<IChatCommandPlugIn>(nativeMovePlugIn);

        await this.processor.ProcessMessageAsync(this.player, ("/move lorencia", "Tester")).ConfigureAwait(false);
        await this.processor.ProcessMessageAsync(this.player, ("/bar", "Tester")).ConfigureAwait(false);

        Assert.That(nativeMovePlugIn.ReceivedCommands, Is.EqualTo(new[] { "/move lorencia" }));
        Assert.That(this.movePlugIn.ReceivedCommands, Is.EqualTo(new[] { "/custommove lorencia_bar" }));
    }

    /// <summary>
    /// Tests that configured aliases are ignored while the configuration is disabled
    /// (CustomMoveConfiguration.Enabled = false), so that the native invalid command
    /// handling takes over instead of executing the moves.
    /// </summary>
    [Test]
    public async Task AliasIsIgnoredWhenConfigurationIsDisabled()
    {
        this.AddConfiguredMove(MoveName, Alias);
        this.movePlugIn.Configuration!.Enabled = false;

        await this.processor.ProcessMessageAsync(this.player, ("/bar", "Tester")).ConfigureAwait(false);

        Assert.That(this.movePlugIn.ReceivedCommands, Is.Empty);
        Assert.That(this.viewContainer.MessagePlugIn.Messages, Has.Count.EqualTo(1));
        Assert.That(this.viewContainer.MessagePlugIn.Messages[0].Message, Is.EqualTo(this.player.GetLocalizedMessage(nameof(PlayerMessage.InvalidChatCommand), "/bar")));
    }

    /// <summary>
    /// Tests that an unknown command shows the invalid command message, without
    /// calling the custom move plugin.
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

    /// <summary>
    /// Tests that a native chat command takes precedence over a custom move which was
    /// configured with the same effective command, so that configured moves can never
    /// shadow built-in commands.
    /// </summary>
    [Test]
    public async Task NativeCommandTakesPrecedenceOverCustomMoveAlias()
    {
        this.AddConfiguredMove("post", "post");
        var nativePlugIn = new RecordingNativeCommandPlugIn();
        this.player.GameContext.PlugInManager.RegisterPlugInAtPlugInPoint<IChatCommandPlugIn>(nativePlugIn);

        await this.processor.ProcessMessageAsync(this.player, ("/post hello", "Tester")).ConfigureAwait(false);

        Assert.That(nativePlugIn.ReceivedCommands, Is.EqualTo(new[] { "/post hello" }));
        Assert.That(this.movePlugIn.ReceivedCommands, Is.Empty);
    }

    /// <summary>
    /// Tests that a custom move alias still works when there is no native command
    /// with the same key (the fallback behavior).
    /// </summary>
    [Test]
    public async Task CustomMoveAliasWorksWithoutNativeCommandConflict()
    {
        this.AddConfiguredMove(MoveName, Alias);
        var nativePlugIn = new RecordingNativeCommandPlugIn();
        this.player.GameContext.PlugInManager.RegisterPlugInAtPlugInPoint<IChatCommandPlugIn>(nativePlugIn);

        await this.processor.ProcessMessageAsync(this.player, ("/bar", "Tester")).ConfigureAwait(false);

        Assert.That(this.movePlugIn.ReceivedCommands, Is.EqualTo(new[] { $"/custommove {MoveName}" }));
        Assert.That(nativePlugIn.ReceivedCommands, Is.Empty);
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

    /// <summary>
    /// A native move command plugin which records the commands it receives,
    /// instead of executing them. It uses the "/move" key, like the native
    /// <see cref="MoveChatCommandPlugIn"/> of the game.
    /// </summary>
    [Guid("4E7C5A19-2F0D-4A8E-B7C6-8A2D3B9E4F10")]
    private sealed class RecordingNativeMoveCommandPlugIn : ChatCommandPlugInBase<ArgumentsBase>
    {
        /// <summary>
        /// Gets the list of commands which were received by this plugin.
        /// </summary>
        public List<string> ReceivedCommands { get; } = new();

        /// <inheritdoc />
        public override string Key => "/move";

        /// <inheritdoc />
        public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

        /// <inheritdoc />
        protected override ValueTask DoHandleCommandAsync(Player player, ArgumentsBase arguments)
        {
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public override ValueTask HandleCommandAsync(Player player, string command)
        {
            this.ReceivedCommands.Add(command);
            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    /// A native chat command plugin which records the commands it receives,
    /// instead of executing them.
    /// </summary>
    [Guid("8D2B7C4A-1F3E-4D9B-A6C5-0E7F8A9B3C42")]
    private sealed class RecordingNativeCommandPlugIn : ChatCommandPlugInBase<ArgumentsBase>
    {
        /// <summary>
        /// Gets the list of commands which were received by this plugin.
        /// </summary>
        public List<string> ReceivedCommands { get; } = new();

        /// <inheritdoc />
        public override string Key => "/post";

        /// <inheritdoc />
        public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

        /// <inheritdoc />
        protected override ValueTask DoHandleCommandAsync(Player player, ArgumentsBase arguments)
        {
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public override ValueTask HandleCommandAsync(Player player, string command)
        {
            this.ReceivedCommands.Add(command);
            return ValueTask.CompletedTask;
        }
    }
}
