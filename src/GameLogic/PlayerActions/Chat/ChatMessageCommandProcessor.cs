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

        // Native commands take precedence over custom move aliases, so that a configured
        // move can never shadow a built-in chat command (e.g. "/post").
        var commandHandler = sender.GameContext.PlugInManager.GetStrategy<IChatCommandPlugIn>(commandKey);
        if (commandHandler is null)
        {
            // Check custom move commands (e.g. "/bar") as fallback. The custom move plugin is
            // only consulted while it is activated (PlugInConfiguration.IsActive) and its
            // configuration is enabled (CustomMoveConfiguration.Enabled); both can be managed
            // via the admin panel. The plugin doesn't own the "/move" key, so the native move
            // command keeps working independently of this fallback.
            var movePlugIn = sender.GameContext.PlugInManager.GetStrategy<IChatCommandPlugIn>(CustomMoveChatCommandPlugIn.CommandKey);
            if (movePlugIn is ISupportCustomConfiguration<CustomMoveConfiguration> { Configuration: { Enabled: true } moveConfiguration })
            {
                var move = CustomMoveService.GetMove(commandKey, moveConfiguration);
                if (move is not null)
                {
                    if (sender.SelectedCharacter!.CharacterStatus < movePlugIn.MinCharacterStatusRequirement)
                    {
                        sender.Logger.LogWarning($"{sender.Name} is trying to execute {commandKey} command without meeting the requirements");
                        return;
                    }

                    // The plugin resolves moves by their name, so the rewritten command must
                    // use the name, even when the player typed the configured alias (e.g. "/bar").
                    var newContent = $"{CustomMoveChatCommandPlugIn.CommandKey} {move.Name}" + content.Message[commandKey.Length..];
                    await movePlugIn.HandleCommandAsync(sender, newContent).ConfigureAwait(false);
                    return;
                }
            }

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