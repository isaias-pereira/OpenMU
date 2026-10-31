// <copyright file="CashShopStorageListRequestHandlerPlugIn.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.MessageHandler.CashShop;

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlayerActions.CashShop;
using MUnique.OpenMU.Network.Packets.ClientToServer;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Handler for the cash shop storage list request (C1 D2 05). The client requests a page of the
/// storage when the shop dialog was opened or when the player uses the storage paging.
/// </summary>
/// <remarks>
/// The client requests the storage list when the shop opens (<c>D2 02</c>) and after every
/// successful purchase (<c>D2 03</c>). The handler answers the page counts (<c>D2 06</c>) and
/// then the items of the requested page (<c>D2 0D</c>).
/// </remarks>
[PlugIn]
[Display(Name = "Cash Shop Storage List Handler", Description = "Answers the cash shop storage list request (D2 05).")]
[Guid("3A4A57D7-1644-4A10-89D6-4E3EBF06C77B")]
[BelongsToGroup(CashShopGroupHandlerPlugIn.GroupKey)]
internal class CashShopStorageListRequestHandlerPlugIn : ISubPacketHandlerPlugIn
{
    private readonly CashShopStorageListAction _action;
    private readonly ILogger<CashShopStorageListRequestHandlerPlugIn> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CashShopStorageListRequestHandlerPlugIn"/> class.
    /// </summary>
    /// <param name="action">The cash shop storage list action.</param>
    /// <param name="logger">The logger (temporary trace instrumentation, Rodada 2S).</param>
    public CashShopStorageListRequestHandlerPlugIn(CashShopStorageListAction action, ILogger<CashShopStorageListRequestHandlerPlugIn> logger)
    {
        this._action = action ?? throw new ArgumentNullException(nameof(action));
        this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public bool IsEncryptionExpected => false;

    /// <inheritdoc/>
    public byte Key => CashShopStorageListRequest.SubCode;

    /// <inheritdoc/>
    public async ValueTask HandlePacketAsync(Player player, Memory<byte> packet)
    {
        var stopwatch = Stopwatch.StartNew();
        if (packet.Length < CashShopStorageListRequest.Length)
        {
            return;
        }

        CashShopStorageListRequest request = packet;
        this._logger.LogInformation(
            "[storage-trace] D2 05 RECEIVED from {Player} at {Timestamp:O}: page={Page}, type={Type}.",
            player.Name,
            DateTimeOffset.UtcNow,
            request.PageIndex,
            request.InventoryType);

        await this._action.RequestListAsync(player, request.PageIndex, request.InventoryType).ConfigureAwait(false);

        this._logger.LogInformation(
            "[storage-trace] D2 05 ANSWERED (D2 06 + D2 0D sent) in {ElapsedMs} ms for {Player}.",
            stopwatch.ElapsedMilliseconds,
            player.Name);
    }
}
