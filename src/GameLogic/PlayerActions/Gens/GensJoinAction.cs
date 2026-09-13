// <copyright file="GensJoinAction.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.Gens;

using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.Views.Gens;

/// <summary>
/// Action to join a gens by talking to one of the gens NPCs (543 Duprian / 544 Vanert).
/// </summary>
public class GensJoinAction
{
    /// <summary>
    /// Joins the player to the specified gens.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="gensType">The gens type to join.</param>
    public async ValueTask JoinAsync(Player player, GensType gensType)
    {
        var character = player.SelectedCharacter;
        if (character is null)
        {
            return;
        }

        var success = await GensService.JoinAsync(player.PersistenceContext, character.Id, gensType).ConfigureAwait(false);
        var status = GensService.GetGensStatus(character.Id);
        await player.InvokeViewPlugInAsync<IGensViewPlugIn>(p => p.ShowGensJoinResponseAsync(success, status)).ConfigureAwait(false);
        if (success && status is not null)
        {
            await player.InvokeViewPlugInAsync<IGensViewPlugIn>(p => p.UpdateGensInfoAsync(status)).ConfigureAwait(false);
            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.GensJoinSuccess), gensType).ConfigureAwait(false);
        }
        else
        {
            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.GensJoinFailed)).ConfigureAwait(false);
        }
    }
}