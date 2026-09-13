// <copyright file="GensService.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic;

using System.Collections.Concurrent;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.Persistence;

/// <summary>
/// Central service for the Gens system. Keeps an in-memory cache (O(1)) for the
/// active players and persists the membership through the <see cref="GensEntry"/>
/// entity. Follows the same static pattern as <see cref="WCoinService"/> and
/// <see cref="VipService"/>: the methods receive the <c>player.PersistenceContext</c>
/// (which implements <see cref="IContext"/>) and the character identifier.
/// </summary>
/// <remarks>
/// The cache is volatile: it is hydrated when a player enters the world
/// (see GensCacheHydrationPlugIn) and kept up to date on every change.
/// </remarks>
public static class GensService
{
    /// <summary>
    /// The amount of zen a player receives per contribution point when using the gens reward.
    /// </summary>
    public static readonly long RewardZenPerContributionPoint = 1;

    /// <summary>
    /// The contribution points which are required to reach the next gens rank.
    /// Index 0 corresponds to <see cref="GensRank.Rank1"/>.
    /// </summary>
    public static readonly long[] RankThresholds = { 0, 1_000, 3_000, 6_000, 12_000, 25_000 };

    /// <summary>
    /// The maximum number of entries in a gens ranking response.
    /// </summary>
    public static readonly int MaximumRankingEntries = 50;

    private static readonly ConcurrentDictionary<Guid, GensStatus> Cache = new();

    /// <summary>
    /// Gets the in-memory gens status of a character, if it is cached.
    /// </summary>
    /// <param name="characterId">The character identifier.</param>
    /// <returns>The gens status; <c>null</c> if the character is not in a gens or not cached.</returns>
    public static GensStatus? GetGensStatus(Guid characterId)
    {
        return Cache.TryGetValue(characterId, out var status) ? status : null;
    }

    /// <summary>
    /// Loads the persisted gens entry of a character into the cache. Used when a player enters the world.
    /// </summary>
    /// <param name="context">The persistence context.</param>
    /// <param name="characterId">The character identifier.</param>
    public static async ValueTask LoadIntoCacheAsync(IContext context, Guid characterId)
    {
        var entry = await GetEntryAsync(context, characterId).ConfigureAwait(false);
        if (entry is { } gensEntry && gensEntry.GensType != GensType.Undefined)
        {
            Cache[characterId] = new GensStatus(gensEntry.GensType, gensEntry.Rank, gensEntry.Family, gensEntry.ContributionPoints);
        }
        else
        {
            Cache.TryRemove(characterId, out _);
        }
    }

    /// <summary>
    /// Removes a character from the cache, e.g. when it leaves the world.
    /// </summary>
    /// <param name="characterId">The character identifier.</param>
    public static void UnloadFromCache(Guid characterId)
    {
        Cache.TryRemove(characterId, out _);
    }

    /// <summary>
    /// Joins a character to the given gens. Fails if the character is already in a gens.
    /// </summary>
    /// <param name="context">The persistence context.</param>
    /// <param name="characterId">The character identifier.</param>
    /// <param name="gensType">The gens type to join.</param>
    /// <returns><c>True</c>, if the join was successful; otherwise, <c>false</c> (e.g. already in a gens or invalid type).</returns>
    public static async ValueTask<bool> JoinAsync(IContext context, Guid characterId, GensType gensType)
    {
        if (gensType == GensType.Undefined)
        {
            return false;
        }

        if (GetGensStatus(characterId) is not null)
        {
            // already in a gens - do not allow to join another one, and do not reset the points.
            return false;
        }

        var entry = await GetEntryAsync(context, characterId).ConfigureAwait(false) ?? context.CreateNew<GensEntry>(characterId);
        entry.GensType = gensType;
        entry.Rank = GetRank(entry.ContributionPoints);
        entry.Family = 0;
        entry.JoinDate = DateTime.UtcNow;

        if (!await context.SaveChangesAsync().ConfigureAwait(false))
        {
            return false;
        }

        Cache[characterId] = new GensStatus(entry.GensType, entry.Rank, entry.Family, entry.ContributionPoints);
        return true;
    }

    /// <summary>
    /// Lets a character leave the current gens. The accumulated contribution points are lost.
    /// </summary>
    /// <param name="context">The persistence context.</param>
    /// <param name="characterId">The character identifier.</param>
    /// <returns><c>True</c>, if the character was in a gens and left it successfully; otherwise, <c>false</c>.</returns>
    public static async ValueTask<bool> LeaveAsync(IContext context, Guid characterId)
    {
        var entry = await GetEntryAsync(context, characterId).ConfigureAwait(false);
        if (entry is null)
        {
            return false;
        }

        if (!await context.DeleteAsync(entry).ConfigureAwait(false))
        {
            return false;
        }

        Cache.TryRemove(characterId, out _);
        return true;
    }

