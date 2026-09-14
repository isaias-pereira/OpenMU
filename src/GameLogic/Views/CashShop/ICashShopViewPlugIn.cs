// <copyright file="ICashShopViewPlugIn.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.CashShop;

/// <summary>
/// Interface of a view whose implementation informs about the cash shop (packet group 0xD2).
/// </summary>
public interface ICashShopViewPlugIn : IViewPlugIn
{
    /// <summary>
    /// Sends the point info (D2 01) with the current cash shop currencies of the account.
    /// </summary>
    /// <param name="totalCash">The total cash (C + P).</param>
    /// <param name="cashCredit">The cash credit (WCoinC).</param>
    /// <param name="cashPrepaid">The cash prepaid (WCoinP) — the wallet balance.</param>
    /// <param name="totalPoint">The total point.</param>
    /// <param name="totalMileage">The total mileage.</param>
    ValueTask ShowPointInfoAsync(double totalCash, double cashCredit, double cashPrepaid, double totalPoint, double totalMileage);

    /// <summary>
    /// Sends the result of the shop open request (D2 02). When it's not a failure, the client
    /// requests the point info (D2 01) and the storage list (D2 05) on its own.
    /// </summary>
    /// <param name="success">If set to <c>true</c>, the client shows the shop dialog.</param>
    ValueTask ShowOpenResultAsync(bool success);

    /// <summary>
    /// Sends the storage page count information (D2 06). Without this message the client
    /// storage page list stays uninitialized.
    /// </summary>
    /// <param name="totalItemCount">The total number of items in the storage.</param>
    /// <param name="currentPageItemCount">The number of items on the current page.</param>
    /// <param name="pageIndex">The index of the current page.</param>
    /// <param name="totalPage">The total number of pages.</param>
    ValueTask ShowStorageCountAsync(ushort totalItemCount, ushort currentPageItemCount, ushort pageIndex, ushort totalPage);

    /// <summary>
    /// Sends the script catalog version (D2 0C). This is the message which unlocks the shop
    /// client-side (SetScriptVersion + ShopOpenUnLock). The client then loads the local
    /// catalog folder data\InGameShopScript\{saleZone:000}.{year:0000}.{yearIdentify:000}\.
    /// </summary>
    /// <param name="saleZone">The sale zone of the catalog (e.g. 512).</param>
    /// <param name="year">The year of the catalog (e.g. 2012).</param>
    /// <param name="yearIdentify">The year identifier of the catalog (e.g. 76).</param>
    ValueTask ShowScriptVersionAsync(ushort saleZone, ushort year, ushort yearIdentify);

    /// <summary>
    /// Sends the banner catalog version (D2 15).
    /// </summary>
    /// <param name="bannerZone">The banner zone (e.g. 583).</param>
    /// <param name="year">The year of the banner catalog (e.g. 2011).</param>
    /// <param name="yearIdentify">The year identifier of the banner catalog (e.g. 1).</param>
    ValueTask ShowBannerVersionAsync(ushort bannerZone, ushort year, ushort yearIdentify);

    /// <summary>
    /// Sends the count of event items of the requested event category (D2 13).
    /// </summary>
    /// <param name="itemCount">The number of event items; 0 leaves the event list empty.</param>
    ValueTask ShowEventItemCountAsync(ushort itemCount);
}
