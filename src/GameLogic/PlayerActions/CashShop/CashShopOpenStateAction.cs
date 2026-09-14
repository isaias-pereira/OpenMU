// <copyright file="CashShopOpenStateAction.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.CashShop;

using MUnique.OpenMU.GameLogic.Views.CashShop;

/// <summary>
/// Action to handle the cash shop open/close request (D2 02).
/// </summary>
public class CashShopOpenStateAction
{
    /// <summary>
    /// Handles the request of the client to open or close the shop dialog.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="isClosed">If set to <c>true</c>, the client closed the dialog and nothing is answered.</param>
    public async ValueTask HandleOpenStateAsync(Player player, bool isClosed)
    {
        if (isClosed)
        {
            // The client only informs us that it closed the dialog.
            return;
        }

        if (player.SelectedCharacter is null)
        {
            return;
        }

        // The client (CNewUIInGameShop::IsInGameShopOpen) refuses to even ask for opening when
        // Hero->SafeZone is false: the message says "You can only open MU Item Shop in a town or
        // safe zone", and the composed WD_* term of the condition is always false, so the only
        // effective requirement is the safezone flag. We mirror that here so a modified client
        // cannot open the shop outside of town.
        var isOpenAllowed = player.IsAtSafezone();

        await player.InvokeViewPlugInAsync<ICashShopViewPlugIn>(p => p.ShowOpenResultAsync(isOpenAllowed)).ConfigureAwait(false);
    }
}
