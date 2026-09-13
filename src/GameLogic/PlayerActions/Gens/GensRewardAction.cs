// <copyright file="GensRewardAction.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.Gens;

using MUnique.OpenMU.GameLogic.Views.Gens;
using MUnique.OpenMU.GameLogic.Views.Inventory;

/// <summary>
/// Action to exchange the accumulated gens contribution points for zen at a gens NPC.
/// </summary>
public class GensRewardAction
{
    /// <summary>
    /// Exchanges the contribution points of the player for zen.
    /// </summary>
    /// <param name="player">The player.</param>
    public async ValueTask RewardAsync(Player player)
    {
        var character = player.SelectedCharacter;
        if (character is null)
        {
            return;
        }

        var status = GensService.GetGensStatus(character.Id);
        if (status is null)
        {
            await GensService.LoadIntoCacheAsync(player.PersistenceContext, character.Id).ConfigureAwait(false);
            status = GensService.GetGensStatus(character.Id);
        }

        if (status is null || status.ContributionPoints <= 0)
        {
            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.GensRewardNoPoints)).ConfigureAwait(false);
            await player.InvokeViewPlugInAsync<IGensViewPlugIn>(p => p.ShowGensRewardResponseAsync(false, status, 0)).ConfigureAwait(false);
            return;
        }

        var zen = checked((long)Math.Min(status.ContributionPoints * GensService.RewardZenPerContributionPoint, int.MaxValue));
        if (!player.TryAddMoney((int)zen))
        {
            await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.GensRewardNoSpace)).ConfigureAwait(false);
            await player.InvokeViewPlugInAsync<IGensViewPlugIn>(p => p.ShowGensRewardResponseAsync(false, status, 0)).ConfigureAwait(false);
            return;
        }

        var reset = await GensService.ResetContributionAsync(player.PersistenceContext, character.Id).ConfigureAwait(false);
        if (!reset)
        {
            player.Logger.LogWarning("Could not reset the gens contribution of player {0}", player.Name);
        }

        await player.InvokeViewPlugInAsync<IUpdateMoneyPlugIn>(p => p.UpdateMoneyAsync()).ConfigureAwait(false);
        await player.InvokeViewPlugInAsync<IGensViewPlugIn>(p => p.ShowGensRewardResponseAsync(true, GensService.GetGensStatus(character.Id), zen)).ConfigureAwait(false);
        await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.GensRewardSuccess), zen).ConfigureAwait(false);
    }
}