// <copyright file="ChatMessageCommandProcessor.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.Chat;

using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// A chat message processor which handles chat commands.
/// </summary>
public class ChatMessageCommandProcessor : IChatMessageProcessor
{
    /// <inheritdoc />
    public async ValueTask ProcessMessageAsync(Player sender, (string Message, string PlayerName) content)
    {
        var commandKey = content.Message.Split(' ').First();

        // Check custom move commands first (e.g. "/bar")
        var movePlugIn = sender.GameContext.PlugInManager.GetStrategy<IChatCommandPlugIn>("/move");
        if (movePlugIn is ISupportCustomConfiguration<CustomMoveConfiguration> moveConfig && moveConfig.Configuration is { } moveConfiguration)
        {
            var move = CustomMoveService.GetMove(commandKey, moveConfiguration);
            if (move is not null)
            {
                var newName = move.Command ?? move.Name;
                var newContent = $"/move {newName}" + content.Message[commandKey.Length..];
                await movePlugIn.HandleCommandAsync(sender, newContent).ConfigureAwait(false);
                return;
            }
        }

        var commandHandler = sender.GameContext.PlugInManager.GetStrategy<IChatCommandPlugIn>(commandKey);
        if (commandHandler is null)
        {
            await sender.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.InvalidChatCommand), commandKey).ConfigureAwait(false);
            return;
        }

        if (sender.SelectedCharacter!.CharacterStatus < commandHandler.MinCharacterStatusRequirement)
        {
            sender.Logger.LogWarning($"{sender.Name} is trying to execute {commandKey} command without meeting the requirements");
            return;
        }

        await commandHandler.HandleCommandAsync(sender, content.Message).ConfigureAwait(false);
    }
}