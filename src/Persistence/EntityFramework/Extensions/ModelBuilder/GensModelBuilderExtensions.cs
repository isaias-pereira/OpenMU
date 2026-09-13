// <copyright file="GensModelBuilderExtensions.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.EntityFramework.Extensions.ModelBuilder;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MUnique.OpenMU.Persistence.EntityFramework.Model;

/// <summary>
/// Extensions for the <see cref="EntityTypeBuilder{GensEntry}"/>.
/// </summary>
internal static class GensEntryExtensions
{
    /// <summary>
    /// Applies the settings for the <see cref="GensEntry"/> entity.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static void Apply(this EntityTypeBuilder<GensEntry> builder)
    {
        // Shared primary key: o Id é a própria CharacterId. Garante 1-para-1 e Upsert natural.
        builder.HasOne<Character>()
            .WithOne()
            .HasForeignKey<GensEntry>(g => g.Id)
            .OnDelete(DeleteBehavior.Cascade);

        // Índices para o ranking e para listagem por lado.
        builder.HasIndex(g => g.GensType);
        builder.HasIndex(g => g.ContributionPoints);
    }
}