// <copyright file="IGensViewPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.Gens;

/// <summary>
/// Interface of a view whose implementation informs about the gens state of a player.
/// </summary>
public interface IGensViewPlugIn : IViewPlugIn
{
    /// <summary>
    /// Shows the result of a join request.
    /// </summary>
    /// <param name="success">If set to <c>true</c>, the join was successful.</param>
    /// <param name="status">The current gens status (may be <c>null</c> if not in a gens anymore).</param>
    ValueTask ShowGensJoinResponseAsync(bool success, GensService.GensStatus? status);

    /// <summary>
    /// Shows the result of a leave request.
    /// </summary>
    /// <param name="success">If set to <c>true</c>, the leave was successful.</param>
    ValueTask ShowGensLeaveResponseAsync(bool success);

    /// <summary>
    /// Updates the gens information (type, rank, family and contribution points) on the client.
    /// </summary>
    /// <param name="status">The current gens status.</param>
    ValueTask UpdateGensInfoAsync(GensService.GensStatus status);

    /// <summary>
    /// Shows the result of a reward request.
    /// </summary>
    /// <param name="success">If set to <c>true</c>, the reward was granted.</param>
    /// <param name="status">The current gens status after the reward request.</param>
    /// <param name="rewardZen">The amount of zen which was granted.</param>
    ValueTask ShowGensRewardResponseAsync(bool success, GensService.GensStatus? status, long rewardZen);

    /// <summary>
    /// Shows the gens ranking.
    /// </summary>
    /// <param name="entries">The ranking entries.</param>
    ValueTask ShowGensRankingAsync(IList<GensService.GensRankingEntry> entries);
}