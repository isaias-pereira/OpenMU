// <copyright file="CashShopPointInfoAction.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.CashShop;

using MUnique.OpenMU.GameLogic.Views.CashShop;

/// <summary>
/// Action to answer the cash shop point info request (D2 01).
/// </summary>
/// <remarks>
/// The account wallet (WCoin) is the single currency implemented so far: WCoinP is shown as the
/// wallet balance; WCoinC, total point and mileage are reported as 0 until they are implemented.
/// </remarks>
public class CashShopPointInfoAction
{
    /// <summary>
    /// Sends the point info of the player's account to the client.
    /// </summary>
    /// <param name="player">The player.</param>
    public async ValueTask SetPointInfoAsync(Player player)
    {
        if (player.SelectedCharacter is null || player.Account is not { } account)
        {
            return;
        }

        var balance = await WCoinService.GetBalanceAsync(player.PersistenceContext, account.Id).ConfigureAwait(false);
        var prepaid = (double)Math.Max(0, balance);

        await player.InvokeViewPlugInAsync<ICashShopViewPlugIn>(
            p => p.ShowPointInfoAsync(prepaid, 0, prepaid, 0, 0)).ConfigureAwait(false);
    }
}
