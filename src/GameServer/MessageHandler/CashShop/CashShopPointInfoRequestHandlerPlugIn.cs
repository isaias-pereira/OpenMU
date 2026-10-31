// <copyright file="CashShopPointInfoRequestHandlerPlugIn.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.MessageHandler.CashShop;

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlayerActions.CashShop;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Handler for the cash shop point info request (C1 D2 01). The client asks for the available
/// cash shop currencies of the account. It sends this request automatically after it received
/// a successful shop open result (D2 02).
/// </summary>
[PlugIn]
[Display(Name = "Cash Shop Point Info Handler", Description = "Answers the cash shop point info request (D2 01).")]
[Guid("8A604A21-C046-4D56-BD03-8922370FE25F")]
[BelongsToGroup(CashShopGroupHandlerPlugIn.GroupKey)]
internal class CashShopPointInfoRequestHandlerPlugIn : ISubPacketHandlerPlugIn
{
    private readonly CashShopPointInfoAction _action = new();
    private readonly ILogger<CashShopPointInfoRequestHandlerPlugIn> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CashShopPointInfoRequestHandlerPlugIn"/> class.
    /// </summary>
    /// <param name="logger">The logger (temporary trace instrumentation, Rodada 2S).</param>
    public CashShopPointInfoRequestHandlerPlugIn(ILogger<CashShopPointInfoRequestHandlerPlugIn> logger)
    {
        this._logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public bool IsEncryptionExpected => false;

    /// <inheritdoc/>
    public byte Key => 0x01;

    /// <inheritdoc/>
    public async ValueTask HandlePacketAsync(Player player, Memory<byte> packet)
    {
        var stopwatch = Stopwatch.StartNew();
        this._logger.LogInformation("[wcoin-trace] D2 01 RECEIVED from {Player} at {Timestamp:O}.", player.Name, DateTimeOffset.UtcNow);
        await this._action.SetPointInfoAsync(player).ConfigureAwait(false);
        this._logger.LogInformation("[wcoin-trace] D2 01 ANSWERED in {ElapsedMs} ms for {Player}.", stopwatch.ElapsedMilliseconds, player.Name);
    }
}
