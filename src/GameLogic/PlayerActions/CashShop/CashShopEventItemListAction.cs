// <copyright file="CashShopEventItemListAction.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.CashShop;

using MUnique.OpenMU.GameLogic.Views.CashShop;

/// <summary>
/// Action to handle the cash shop event item list request (D2 13).
/// </summary>
/// <remarks>
/// The event shop is not implemented yet (FASE 10), so this always answers with a count of
/// zero items, which the client renders as an empty event list.
/// </remarks>
public class CashShopEventItemListAction
{
    /// <summary>
    /// Handles the event item list request by answering a zero item count (D2 13).
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="categoryIndex">The requested event category index.</param>
    public async ValueTask RequestListAsync(Player player, uint categoryIndex)
    {
        if (player.SelectedCharacter is null)
        {
            return;
        }

        await player.InvokeViewPlugInAsync<ICashShopViewPlugIn>(p => p.ShowEventItemCountAsync(0)).ConfigureAwait(false);
    }
}
