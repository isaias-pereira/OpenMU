// <copyright file="QuizModelBuilderExtensions.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.EntityFramework.Extensions.ModelBuilder;

using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MUnique.OpenMU.Persistence.EntityFramework.Model;

/// <summary>
/// Extensions for the quiz entities configuration.
/// As the quiz entities only store Guid references (no navigation properties), the
/// foreign keys are declared exclusively in the migration DDL; here we only configure
/// column constraints and the indexes used by queries and filters.
/// </summary>
internal static class QuizModelBuilderExtensions
{
    /// <summary>
    /// Applies the settings for the <see cref="QuizConfiguration"/> entity (single-row configuration).
    /// </summary>
    public static void Apply(this EntityTypeBuilder<QuizConfiguration> builder)
    {
        builder.HasIndex(c => c.IsEnabled);
    }

    /// <summary>
    /// Applies the settings for the <see cref="QuizQuestion"/> entity.
    /// </summary>
    public static void Apply(this EntityTypeBuilder<QuizQuestion> builder)
    {
        builder.Property(q => q.Question).IsRequired().HasMaxLength(500);
        builder.Property(q => q.Category).HasMaxLength(100);
        builder.HasIndex(q => q.IsActive);
        builder.HasIndex(q => q.Category);
        builder.HasIndex(q => q.Difficulty);
    }

    /// <summary>
    /// Applies the settings for the <see cref="QuizAnswer"/> entity.
    /// </summary>
    public static void Apply(this EntityTypeBuilder<QuizAnswer> builder)
    {
        builder.Property(a => a.OriginalAnswer).HasMaxLength(255);
        builder.Property(a => a.NormalizedAnswer).IsRequired().HasMaxLength(255);
        builder.HasIndex(a => a.QuestionId);
    }

    /// <summary>
    /// Applies the settings for the <see cref="QuizRound"/> entity.
    /// </summary>
    public static void Apply(this EntityTypeBuilder<QuizRound> builder)
    {
        builder.HasIndex(r => r.Status);
        builder.HasIndex(r => r.StartedAt);
    }

    /// <summary>
    /// Applies the settings for the <see cref="QuizRoundQuestion"/> entity.
    /// </summary>
    public static void Apply(this EntityTypeBuilder<QuizRoundQuestion> builder)
    {
        builder.HasIndex(rq => rq.RoundId);
        builder.HasIndex(rq => rq.QuestionId);
    }

    /// <summary>
    /// Applies the settings for the <see cref="QuizWinner"/> entity.
    /// </summary>
    public static void Apply(this EntityTypeBuilder<QuizWinner> builder)
    {
        builder.HasIndex(w => w.AccountId);
        builder.HasIndex(w => w.CharacterId);
        builder.HasIndex(w => w.CreatedAt);
    }
}
