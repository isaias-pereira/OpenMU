// <copyright file="CashShopItemBuyRequestHandlerPlugIn.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.MessageHandler.CashShop;

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlayerActions.CashShop;
using MUnique.OpenMU.GameLogic.Views.CashShop;
using MUnique.OpenMU.Network.Packets.ClientToServer;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Handles the cash shop item purchase request (C1 D2 03).
/// </summary>
[PlugIn]
[Display(Name = "Cash Shop Item Buy Handler", Description = "Handles a cash shop item purchase request (D2 03).")]
[Guid("4A6B3B4D-3B0D-4C1E-9C1E-7B2D7D3B5A11")]
[BelongsToGroup(CashShopGroupHandlerPlugIn.GroupKey)]
internal class CashShopItemBuyRequestHandlerPlugIn : ISubPacketHandlerPlugIn
{
    private readonly CashShopBuyAction _action;
    private readonly ILogger<CashShopItemBuyRequestHandlerPlugIn> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CashShopItemBuyRequestHandlerPlugIn"/> class.
    /// </summary>
    /// <param name="action">The cash shop buy action.</param>
    /// <param name="logger">The logger (temporary trace instrumentation, Rodada 2S).</param>
    public CashShopItemBuyRequestHandlerPlugIn(CashShopBuyAction action, ILogger<CashShopItemBuyRequestHandlerPlugIn> logger)
    {
        this._action = action ?? throw new ArgumentNullException(nameof(action));
        this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool IsEncryptionExpected => false;

    /// <inheritdoc />
    public byte Key => CashShopItemBuyRequest.SubCode;

    /// <inheritdoc />
    public async ValueTask HandlePacketAsync(Player player, Memory<byte> packet)
    {
        var stopwatch = Stopwatch.StartNew();
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        if (packet.Length < CashShopItemBuyRequest.Length)
        {
            this._logger.LogWarning(
                "[buy-trace {CorrelationId}] D2 03 DROPPED: length {Length} < {Expected}.",
                correlationId,
                packet.Length,
                CashShopItemBuyRequest.Length);
            return;
        }

        CashShopItemBuyRequest request = packet;
        this._logger.LogInformation(
            "[buy-trace {CorrelationId}] D2 03 RECEIVED from {Player} at {Timestamp:O}: package={Package}, category={Category}, productMain={Product}, item={Item}, coin={Coin}, mileage={Mileage}.",
            correlationId,
            player.Name,
            DateTimeOffset.UtcNow,
            request.PackageMainIndex,
            request.Category,
            request.ProductMainIndex,
            request.ItemIndex,
            request.CoinIndex,
            request.MileageFlag);

        if (request.PackageMainIndex > int.MaxValue ||
            request.Category > int.MaxValue ||
            request.ProductMainIndex > int.MaxValue ||
            request.CoinIndex > int.MaxValue)
        {
            await player.InvokeViewPlugInAsync<ICashShopViewPlugIn>(
                p => p.ShowBuyResultAsync(false, (int)PurchaseErrorCode.SelectionUnavailable, null)).ConfigureAwait(false);
            this._logger.LogWarning("[buy-trace {CorrelationId}] refused: field overflow.", correlationId);
            return;
        }

        await this._action.HandleBuyAsync(
            player,
            (int)request.PackageMainIndex,
            (int)request.Category,
            (int)request.ProductMainIndex,
            request.ItemIndex,
            (int)request.CoinIndex,
            request.MileageFlag != 0).ConfigureAwait(false);

        this._logger.LogInformation(
            "[buy-trace {CorrelationId}] D2 03 COMPLETED in {ElapsedMs} ms (result logged by the buy service).",
            correlationId,
            stopwatch.ElapsedMilliseconds);
    }
}
