// <copyright file="GensRankingAction.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.Gens;

using MUnique.OpenMU.GameLogic.Views.Gens;

/// <summary>
/// Action to request the current gens ranking from a gens NPC.
/// </summary>
public class GensRankingAction
{
    /// <summary>
    /// Sends the current gens ranking to the player.
    /// </summary>
    /// <param name="player">The player.</param>
    public async ValueTask ShowRankingAsync(Player player)
    {
        var character = player.SelectedCharacter;
        if (character is null)
        {
            return;
        }

        var status = GensService.GetGensStatus(character.Id);
        if (status is { } gensStatus && gensStatus.GensType != MUnique.OpenMU.DataModel.Entities.GensType.Undefined)
        {
            var entries = await GensService.GetRankingAsync(player.PersistenceContext, gensStatus.GensType).ConfigureAwait(false);
            await player.InvokeViewPlugInAsync<IGensViewPlugIn>(p => p.ShowGensRankingAsync(entries)).ConfigureAwait(false);
        }
        else
        {
            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.GensNotInGens)).ConfigureAwait(false);
        }
    }
}