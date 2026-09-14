// <copyright file="CashShopPointInfoRequestHandlerPlugIn.cs" company="MUnique">
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.MessageHandler.CashShop;

using System.Runtime.InteropServices;
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

    /// <inheritdoc/>
    public bool IsEncryptionExpected => false;

    /// <inheritdoc/>
    public byte Key => 0x01;

    /// <inheritdoc/>
    public async ValueTask HandlePacketAsync(Player player, Memory<byte> packet)
    {
        await this._action.SetPointInfoAsync(player).ConfigureAwait(false);
    }
}
