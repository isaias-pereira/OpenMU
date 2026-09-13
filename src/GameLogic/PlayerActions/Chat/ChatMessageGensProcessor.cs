// <copyright file="ChatMessageGensProcessor.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.Chat;

using System.ComponentModel;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.PlugIns;
using MUnique.OpenMU.GameLogic.Views;

/// <summary>
/// A chat message processor which sends the message to all players of the same gens.
/// </summary>
public class ChatMessageGensProcessor : BannableChatMessageBaseProcessor
{
    /// <inheritdoc />
    public override async ValueTask SubclassProcessMessageAsync(Player sender, (string Message, string PlayerName) content)
    {
        var eventArgs = new CancelEventArgs();
        sender.GameContext.PlugInManager.GetPlugInPoint<IChatMessageReceivedPlugIn>()?.ChatMessageReceived(sender, content.Message, eventArgs);
        if (eventArgs.Cancel)
        {
            return;
        }

        var character = sender.SelectedCharacter;
        if (character is null)
        {
            return;
        }

        var status = GensService.GetGensStatus(character.Id);
        if (status is null || status.GensType == GensType.Undefined)
        {
            await sender.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.GensNotInGens)).ConfigureAwait(false);
            return;
        }

        var players = await sender.GameContext.GetPlayersAsync().ConfigureAwait(false);
        foreach (var target in players)
        {
            if (target == sender)
            {
                continue;
            }

            var targetCharacter = target.SelectedCharacter;
            var targetStatus = targetCharacter is null ? null : GensService.GetGensStatus(targetCharacter.Id);
            if (targetStatus is { } otherStatus && otherStatus.GensType == status.GensType)
            {
                await target.InvokeViewPlugInAsync<IChatViewPlugIn>(
                    p => p.ChatMessageAsync(content.Message, character.Name, ChatMessageType.Gens)).ConfigureAwait(false);
            }
        }
    }
}