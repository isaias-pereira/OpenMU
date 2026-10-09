// <copyright file="CashShopStorageItemConsumeRequestHandlerPlugIn.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.MessageHandler.CashShop;

using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlayerActions.CashShop;
using MUnique.OpenMU.GameLogic.Views.CashShop;
using MUnique.OpenMU.Network.Packets.ClientToServer;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Handles the cash shop storage item use request (C1 D2 0B), by which the player claims an item
/// from the cash shop storage into the character inventory.
/// </summary>
/// <remarks>
/// The client identifies the storage entry by the <c>(StorageSeq, StorageItemSeq)</c> pair it
/// received in the D2 0D line — the fields the OpenMU packet struct misleadingly names
/// <c>BaseItemCode</c>/<c>MainItemCode</c>. The item code carried in the request
/// (<c>ItemIndex</c>) is derived from the client's local catalog and is <b>not</b> the authority:
/// the server resolves the row against the authenticated account's persisted rows only. The
/// result code of the response is dictated by <c>ReceiveIGS_UseStorageItem</c> in the client.
/// </remarks>
[PlugIn]
[Display(Name = "Cash Shop Storage Item Use Handler", Description = "Handles a cash shop storage item use (claim) request (D2 0B).")]
[Guid("5B7C4C5E-4C1E-4D1E-9D1E-8C3E8C4C6B12")]
[BelongsToGroup(CashShopGroupHandlerPlugIn.GroupKey)]
internal class CashShopStorageItemConsumeRequestHandlerPlugIn : ISubPacketHandlerPlugIn
{
    private readonly CashShopStorageClaimService _claimService;
    private readonly ILogger<CashShopStorageItemConsumeRequestHandlerPlugIn> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CashShopStorageItemConsumeRequestHandlerPlugIn"/> class.
    /// </summary>
    /// <param name="claimService">The cash shop storage claim service.</param>
    /// <param name="logger">The logger.</param>
    public CashShopStorageItemConsumeRequestHandlerPlugIn(CashShopStorageClaimService claimService, ILogger<CashShopStorageItemConsumeRequestHandlerPlugIn> logger)
    {
        this._claimService = claimService ?? throw new ArgumentNullException(nameof(claimService));
        this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool IsEncryptionExpected => false;

    /// <inheritdoc />
    public byte Key => CashShopStorageItemConsumeRequest.SubCode;

    /// <inheritdoc />
    public async ValueTask HandlePacketAsync(Player player, Memory<byte> packet)
    {
        if (packet.Length < 13)
        {
            return;
        }

        CashShopStorageItemConsumeRequest request = packet;
        if (request.BaseItemCode > int.MaxValue || request.MainItemCode > int.MaxValue)
        {
            await this.SendResultAsync(player, StorageClaimErrorCode.StorageNotFound).ConfigureAwait(false);
            return;
        }

        this._logger.LogDebug(
            "Cash shop storage item use request from {Player}: storage seq {StorageSeq}, item seq {StorageItemSeq}, client item code {ItemCode}, type {ProductType}.",
            player.Name,
            request.BaseItemCode,
            request.MainItemCode,
            request.ItemIndex,
            request.ProductType);

        var result = await this._claimService.ClaimAsync(
            player,
            (int)request.BaseItemCode,
            (int)request.MainItemCode,
            request.ProductType).ConfigureAwait(false);

        await this.SendResultAsync(player, result.ErrorCode).ConfigureAwait(false);
    }

    private async ValueTask SendResultAsync(Player player, StorageClaimErrorCode errorCode)
    {
        // The view sends the D2 0B response alone, which is what the use flow expects: the
        // client renders the result itself (message boxes) and re-requests the storage list on
        // success, so no other response is sent here.
        await player.InvokeViewPlugInAsync<ICashShopViewPlugIn>(p => p.ShowStorageItemUseResultAsync(errorCode)).ConfigureAwait(false);
        if (errorCode != StorageClaimErrorCode.Success)
        {
            this._logger.LogInformation(
                "Cash shop storage item use refused for {Player} with code {ErrorCode}.",
                player.Name,
                (int)errorCode);
        }
    }
}
