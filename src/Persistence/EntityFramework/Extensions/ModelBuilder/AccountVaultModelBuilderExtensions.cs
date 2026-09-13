// <copyright file="AccountVaultModelBuilderExtensions.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
namespace MUnique.OpenMU.Persistence.EntityFramework.Extensions.ModelBuilder;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MUnique.OpenMU.Persistence.EntityFramework.Model;

internal static class AccountVaultExtensions
{
    public static void Apply(this EntityTypeBuilder<AccountVault> builder)
    {
        // AccountId is a shadow foreign key created by the generated cascade configuration
        // (Account.RawVaults -> WithOne -> Cascade), same convention as Character.
        builder.HasIndex("AccountId", nameof(AccountVault.Number)).IsUnique();
        builder.HasIndex("StorageId");
    }
}
