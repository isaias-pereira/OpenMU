// <copyright file="CashShopEventItemListAction.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.CashShop;

using MUnique.OpenMU.GameLogic.CashShop;
using MUnique.OpenMU.GameLogic.Views.CashShop;

/// <summary>
/// Action to handle the cash shop event item list request (D2 13).
/// Integrado com o CashShopCatalogService (Fase 6) para retornar pacotes ativos por categoria.
/// </summary>
public class CashShopEventItemListAction
{
    private readonly ICashShopCatalogService _catalogService;

    /// <summary>
    /// Initializes a new instance of the <see cref="CashShopEventItemListAction"/> class.
    /// </summary>
    /// <param name="catalogService">The cash shop catalog service.</param>
    public CashShopEventItemListAction(ICashShopCatalogService catalogService)
    {
        this._catalogService = catalogService ?? throw new ArgumentNullException(nameof(catalogService));
    }

    /// <summary>
    /// Handles the event item list request by returning active packages for the requested category.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="categoryIndex">The requested event category index (ProductDisplaySeq).</param>
    public async ValueTask RequestListAsync(Player player, uint categoryIndex)
    {
        if (player.SelectedCharacter is null)
        {
            return;
        }

        // Buscar pacotes ativos da categoria solicitada
        var packages = await this._catalogService.GetPackagesByCategoryAsync((int)categoryIndex, player.GameContext.Configuration).ConfigureAwait(false);
        var activePackageCount = packages.Count;

        await player.InvokeViewPlugInAsync<ICashShopViewPlugIn>(p => p.ShowEventItemCountAsync((ushort)activePackageCount)).ConfigureAwait(false);
    }
}
