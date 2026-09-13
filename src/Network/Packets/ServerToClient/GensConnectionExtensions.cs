// <copyright file="GensConnectionExtensions.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Network.Packets.ServerToClient;

using System.Collections.Generic;
using MUnique.OpenMU.Network;

/// <summary>
/// A single entry of the gens ranking packet.
/// </summary>
public readonly record struct GensRankingPacketEntry(string CharacterName, uint ContributionPoints, byte Rank, byte Family);

/// <summary>
/// Server-to-client packets of the Gens system (packet type 0xF8).
/// </summary>
/// <remarks>
/// Note: these packets were not part of the upstream packet definition files, so they are
/// written here by hand. The layouts follow the commonly used Season 6 community specification.
/// </remarks>
public static class GensConnectionExtensions
{
    /// <summary>
    /// Sends a <see cref="GensConnectionExtensions"/> join response (C1 F8 01) to the client.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="result">If set to <c>true</c>, the join was successful.</param>
    /// <param name="gensType">The gens type.</param>
    public static async ValueTask SendGensJoinResponseAsync(this IConnection? connection, bool result, byte gensType)
    {
        if (connection is null)
        {
            return;
        }

        int WritePacket()
        {
            const byte length = 6;
            var span = connection.Output.GetSpan(length);
            span[0] = 0xC1;
            span[1] = length;
            span[2] = 0xF8;
            span[3] = 0x01;
            span[4] = result ? (byte)1 : (byte)0;
            span[5] = gensType;
            return length;
        }

        await connection.SendAsync(WritePacket).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends the gens info update (C1 F8 02) to the client, e.g. after joining or after a contribution change.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="gensType">The gens type.</param>
    /// <param name="rank">The current gens rank.</param>
    /// <param name="family">The gens family.</param>
    /// <param name="contributionPoints">The accumulated contribution points.</param>
    public static async ValueTask SendGensInfoAsync(this IConnection? connection, byte gensType, byte rank, byte family, uint contributionPoints)
    {
        if (connection is null)
        {
            return;
        }

        int WritePacket()
        {
            const byte length = 11;
            var span = connection.Output.GetSpan(length);
            span[0] = 0xC1;
            span[1] = length;
            span[2] = 0xF8;
            span[3] = 0x02;
            span[4] = gensType;
            span[5] = rank;
            span[6] = family;
            span[7] = (byte)(contributionPoints & 0xFF);
            span[8] = (byte)((contributionPoints >> 8) & 0xFF);
            span[9] = (byte)((contributionPoints >> 16) & 0xFF);
            span[10] = (byte)((contributionPoints >> 24) & 0xFF);
            return length;
        }

        await connection.SendAsync(WritePacket).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a leave response (C1 F8 03) to the client.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="result">If set to <c>true</c>, the leave was successful.</param>
    public static async ValueTask SendGensLeaveResponseAsync(this IConnection? connection, bool result)
    {
        if (connection is null)
        {
            return;
        }

        int WritePacket()
        {
            const byte length = 6;
            var span = connection.Output.GetSpan(length);
            span[0] = 0xC1;
            span[1] = length;
            span[2] = 0xF8;
            span[3] = 0x03;
            span[4] = result ? (byte)1 : (byte)0;
            span[5] = 0;
            return length;
        }

        await connection.SendAsync(WritePacket).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a reward response (C1 F8 09) to the client.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="result">If set to <c>true</c>, the reward was granted.</param>
    /// <param name="gensType">The gens type.</param>
    /// <param name="rank">The current gens rank after the reward.</param>
    /// <param name="family">The gens family.</param>
    /// <param name="contributionPoints">The remaining contribution points after the reward.</param>
    public static async ValueTask SendGensRewardResponseAsync(this IConnection? connection, bool result, byte gensType, byte rank, byte family, uint contributionPoints)
    {
        if (connection is null)
        {
            return;
        }

        int WritePacket()
        {
            const byte length = 12;
            var span = connection.Output.GetSpan(length);
            span[0] = 0xC1;
            span[1] = length;
            span[2] = 0xF8;
            span[3] = 0x09;
            span[4] = result ? (byte)1 : (byte)0;
            span[5] = gensType;
            span[6] = rank;
            span[7] = family;
            span[8] = (byte)(contributionPoints & 0xFF);
            span[9] = (byte)((contributionPoints >> 8) & 0xFF);
            span[10] = (byte)((contributionPoints >> 16) & 0xFF);
            span[11] = (byte)((contributionPoints >> 24) & 0xFF);
            return length;
        }

        await connection.SendAsync(WritePacket).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends the gens ranking (C1 F8 0B) to the client.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="entries">The ranking entries.</param>
    public static async ValueTask SendGensRankingResponseAsync(this IConnection? connection, IList<GensRankingPacketEntry> entries)
    {
        if (connection is null)
        {
            return;
        }

        int WritePacket()
        {
            var count = (byte)Math.Min(entries.Count, byte.MaxValue);
            var length = (byte)(5 + (count * 16));
            var span = connection.Output.GetSpan(length);
            span[0] = 0xC1;
            span[1] = length;
            span[2] = 0xF8;
            span[3] = 0x0B;
            span[4] = count;

            var offset = 5;
            for (var i = 0; i < count; i++)
            {
                var entry = entries[i];
                for (var c = 0; c < 10; c++)
                {
                    span[offset + c] = c < entry.CharacterName.Length ? (byte)entry.CharacterName[c] : (byte)0;
                }

                span[offset + 10] = (byte)(entry.ContributionPoints & 0xFF);
                span[offset + 11] = (byte)((entry.ContributionPoints >> 8) & 0xFF);
                span[offset + 12] = (byte)((entry.ContributionPoints >> 16) & 0xFF);
                span[offset + 13] = (byte)((entry.ContributionPoints >> 24) & 0xFF);
                span[offset + 14] = entry.Rank;
                span[offset + 15] = entry.Family;
                offset += 16;
            }

            return length;
        }

        await connection.SendAsync(WritePacket).ConfigureAwait(false);
    }
}