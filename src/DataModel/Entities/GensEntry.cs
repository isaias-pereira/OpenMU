// <copyright file="GensEntry.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.DataModel.Entities;

/// <summary>
/// The gens side a character can belong to.
/// </summary>
public enum GensType : byte
{
    /// <summary>
    /// The character is not in any gens.
    /// </summary>
    Undefined = 0,

    /// <summary>
    /// The Duprian gens.
    /// </summary>
    Duprian = 1,

    /// <summary>
    /// The Vanert gens.
    /// </summary>
    Vanert = 2,
}

/// <summary>
/// The contribution rank of a character within its gens. The rank is derived
/// from the accumulated contribution points.
/// </summary>
public enum GensRank : byte
{
    /// <summary>
    /// The default rank of a new/unknow gens member.
    /// </summary>
    Unranked = 0,

    /// <summary>
    /// The first rank (novice).
    /// </summary>
    Rank1 = 1,

    /// <summary>
    /// The second rank.
    /// </summary>
    Rank2 = 2,

    /// <summary>
    /// The third rank.
    /// </summary>
    Rank3 = 3,

    /// <summary>
    /// The fourth rank.
    /// </summary>
    Rank4 = 4,

    /// <summary>
    /// The fifth rank.
    /// </summary>
    Rank5 = 5,

    /// <summary>
    /// The sixth rank (highest).
    /// </summary>
    Rank6 = 6,
}

/// <summary>
/// The gens membership of a character. Relation is 1-to-1 using a shared primary key,
/// so the <see cref="Id"/> is always the same as the <see cref="Character.Id"/>.
/// </summary>
[AggregateRoot]
public class GensEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GensEntry"/> class.
    /// </summary>
    public GensEntry()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GensEntry"/> class with the specified identifier.
    /// </summary>
    /// <param name="id">The identifier, which is the same as the <see cref="Character.Id"/>.</param>
    public GensEntry(Guid id)
    {
        this.Id = id;
    }

    /// <summary>
    /// Gets or sets the identifier. Is always the same as the <see cref="Character.Id"/>.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the gens type the character belongs to.
    /// </summary>
    public GensType GensType { get; set; }

    /// <summary>
    /// Gets or sets the current contribution rank.
    /// </summary>
    public GensRank Rank { get; set; }

    /// <summary>
    /// Gets or sets the gens family (0-3).
    /// </summary>
    public byte Family { get; set; }

    /// <summary>
    /// Gets or sets the accumulated contribution points.
    /// </summary>
    public long ContributionPoints { get; set; }

    /// <summary>
    /// Gets or sets the date when the character joined the gens.
    /// </summary>
    public DateTime JoinDate { get; set; } = DateTime.UtcNow;
}