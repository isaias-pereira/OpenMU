// <copyright file="CashShopStorageListAction.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.CashShop;

using MUnique.OpenMU.GameLogic.Views.CashShop;

/// <summary>
/// Action to handle the cash shop storage list request (D2 05).
/// </summary>
/// <remarks>
/// The cash shop storage is not implemented yet (FASE 9). To keep the client UI responsive —
/// it initializes its storage page list only when D2 06 arrives — this action always answers
/// with an empty storage. When the real storage comes, this is the place to page the items and
/// send the item list (D2 0D) afterwards.
/// </remarks>
public class CashShopStorageListAction
{
    /// <summary>
    /// Handles the storage list request of the client by answering an empty storage (D2 06).
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="pageIndex">The requested page index.</param>
    /// <param name="inventoryType">The requested inventory (storage) type.</param>
    public async ValueTask RequestListAsync(Player player, uint pageIndex, byte inventoryType)
    {
        if (player.SelectedCharacter is null)
        {
            return;
        }

        // Empty storage: 0 items, 0 pages. pageIndex is echoed clamped to ushort so that the
        // client shows the requested page as current.
        var page = (ushort)Math.Min(pageIndex, ushort.MaxValue);
        await player.InvokeViewPlugInAsync<ICashShopViewPlugIn>(
            p => p.ShowStorageCountAsync(0, 0, page, 0)).ConfigureAwait(false);
    }
}
