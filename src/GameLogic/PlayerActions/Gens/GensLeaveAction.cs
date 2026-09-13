// <copyright file="GensLeaveAction.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.Gens;

using MUnique.OpenMU.GameLogic.Views.Gens;

/// <summary>
/// Action to leave the current gens by talking to one of the gens NPCs.
/// </summary>
public class GensLeaveAction
{
    /// <summary>
    /// Lets the player leave the current gens. The accumulated contribution points are lost.
    /// </summary>
    /// <param name="player">The player.</param>
    public async ValueTask LeaveAsync(Player player)
    {
        var character = player.SelectedCharacter;
        if (character is null)
        {
            return;
        }

        var success = await GensService.LeaveAsync(player.PersistenceContext, character.Id).ConfigureAwait(false);
        await player.InvokeViewPlugInAsync<IGensViewPlugIn>(p => p.ShowGensLeaveResponseAsync(success)).ConfigureAwait(false);
        await player.ShowLocalizedBlueMessageAsync(success ? nameof(PlayerMessage.GensLeaveSuccess) : nameof(PlayerMessage.GensLeaveFailed)).ConfigureAwait(false);
    }
}