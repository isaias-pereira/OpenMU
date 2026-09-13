// <copyright file="GensViewPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView.Gens;

using System.Linq;
using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Views.Gens;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.Network.Packets.ServerToClient;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// The default implementation of the <see cref="IGensViewPlugIn"/> which is forwarding everything to the game client with the gens packets.
/// </summary>
[PlugIn]
[Display(Name = "Gens View", Description = "Sends the gens state updates to the game client.")]
[Guid("5FC47B2A-1BA3-4D5B-BB94-E0D5BF9CAF9D")]
public class GensViewPlugIn : IGensViewPlugIn
{
    private readonly RemotePlayer _player;

    /// <summary>
    /// Initializes a new instance of the <see cref="GensViewPlugIn"/> class.
    /// </summary>
    /// <param name="player">The player.</param>
    public GensViewPlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc/>
    public async ValueTask ShowGensJoinResponseAsync(bool success, GensService.GensStatus? status)
    {
        var connection = this._player.Connection;
        if (connection is null)
        {
            return;
        }

        var gensType = (byte)(status?.GensType ?? GensType.Undefined);
        await connection.SendGensJoinResponseAsync(success, gensType).ConfigureAwait(false);
        if (success && status is not null)
        {
            await this.UpdateGensInfoAsync(status).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async ValueTask ShowGensLeaveResponseAsync(bool success)
    {
        await this._player.Connection.SendGensLeaveResponseAsync(success).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask UpdateGensInfoAsync(GensService.GensStatus status)
    {
        var connection = this._player.Connection;
        if (connection is null)
        {
            return;
        }

        var contribution = (uint)Math.Max(0, status.ContributionPoints);
        await connection.SendGensInfoAsync((byte)status.GensType, (byte)status.Rank, status.Family, contribution).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask ShowGensRewardResponseAsync(bool success, GensService.GensStatus? status, long rewardZen)
    {
        var connection = this._player.Connection;
        if (connection is null)
        {
            return;
        }

        var gensType = (byte)(status?.GensType ?? GensType.Undefined);
        var rank = (byte)(status?.Rank ?? GensRank.Unranked);
        var family = status?.Family ?? 0;
        var contribution = status?.ContributionPoints is { } points ? (uint)Math.Max(0, points) : 0;
        await connection.SendGensRewardResponseAsync(success, gensType, rank, family, contribution).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask ShowGensRankingAsync(IList<GensService.GensRankingEntry> entries)
    {
        var connection = this._player.Connection;
        if (connection is null)
        {
            return;
        }

        var packetEntries = entries.Select(entry => new GensRankingPacketEntry(
            entry.CharacterName,
            (uint)Math.Max(0, entry.ContributionPoints),
            (byte)entry.Rank,
            entry.Family)).ToList();
        await connection.SendGensRankingResponseAsync(packetEntries).ConfigureAwait(false);
    }
}