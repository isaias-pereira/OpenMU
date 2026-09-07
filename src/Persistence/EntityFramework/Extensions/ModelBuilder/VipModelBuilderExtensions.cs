// <copyright file="VipModelBuilderExtensions.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace MUnique.OpenMU.Persistence.EntityFramework.Extensions.ModelBuilder;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MUnique.OpenMU.Persistence.EntityFramework.Model;

internal static class VipPlanExtensions
{
    public static void Apply(this EntityTypeBuilder<VipPlan> builder)
    {
        builder.HasIndex(p => p.IsActive);
    }
}

internal static class AccountVipExtensions
{
    public static void Apply(this EntityTypeBuilder<AccountVip> builder)
    {
        // Shared Primary Key: O Id é a própria AccountId. Garante 1-para-1 e Upsert natural.
        builder.HasOne<Account>()
            .WithOne()
            .HasForeignKey<AccountVip>(v => v.Id)
            .OnDelete(DeleteBehavior.Cascade);
            
        builder.HasOne<VipPlan>()
            .WithMany()
            .HasForeignKey(v => v.VipPlanId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal static class VipHistoryExtensions
{
    public static void Apply(this EntityTypeBuilder<VipHistory> builder)
    {
        builder.HasIndex(h => h.AccountId);
        builder.HasIndex(h => h.CreatedAt);
        
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(h => h.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}