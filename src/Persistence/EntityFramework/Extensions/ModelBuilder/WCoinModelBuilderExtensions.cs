// <copyright file="WCoinModelBuilderExtensions.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.EntityFramework.Extensions.ModelBuilder;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MUnique.OpenMU.Persistence.EntityFramework.Model;

/// <summary>
/// Extensions for the <see cref="EntityTypeBuilder{WCoinWallet}"/>.
/// </summary>
internal static class WCoinWalletExtensions
{
    /// <summary>
    /// Applies the settings for the <see cref="WCoinWallet"/> entity.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static void Apply(this EntityTypeBuilder<WCoinWallet> builder)
    {
        // Nunca deixa saldo negativo entrar no banco, nem que um bug na aplicação tente.
        builder.ToTable(t => t.HasCheckConstraint("CK_WCoinWallet_Balance", "\"Balance\" >= 0"));

        // Chave compartilhada com a Account (mesmo Id) -> FK real de verdade a nível de
        // banco, sem precisar tocar em Account.cs nem criar coluna de FK a mais.
        builder.HasOne<Account>()
            .WithOne()
            .HasForeignKey<WCoinWallet>(wallet => wallet.Id)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Extensions for the <see cref="EntityTypeBuilder{WCoinTransaction}"/>.
/// </summary>
internal static class WCoinTransactionExtensions
{
    /// <summary>
    /// Applies the settings for the <see cref="WCoinTransaction"/> entity.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static void Apply(this EntityTypeBuilder<WCoinTransaction> builder)
    {
        builder.Property(t => t.Description).HasMaxLength(255);

        builder.HasIndex(t => t.AccountId);
        builder.HasIndex(t => t.CreatedAt);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(t => t.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