    /// <summary>
    /// Adds contribution points to the character's gens membership and persists the new total.
    /// </summary>
    /// <param name="context">The persistence context.</param>
    /// <param name="characterId">The character identifier.</param>
    /// <param name="amount">The amount of contribution points to add (must be positive).</param>
    /// <returns>The new contribution total; or <c>-1</c> if the character is not member of a gens.</returns>
    public static async ValueTask<long> AddContributionAsync(IContext context, Guid characterId, long amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "The contribution amount must be positive.");
        }

        var status = GetGensStatus(characterId);
        if (status is null)
        {
            // not in a gens - could be an offline character; try to load the persisted entry.
            await LoadIntoCacheAsync(context, characterId).ConfigureAwait(false);
            status = GetGensStatus(characterId);
            if (status is null)
            {
                return -1;
            }
        }

        var entry = await GetEntryAsync(context, characterId).ConfigureAwait(false) ?? context.CreateNew<GensEntry>(characterId);
        entry.GensType = status.GensType;
        entry.ContributionPoints = checked(entry.ContributionPoints + amount);
        entry.Rank = GetRank(entry.ContributionPoints);

        if (!await context.SaveChangesAsync().ConfigureAwait(false))
        {
            return status.ContributionPoints;
        }

        var newStatus = new GensStatus(entry.GensType, entry.Rank, entry.Family, entry.ContributionPoints);
        Cache[characterId] = newStatus;
        return newStatus.ContributionPoints;
    }

    /// <summary>
    /// Resets the accumulated contribution points of a character to 0 (used by the gens reward exchange).
    /// </summary>
    /// <param name="context">The persistence context.</param>
    /// <param name="characterId">The character identifier.</param>
    /// <returns><c>True</c>, if the points could be reset; otherwise, <c>false</c>.</returns>
    public static async ValueTask<bool> ResetContributionAsync(IContext context, Guid characterId)
    {
        var entry = await GetEntryAsync(context, characterId).ConfigureAwait(false);
        if (entry is null)
        {
            return false;
        }

        entry.ContributionPoints = 0;
        entry.Rank = GetRank(0);

        if (!await context.SaveChangesAsync().ConfigureAwait(false))
        {
            return false;
        }

        Cache[characterId] = new GensStatus(entry.GensType, entry.Rank, entry.Family, 0);
        return true;
    }

    /// <summary>
    /// Gets the current gens ranking for the given gens, sorted by contribution points (descending).
    /// </summary>
    /// <param name="context">The persistence context.</param>
    /// <param name="gensType">The gens type.</param>
    /// <returns>The ranking entries, limited to <see cref="MaximumRankingEntries"/>.</returns>
    public static async ValueTask<IList<GensRankingEntry>> GetRankingAsync(IContext context, GensType gensType)
    {
        IEnumerable<GensEntry> allEntries = await context.GetAsync<GensEntry>().ConfigureAwait(false);
        var result = new List<GensRankingEntry>();
        var rankedEntries = allEntries
            .Where(entry => entry.GensType == gensType)
            .OrderByDescending(entry => entry.ContributionPoints)
            .Take(MaximumRankingEntries);
        foreach (var entry in rankedEntries)
        {
            var character = await context.GetByIdAsync<Character>(entry.Id).ConfigureAwait(false);
            result.Add(new GensRankingEntry(character?.Name ?? "Unknown", entry.GensType, entry.Rank, entry.Family, entry.ContributionPoints));
        }

        return result;
    }

    /// <summary>
    /// Gets the rank which belongs to the given amount of contribution points.
    /// </summary>
    /// <param name="contributionPoints">The contribution points.</param>
    /// <returns>The rank.</returns>
    public static GensRank GetRank(long contributionPoints)
    {
        byte rank = 0;
        for (var i = 0; i < RankThresholds.Length; i++)
        {
            if (contributionPoints >= RankThresholds[i])
            {
                rank = (byte)(i + 1);
            }
            else
            {
                break;
            }
        }

        return (GensRank)rank;
    }

    /// <summary>
    /// Gets the persisted gens entry of a character.
    /// </summary>
    private static async ValueTask<GensEntry?> GetEntryAsync(IContext context, Guid characterId)
    {
        return await context.GetByIdAsync<GensEntry>(characterId).ConfigureAwait(false);
    }

    /// <summary>
    /// Immutable snapshot of the gens membership of a character.
    /// </summary>
    public record GensStatus(GensType GensType, GensRank Rank, byte Family, long ContributionPoints);

    /// <summary>
    /// A single entry of the gens ranking.
    /// </summary>
    public record GensRankingEntry(string CharacterName, GensType GensType, GensRank Rank, byte Family, long ContributionPoints);

}