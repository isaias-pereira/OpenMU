// <copyright file="CashShopViewPlugIn.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView.CashShop;

using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.Views.CashShop;
using MUnique.OpenMU.Network.Packets.ServerToClient;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// The default implementation of the <see cref="ICashShopViewPlugIn"/> which is forwarding everything
/// to the game client with the cash shop packets (packet group 0xD2).
/// </summary>
[PlugIn]
[Display(Name = "Cash Shop View", Description = "Sends the cash shop (0xD2) responses to the game client.")]
[Guid("2CA85948-E329-4903-872D-EE0800DB5ED6")]
public class CashShopViewPlugIn : ICashShopViewPlugIn
{
    private readonly RemotePlayer _player;

    /// <summary>
    /// Initializes a new instance of the <see cref="CashShopViewPlugIn"/> class.
    /// </summary>
    /// <param name="player">The player.</param>
    public CashShopViewPlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc/>
    public async ValueTask ShowPointInfoAsync(double totalCash, double cashCredit, double cashPrepaid, double totalPoint, double totalMileage)
    {
        await this._player.Connection.SendCashShopPointInfoAsync(0, totalCash, cashCredit, cashPrepaid, totalPoint, totalMileage).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask ShowOpenResultAsync(bool success)
    {
        await this._player.Connection.SendCashShopOpenResultAsync(success).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask ShowStorageCountAsync(ushort totalItemCount, ushort currentPageItemCount, ushort pageIndex, ushort totalPage)
    {
        await this._player.Connection.SendCashShopStorageCountAsync(totalItemCount, currentPageItemCount, pageIndex, totalPage).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask ShowScriptVersionAsync(ushort saleZone, ushort year, ushort yearIdentify)
    {
        await this._player.Connection.SendCashShopScriptVersionAsync(saleZone, year, yearIdentify).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask ShowBannerVersionAsync(ushort bannerZone, ushort year, ushort yearIdentify)
    {
        await this._player.Connection.SendCashShopBannerVersionAsync(bannerZone, year, yearIdentify).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask ShowEventItemCountAsync(ushort itemCount)
    {
        await this._player.Connection.SendCashShopEventItemCountAsync(itemCount).ConfigureAwait(false);
    }
}
