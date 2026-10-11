// <copyright file="CashShopStorageListAction.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.CashShop;

using System;
using System.Collections.Generic;
using System.Linq;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.CashShop;
using MUnique.OpenMU.GameLogic.Views.CashShop;

/// <summary>
/// Action to handle the cash shop storage list request (D2 05).
/// </summary>
/// <remarks>
/// The client asks for the storage list whenever the shop opens (<c>ReceiveIGS_ShopOpenResult</c>)
/// and again right after a successful purchase (<c>ReceiveIGS_BuyItem</c>, case 0). It first
/// receives the page counts (<c>D2 06</c>) and then exactly <c>wCurrentItemCount</c> lines
/// (<c>D2 0D</c>), so both are sent here from the account's storage.
/// </remarks>
public class CashShopStorageListAction
{
    /// <summary>
    /// O código de storage que o cliente envia para o storage de compras ("safekeeping").
    /// Confirmado em <c>CNewUIInGameShop::GetCurrentStorageCode</c>: a aba padrão devolve 'S'.
    /// </summary>
    public const byte DefaultStorageType = (byte)'S';

    /// <summary>
    /// O código de storage da caixa de presentes ("gift box"). É a aba que mostra o que a conta
    /// <b>recebeu</b> de outros jogadores, e o servidor a alimenta com as linhas marcadas como
    /// presente (<see cref="CashShopStorageItem.IsGift"/>).
    /// </summary>
    /// <remarks>
    /// O cliente distingue as abas por este byte no request D2 05, não pelo
    /// <c>chItemType</c> da resposta: <c>chItemType</c> só aceita 'P' (produto) e 'C' (moeda), e
    /// <c>AddStorageItem</c> descarta a linha para qualquer outro valor. Por isso um presente
    /// continua sendo enviado com 'P' e é separado das compras por <see cref="CashShopStorageItem.IsGift"/>.
    /// </remarks>
    public const byte GiftStorageType = (byte)'G';

    private readonly ICashShopStorageService _storageService;

    /// <summary>
    /// Initializes a new instance of the <see cref="CashShopStorageListAction"/> class.
    /// </summary>
    /// <param name="storageService">The cash shop storage service.</param>
    public CashShopStorageListAction(ICashShopStorageService storageService)
    {
        this._storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
    }

    /// <summary>
    /// Handles the storage list request of the client by answering the page counts (D2 06)
    /// followed by the items of the requested page: D2 0D for the purchase tab ('S') and
    /// D2 0E with sender/message for the gift tab ('G').
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="pageIndex">The requested page index.</param>
    /// <param name="inventoryType">The requested storage type ('S' = safekeeping, 'G' = gift box).</param>
    public async ValueTask RequestListAsync(Player player, uint pageIndex, byte inventoryType)
    {
        if (player.Account is not { } account)
        {
            return;
        }

        // O cliente distingue suas abas pelo código de storage que envia: 'S' é o safekeeping que
        // recebe as compras da própria conta, 'G' é a caixa de presentes com o que a conta
        // recebeu de outros jogadores. Qualquer outro código é recusado com a lista vazia, para
        // que uma aba desconhecida não reciba linhas de outra aba por engano.
        var isGiftTab = inventoryType == GiftStorageType;
        if (inventoryType != DefaultStorageType && !isGiftTab)
        {
            await player.InvokeViewPlugInAsync<ICashShopViewPlugIn>(
                p => p.ShowStorageCountAsync(0, 0, 0, 0)).ConfigureAwait(false);
            return;
        }

        var pageSize = this._storageService.Capacity;
        var totalCount = isGiftTab
            ? await this._storageService.GetGiftCountAsync(account.Id).ConfigureAwait(false)
            : await this._storageService.GetItemCountAsync(account.Id).ConfigureAwait(false);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        // The client requests page 1 when the shop opens, and 0/1 for the first page when
        // paging, so the index is normalized here: anything <= 1 is the first page.
        var requestedPage = pageIndex <= 1 ? 1 : (int)pageIndex;
        var skip = (requestedPage - 1) * pageSize;

        // Out-of-range pages answer with the counts only, which leaves the list empty.
        var currentPageItems = requestedPage <= totalPages
            ? isGiftTab
                ? await this._storageService.GetGiftPageAsync(account.Id, skip, pageSize).ConfigureAwait(false)
                : await this._storageService.GetPageAsync(account.Id, skip, pageSize).ConfigureAwait(false)
            : Array.Empty<CashShopStorageItem>();

        var currentPageItemCount = (ushort)Math.Min(currentPageItems.Count, ushort.MaxValue);
        await player.InvokeViewPlugInAsync<ICashShopViewPlugIn>(
            p => p.ShowStorageCountAsync(
                (ushort)Math.Min(totalCount, ushort.MaxValue),
                currentPageItemCount,
                (ushort)Math.Min(requestedPage, ushort.MaxValue),
                (ushort)Math.Min(totalPages, ushort.MaxValue))).ConfigureAwait(false);

        foreach (var item in currentPageItems)
        {
            // The client only renders 'P' (product) or 'C' (currency); anything else is
            // silently dropped by AddStorageItem, so never send a different value.
            var itemType = item.ItemType is { Length: > 0 } type ? type[0] : CashShopStorageItemType.Product[0];

            // Fase 10: a aba de presentes entrega cada linha pelo D2 0E (com remetente e
            // mensagem); a aba de compras continua pelo D2 0D. Nenhuma entrada recebe os dois.
            if (isGiftTab)
            {
                await player.InvokeViewPlugInAsync<ICashShopViewPlugIn>(
                    p => p.ShowGiftStorageItemAsync(
                        item.StorageIndex,
                        item.ItemSeq,
                        item.StorageGroup,
                        item.ProductSeq,
                        item.PriceSeq,
                        item.CashPoint,
                        itemType,
                        item.GiftSender,
                        item.GiftMessage)).ConfigureAwait(false);
            }
            else
            {
                await player.InvokeViewPlugInAsync<ICashShopViewPlugIn>(
                    p => p.ShowStorageItemAsync(
                        item.StorageIndex,
                        item.ItemSeq,
                        item.StorageGroup,
                        item.ProductSeq,
                        item.PriceSeq,
                        item.CashPoint,
                        itemType)).ConfigureAwait(false);
            }
        }
    }
}
